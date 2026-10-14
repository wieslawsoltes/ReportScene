import { test, expect } from '@playwright/test';
import { screenshotPixels } from './screenshot-pixels.mjs';
import { ready, state, fresh, bounds, click, choose, save, artifact, screenshot } from './uno-driver.mjs';

test('prepared checkbox stroke stays painted when its nominal rectangle is outside the native viewport', async ({ page }, info) => {
  await ready(page); await click(page, 'tab-report');
  const report = { name: 'Prepared stroke visibility', pages: [{ name: 'Large', width: 1200, height: 1200,
    margins: { left: 0, top: 0, right: 0, bottom: 0 }, bands: [{ name: 'Title', kind: 'ReportTitle', height: 1100,
      items: [{ $type: 'checkbox', id: 'check', name: 'Checkbox', expression: 'false',
        bounds: { x: 300, y: 400, width: 280, height: 280 }, style: { foreground: '#108840', borderWidth: 0 } }] }] }] };
  const open = await bounds(page, 'open'), chooser = page.waitForEvent('filechooser');
  await page.mouse.click(open.X + open.Width / 2, open.Y + open.Height / 2);
  await (await chooser).setFiles({ name: 'stroke.report.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(report)) });
  await expect.poll(async () => (await state(page)).Document).toBe(report.name);
  const original = await save(page), initial = await state(page);
  await click(page, 'preview-report'); await expect.poll(async () => (await state(page)).PreparedPages).toBe(1);
  await choose(page, 'preview-zoom', 9); await expect.poll(async () => (await state(page)).Zoom).toBe(4);
  const indexBuilds = (await state(page)).PreviewIndexBuilds;
  // Real wheel input places the checkbox's nominal right edge 16 DIPs left of
  // the viewport, while its renderer-owned outline still extends into it.
  for (let step = 0; step < 12; step++) {
    const s = await state(page), p = s.Commands['preview-paper'], v = s.Commands['preview-viewport'], scale = s.Zoom * 96 / 72;
    const dx = p.X + 580 * scale - (v.X - 16), dy = p.Y + 540 * scale - (v.Y + v.Height / 2);
    if (Math.abs(dx) < 2 && Math.abs(dy) < 2) break;
    await page.mouse.move(v.X + v.Width / 2, v.Y + v.Height / 2);
    await page.mouse.wheel(dx, dy); await fresh(page);
  }
  await expect.poll(async () => {
    const s = await state(page), p = s.Commands['preview-paper'], v = s.Commands['preview-viewport'], scale = s.Zoom * 96 / 72;
    return Math.max(Math.abs(p.X + 580 * scale - (v.X - 16)), Math.abs(p.Y + 540 * scale - (v.Y + v.Height / 2)));
  }, { message: 'Native viewport has moved beyond the nominal checkbox rectangle' }).toBeLessThan(2);
  await expect.poll(async () => {
    const s = await state(page), v = s.Commands['preview-viewport'];
    const bytes = await screenshot(page, info.outputPath('prepared-offscreen-checkbox-stroke.png'));
    const actual = screenshotPixels(bytes).pixel(v.X + 12, v.Y + v.Height / 2);
    return [16, 136, 64].every((value, i) => Math.abs(value - actual[i]) <= 8);
  }, { message: 'A real green checkbox stroke is visible; a blank white frame cannot pass' }).toBe(true);
  expect((await state(page)).PreviewIndexBuilds).toBe(indexBuilds);
  expect((await state(page)).ContentRevision).toBe(initial.ContentRevision);
  expect((await state(page)).HistoryCount).toBe(initial.HistoryCount);
  expect((await state(page)).Dirty).toBe(false); expect((await state(page)).Diagnostics).toEqual([]);
  expect(await save(page)).toEqual(original);
  await artifact(info, 'prepared-stroke-native-state.json', await state(page));
});
