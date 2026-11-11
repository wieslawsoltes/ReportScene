import { expect } from '@playwright/test';
import { state } from './uno-driver.mjs';

const offsetKeys = ['HorizontalOffset', 'VerticalOffset'];
const sizeKeys = ['Width', 'Height'];
const extentKeys = ['ExtentWidth', 'ExtentHeight'];

// An idle snapshot can precede delivery of an already-dispatched wheel input.
// After a wheel, require a newer native event and motion on every requested,
// movable axis, then the final event and stable native/paper geometry.
export async function waitForScrollSettled(page, viewportKey, afterWheel) {
  let previousSequence = -1, previousPosition = null, stable = 0, settled;
  const paperKey = viewportKey === 'DesignViewport' ? 'design-paper' : 'preview-paper';
  await expect.poll(async () => {
    const snapshot = await state(page), viewport = snapshot?.[viewportKey];
    if (!viewport || typeof viewport.IsIntermediate !== 'boolean' || !Number.isSafeInteger(viewport.ViewChangeVersion)) {
      throw new Error('Missing native scroll evidence: ' + viewportKey);
    }
    if (snapshot.SnapshotSequence === previousSequence) return false;
    previousSequence = snapshot.SnapshotSequence;
    const acknowledged = !afterWheel || (viewport.ViewChangeVersion > afterWheel.version
      && offsetKeys.every((key, axis) => afterWheel.deltas[axis] === 0
        || (viewport[key] - afterWheel.offsets[axis]) * Math.sign(afterWheel.deltas[axis]) > .01));
    if (!acknowledged || viewport.IsIntermediate) {
      stable = 0; previousPosition = null; return false;
    }
    const paper = snapshot.Commands[paperKey];
    if (!paper) throw new Error('Missing native paper geometry: ' + paperKey);
    const position = [viewport.HorizontalOffset, viewport.VerticalOffset, viewport.Width, viewport.Height,
      viewport.ExtentWidth, viewport.ExtentHeight, viewport.ViewChangeVersion, paper.X, paper.Y, paper.Width, paper.Height];
    stable = previousPosition && position.every((value, axis) => Math.abs(value - previousPosition[axis]) < .01)
      ? stable + 1 : 0;
    previousPosition = position;
    settled = snapshot;
    return stable >= 2;
  }, { message: viewportKey + ' acknowledges the wheel and finishes native scrolling', intervals: [100, 200, 400] }).toBe(true);
  return settled;
}

// Corrections are desired screen-DIP movements. Native wheel deltas can have a
// different gain on each axis; calibrate only from acknowledged, unclamped
// movement. Each call sends at most one real wheel event and never changes app
// state through diagnostics. Existing callers retain their finite attempt limit.
export function createWheelScroller(page, viewportKey) {
  const gain = [1, 1];
  return async (deltaX, deltaY) => {
    if (![deltaX, deltaY].every(Number.isFinite)) throw new Error('Wheel correction must be finite.');
    const initial = await waitForScrollSettled(page, viewportKey), before = initial[viewportKey];
    const requested = [deltaX, deltaY].map((delta, axis) => {
      const offset = before[offsetKeys[axis]], maximum = Math.max(0, before[extentKeys[axis]] - before[sizeKeys[axis]]);
      // Ignore sub-DIP corrections and directions proven to be at a native
      // extent boundary; neither guarantees that ViewChanged will be raised.
      if (Math.abs(delta) < 1 || (delta < 0 ? offset <= .01 : offset >= maximum - .01)) return 0;
      return Math.sign(delta) * Math.min(Math.max(1, Math.abs(delta) / gain[axis]), before[sizeKeys[axis]]);
    });
    if (requested.every(delta => delta === 0)) return initial;
    await page.mouse.wheel(requested[0], requested[1]);
    const final = await waitForScrollSettled(page, viewportKey, {
      version: before.ViewChangeVersion, offsets: offsetKeys.map(key => before[key]), deltas: requested
    });
    const after = final[viewportKey];
    for (let axis = 0; axis < 2; axis++) {
      const delta = requested[axis], movement = after[offsetKeys[axis]] - before[offsetKeys[axis]];
      const maximum = Math.max(0, after[extentKeys[axis]] - after[sizeKeys[axis]]), position = after[offsetKeys[axis]];
      // A native per-wheel viewport cap or content boundary does not measure
      // gain. Exclude both, and bound quantization-sensitive estimates.
      if (delta !== 0 && movement * delta > 0 && position > .01 && position < maximum - .01
        && Math.abs(movement) < before[sizeKeys[axis]] - .5) {
        gain[axis] = Math.max(.25, Math.min(4, Math.abs(movement / delta)));
      }
    }
    return final;
  };
}
