import { expect } from '@playwright/test';
import fs from 'node:fs/promises';

export const state = page => page.evaluate(() => window.reportScene?.state);
export async function ready(page, selftest = false) {
  await page.goto(`?${selftest ? 'selftest=1' : 'diagnostics=1'}`);
  await page.waitForFunction(() => window.reportScene?.ready || window.reportSceneFatal || window.reportScene?.testFatal, null, { timeout: 180_000 });
  expect(await page.evaluate(() => window.reportSceneFatal || window.reportScene?.testFatal), 'Uno startup must not fail').toBeFalsy();
  await expect.poll(() => page.evaluate(() => window.reportScene?.state?.RenderCount ?? 0)).toBeGreaterThan(0);
  await page.waitForFunction(() => window.reportScene?.presented || window.reportScene?.presentationError || window.reportSceneFatal, null, { timeout: 60_000 });
  expect(await page.evaluate(() => window.reportSceneFatal || window.reportScene?.presentationError)).toBeFalsy();
  await expect(page.locator('.uno-loader, #uno-loading')).toHaveCount(0);
  // Internal rendering is not sufficient readiness. All suites use the same real
  // presentation/font/screenshot handoff and a progressing diagnostic publication.
  await screenshot(page); await fresh(page);
}
export async function fresh(page) {
  const sequence = () => page.evaluate(() => window.reportScene?.state?.SnapshotSequence ?? 0);
  const before = await sequence();
  try {
    await expect.poll(sequence).toBeGreaterThan(before + 1);
  } catch (error) {
    const diagnostic = await page.evaluate(() => ({ fatal: window.reportSceneFatal, diagnosticError: window.reportScene?.diagnosticError, presented: window.reportScene?.presented, sequence: window.reportScene?.state?.SnapshotSequence }));
    throw new Error(`Native diagnostic publication stopped: ${JSON.stringify(diagnostic)}`, { cause: error });
  }
}
export async function bounds(page, id) {
  // Transfer only the requested geometry rather than serializing every report
  // object for each readiness poll. This remains a read-only native snapshot.
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
  const optionId = `${id}-option-${index}`;
  await fresh(page);
  // Scroll the actual native popup until the requested row is inside its viewport.
  // Diagnostics expose clipped hit rectangles, never an invisible off-paper row.
  for (let step = 0; step < 40 && !(await state(page))?.Commands[optionId]; step++) {
    const snapshot = await state(page), popup = snapshot?.Commands[`${id}-popup`];
    if (!popup) { await fresh(page); continue; }
    const visible = Object.keys(snapshot.Commands).filter(k => k.startsWith(`${id}-option-`))
      .map(k => Number(k.slice(`${id}-option-`.length))).filter(Number.isFinite);
    const direction = visible.length && index < Math.min(...visible) ? -1 : 1;
    await page.mouse.move(popup.X + popup.Width / 2, popup.Y + popup.Height / 2);
    await page.mouse.wheel(0, direction * Math.max(22, Math.min(120, popup.Height * .6)));
    await fresh(page);
  }
  // Require the same native rectangle across three distinct diagnostic snapshots.
  let previous = '', previousSequence = -1, stable = 0;
  await expect.poll(async () => {
    const snapshot = await state(page), rect = snapshot?.Commands[optionId];
    if (!rect || snapshot.SnapshotSequence === previousSequence) return false;
    const key = JSON.stringify(rect); stable = key === previous ? stable + 1 : 1;
    previous = key; previousSequence = snapshot.SnapshotSequence;
    return stable >= 3;
  }, { message: `Stable native popup item ${optionId}` }).toBe(true);
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
  await expect(page.locator('.uno-loader, #uno-loading')).toHaveCount(0);
  const bytes = await page.screenshot({ ...(path ? { path } : {}), fullPage: true });
  // A detached decoder never changes application DOM, state or rendering.
  const colors = await page.evaluate(async base64 => {
    const image = new Image(); image.src = `data:image/png;base64,${base64}`; await image.decode();
    const canvas = document.createElement('canvas'); canvas.width = 160; canvas.height = 100;
    const context = canvas.getContext('2d'); context.drawImage(image, 0, 0, 160, 100);
    const pixels = context.getImageData(0, 0, 160, 100).data; const colors = new Set();
    for (let n = 0; n < pixels.length; n += 4) colors.add(((pixels[n] >> 3) << 10) | ((pixels[n + 1] >> 3) << 5) | (pixels[n + 2] >> 3));
    return colors.size;
  }, bytes.toString('base64'));
  expect(colors, 'The real rendered window must contain UI pixels, not only the splash or a blank canvas').toBeGreaterThan(30);
  return bytes;
}
