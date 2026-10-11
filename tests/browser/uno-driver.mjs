import { expect } from '@playwright/test';
import fs from 'node:fs/promises';

export const state = page => page.evaluate(() => window.reportScene?.state);
export async function ready(page) {
  await page.goto('?diagnostics=1');
  await page.waitForFunction(() => window.reportScene?.ready || window.reportSceneFatal, { timeout: 180_000 });
  expect(await page.evaluate(() => window.reportSceneFatal)).toBeFalsy();
  await expect.poll(async () => (await state(page))?.RenderCount ?? 0).toBeGreaterThan(0);
  await fresh(page);
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
  // Selection can replace the ribbon itself (e.g. opening a sample). The caller verifies the
  // resulting document/serialized value instead of polling a newly-created placeholder control.
  await expect.poll(async () => Boolean((await state(page))?.Commands[`${id}-option-${index}`])).toBe(false);
}
export async function save(page) {
  await bounds(page, 'quick-save');
  const waiting = page.waitForEvent('download');
  const r = (await state(page)).Commands['quick-save'];
  await page.mouse.click(r.X + r.Width / 2, r.Y + r.Height / 2);
  const download = await waiting;
  return JSON.parse(await fs.readFile(await download.path(), 'utf8'));
}
export async function artifact(info, name, value) {
  await fs.writeFile(info.outputPath(name), JSON.stringify(value, null, 2));
  await info.attach(name, { path: info.outputPath(name), contentType: 'application/json' });
}
