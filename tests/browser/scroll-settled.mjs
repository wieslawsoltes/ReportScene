import { expect } from '@playwright/test';
import { state } from './uno-driver.mjs';

// ScrollViewer wheel motion is asynchronous. A new diagnostic snapshot can
// describe the middle of an animation; issuing a correction from it overshoots.
// Observe the native final ViewChanged event and stable offsets in separately
// published snapshots before another input or a screen-coordinate assertion.
export async function waitForScrollSettled(page, viewportKey) {
  let previousSequence = -1, previousPosition = null, stable = 0;
  await expect.poll(async () => {
    const snapshot = await state(page), viewport = snapshot?.[viewportKey];
    if (!viewport || typeof viewport.IsIntermediate !== 'boolean') {
      throw new Error('Missing native scroll evidence: ' + viewportKey);
    }
    if (snapshot.SnapshotSequence === previousSequence) return false;
    previousSequence = snapshot.SnapshotSequence;
    if (viewport.IsIntermediate) {
      stable = 0; previousPosition = null; return false;
    }
    const position = [viewport.HorizontalOffset, viewport.VerticalOffset];
    stable = previousPosition && position.every((value, axis) => Math.abs(value - previousPosition[axis]) < .01)
      ? stable + 1 : 0;
    previousPosition = position;
    return stable >= 2;
  }, { message: viewportKey + ' native scrolling has finished', intervals: [100, 200, 400] }).toBe(true);
}
