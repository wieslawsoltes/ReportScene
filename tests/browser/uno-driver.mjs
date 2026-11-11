import { expect } from '@playwright/test';
import fs from 'node:fs/promises';
import { screenshotPixels } from './screenshot-pixels.mjs';

const gpuHealth = new WeakMap();
function watchGpuHealth(page) {
  if (gpuHealth.has(page)) return;
  const health = { errors: [], pending: new Set() };
  gpuHealth.set(page, health);
  page.on('console', message => {
    const text = message.text();
    if (!/^WebGpuInit: (?:device lost\b|uncaptured error:)/.test(text)) return;
    const source = message.args()[0];
    if (!source) {
      health.errors.push({ document: null, text: `GPU health message has no originating context: ${text}` });
      return;
    }
    // Resolve against the message's execution context, never the newly navigated page.
    // A reload may legitimately destroy the previous document's GPU device.
    const pending = source.evaluate(() => performance.timeOrigin)
      .then(document => health.errors.push({ document, text }))
      .catch(error => {
        if (!/Execution context was destroyed|Cannot find context with specified id/.test(String(error)))
          health.errors.push({ document: null, text: `Cannot inspect GPU health: ${error}` });
      })
      .finally(() => health.pending.delete(pending));
    health.pending.add(pending);
  });
}
async function assertGpuHealth(page) {
  const health = gpuHealth.get(page);
  if (!health) return; // The deliberate unsupported-GPU scenario does not call ready().
  const document = await page.evaluate(() => performance.timeOrigin);
  while (health.pending.size) await Promise.all([...health.pending]);
  expect(health.errors.filter(error => error.document === null || error.document === document)
    .map(error => error.text), 'The current document must have no native WebGPU loss or uncaptured errors').toEqual([]);
}

export const state = page => page.evaluate(() => window.reportScene?.state);
export async function ready(page, selftest = false) {
  watchGpuHealth(page);
  await page.goto(`?${selftest ? 'selftest=1' : 'diagnostics=1'}`);
  await page.waitForFunction(() => window.reportScene?.ready || window.reportScene?.renderingError || window.reportSceneFatal || window.reportScene?.testFatal, null, { timeout: 180_000 });
  expect(await page.evaluate(() => window.reportSceneFatal || window.reportScene?.testFatal), 'Uno startup must not fail').toBeFalsy();
  expect(await page.evaluate(() => window.reportScene?.renderingError), 'The native WebGPU report surface must initialize').toBeFalsy();
  expect(await page.evaluate(() => window.reportScene?.renderingBackend), 'Report rendering must use the WebGPU backend').toBe('WebGPU');
  await expect.poll(() => page.evaluate(() => window.reportScene?.state?.RenderCount ?? 0)).toBeGreaterThan(0);
  await page.waitForFunction(() => window.reportScene?.presented || window.reportScene?.presentationError || window.reportSceneFatal, null, { timeout: 60_000 });
  expect(await page.evaluate(() => window.reportSceneFatal || window.reportScene?.presentationError)).toBeFalsy();
  await expect(page.locator('.uno-loader, #uno-loading')).toHaveCount(0);
  await screenshot(page); await fresh(page);
}
export async function fresh(page) {
  await assertGpuHealth(page);
  const sequence = () => page.evaluate(() => window.reportScene?.state?.SnapshotSequence ?? 0);
  const before = await sequence();
  try {
    await expect.poll(sequence).toBeGreaterThan(before + 1);
  } catch (error) {
    const diagnostic = await page.evaluate(() => ({ fatal: window.reportSceneFatal, diagnosticError: window.reportScene?.diagnosticError, presented: window.reportScene?.presented, sequence: window.reportScene?.state?.SnapshotSequence }));
    throw new Error(`Native diagnostic publication did not advance before the deadline: ${JSON.stringify(diagnostic)}`, { cause: error });
  }
  await assertGpuHealth(page);
}
export async function bounds(page, id) {
  const read = () => page.evaluate(key => window.reportScene?.state?.Commands[key], id);
  await expect.poll(async () => Boolean(await read()), { message: `Visible Uno control ${id}` }).toBeTruthy();
  return await read();
}
export async function click(page, id) {
  const r = await bounds(page, id);
  await page.mouse.click(r.X + r.Width / 2, r.Y + r.Height / 2);
  await fresh(page);
}
export async function fill(page, id, text) {
  const r = await bounds(page, id);
  await page.mouse.click(r.X + Math.min(24, r.Width / 2), r.Y + r.Height - 15);
  await fresh(page); await page.keyboard.press('Control+a'); await page.keyboard.insertText(text);
  await expect.poll(() => page.evaluate(key => window.reportScene?.state?.Inputs[key], id), { message: `Native TextBox ${id} receives keyboard input` }).toBe(text);
  await page.keyboard.press('Tab'); await fresh(page);
}
export async function choose(page, id, index) {
  const r = await bounds(page, id);
  await page.mouse.click(r.X + r.Width - 14, r.Y + r.Height - 15);
  const optionId = `${id}-option-${index}`, prefix = `${id}-option-`;
  const scrollState = snapshot => snapshot?.PopupScrolls?.[id];
  let rowHeight = 0;
  const fullyVisible = snapshot => {
    const options = Object.entries(snapshot?.Commands ?? {}).filter(([key]) => key.startsWith(prefix));
    rowHeight = Math.max(rowHeight, ...options.map(([, rect]) => rect.Height));
    const rect = snapshot?.Commands[optionId];
    // Diagnostics clip rows to the native scroll viewport. A few visible pixels
    // at the edge are not a complete click target while the wheel is settling.
    return rect && rowHeight > 0 && rect.Height >= rowHeight - .5;
  };
  await fresh(page);
  for (let step = 0; step < 40; step++) {
    const snapshot = await state(page), popup = snapshot?.Commands[`${id}-popup`];
    if (fullyVisible(snapshot)) break;
    if (!popup) { await fresh(page); continue; }
    const visible = Object.keys(snapshot.Commands).filter(k => k.startsWith(prefix))
      .map(k => Number(k.slice(prefix.length))).filter(Number.isFinite);
    // Scroll a clipped first row upwards and a clipped last row downwards.
    const direction = visible.length && index <= Math.min(...visible) ? -1 : 1;
    await page.mouse.move(popup.X + popup.Width / 2, popup.Y + popup.Height / 2);
    await fresh(page);
    const before = scrollState(await state(page));
    expect(before, `Native popup scroll observations for ${id}`).toBeTruthy();
    const maximum = Math.max(0, before.ExtentHeight - before.ViewportHeight);
    const clamped = direction < 0 ? before.VerticalOffset <= .01 : before.VerticalOffset >= maximum - .01;
    if (clamped) { await fresh(page); continue; }
    await page.mouse.wheel(0, direction * Math.max(22, Math.min(120, popup.Height * .6)));
    // Several diagnostic snapshots may precede delivery of a queued wheel.
    // Require a subsequent native event before accepting the final idle state.
    await expect.poll(async () => {
      const current = scrollState(await state(page));
      return current && current.ViewChangeVersion > before.ViewChangeVersion && !current.IsIntermediate
        && (current.VerticalOffset - before.VerticalOffset) * direction > .01;
    }, { message: `Native popup ${id} acknowledges the wheel and settles` }).toBe(true);
    await fresh(page);
  }
  let previous = '', previousSequence = -1, stable = 0;
  await expect.poll(async () => {
    const snapshot = await state(page), rect = snapshot?.Commands[optionId], scroll = scrollState(snapshot);
    if (!fullyVisible(snapshot) || !scroll || scroll.IsIntermediate) { previous = ''; stable = 0; return false; }
    if (snapshot.SnapshotSequence === previousSequence) return false;
    const key = JSON.stringify([rect, scroll]); stable = key === previous ? stable + 1 : 1;
    previous = key; previousSequence = snapshot.SnapshotSequence;
    return stable >= 3;
  }, { message: `Stable fully visible native popup item ${optionId}` }).toBe(true);
  await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
  await click(page, optionId);
  await expect.poll(async () => Boolean((await state(page))?.Commands[`${id}-option-${index}`])).toBe(false);
}
export async function save(page) {
  await bounds(page, 'quick-save'); const waiting = page.waitForEvent('download');
  const r = (await state(page)).Commands['quick-save'];
  await page.mouse.click(r.X + r.Width / 2, r.Y + r.Height / 2);
  const download = await waiting; return JSON.parse(await fs.readFile(await download.path(), 'utf8'));
}
export async function artifact(info, name, value) {
  await fs.writeFile(info.outputPath(name), JSON.stringify(value, null, 2));
  await info.attach(name, { path: info.outputPath(name), contentType: 'application/json' });
}
export async function screenshot(page, path) {
  await assertGpuHealth(page);
  await expect(page.locator('.uno-loader, #uno-loading')).toHaveCount(0);
  const bytes = await page.screenshot({ ...(path ? { path } : {}), fullPage: true });
  const colors = screenshotPixels(bytes).colorCount();
  expect(colors, 'The real rendered window must contain UI pixels, not only the splash or a blank canvas').toBeGreaterThan(30);
  await assertGpuHealth(page);
  return bytes;
}
