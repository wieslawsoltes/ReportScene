import { expect } from '@playwright/test';
import fs from 'node:fs/promises';

export const state = page => page.evaluate(() => window.reportScene?.state);
export async function ready(page) {
  await page.goto('?diagnostics=1');
  await page.waitForFunction(() => window.reportScene?.ready || window.reportSceneFatal, null, { timeout: 180_000 });
  expect(await page.evaluate(() => window.reportSceneFatal)).toBeFalsy();
  await expect.poll(async () => (await state(page))?.RenderCount ?? 0).toBeGreaterThan(0);
  await page.waitForFunction(() => window.reportScene?.presented || window.reportScene?.presentationError, null, { timeout: 60_000 });
  expect(await page.evaluate(() => window.reportScene.presentationError)).toBeFalsy();
  await expect(page.locator('.uno-loader, #uno-loading')).toHaveCount(0);
  await fresh(page); await screenshot(page);
}
export async function fresh(page) {
  const before = (await state(page))?.SnapshotSequence ?? 0;
  await expect.poll(async () => (await state(page))?.SnapshotSequence ?? 0).toBeGreaterThan(before + 1);
}
export async function bounds(page, id) {
  await expect.poll(async () => Boolean((await state(page))?.Commands[id]), { message: `Visible Uno control ${id}` }).toBeTruthy();
  return (await state(page)).Commands[id];
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
  await expect.poll(async () => (await state(page))?.Inputs[id], { message: `Native TextBox ${id} receives keyboard input` }).toBe(text);
  await page.keyboard.press('Tab'); await fresh(page);
}
export async function choose(page, id, index) {
  const r = await bounds(page, id);
  await page.mouse.click(r.X + r.Width - 14, r.Y + r.Height - 15);
  await click(page, `${id}-option-${index}`);
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
  // Decode the actual screenshot using a detached image/canvas. This never changes application
  // DOM, state or rendering. A loaded-but-covered UI (formerly 5 colors) must not pass.
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
