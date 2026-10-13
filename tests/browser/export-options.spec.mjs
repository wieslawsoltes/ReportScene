import { test, expect } from '@playwright/test';
import fs from 'node:fs/promises';
import { ready, state, fresh, bounds, click, choose, fill, save, artifact, screenshot } from './uno-driver.mjs';

async function load(page, large = false) {
  await ready(page); await click(page, 'tab-report');
  const report = { name: 'Export options contract', data: [{ name: 'Rows', columns: ['Name'], rows: [{ Name: 'Alpha' }, { Name: 'Beta' }] }],
    pages: Array.from({ length: large ? 1 : 4 }, (_, n) => ({ name: `Template${n + 1}`, width: large ? 1000 : 216, height: large ? 1000 : 288,
      bands: [{ id: `band-${n}`, name: `Title${n + 1}`, kind: 'ReportTitle', height: 45, items: [
        { $type: 'text', id: `text-${n}`, name: `Text${n + 1}`, text: `Exported page ${n + 1}`, bounds: { x: 0, y: 0, width: 140, height: 25 } }
      ] }] })) };
  const r = await bounds(page, 'open'), waiting = page.waitForEvent('filechooser');
  await page.mouse.click(r.X + r.Width / 2, r.Y + r.Height / 2);
  await (await waiting).setFiles({ name: 'export.report.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(report)) });
  await expect.poll(async () => (await state(page)).Document).toBe(report.name);
  await click(page, 'preview-report'); await expect.poll(async () => (await state(page)).PreparedPages).toBe(report.pages.length);
  return await state(page);
}
async function openOptions(page) {
  await click(page, 'export-options'); await bounds(page, 'output-dialog-export');
  await expect.poll(async () => (await state(page)).Inputs['output-pages-all']).toBe('True');
}
async function download(page) {
  const waiting = page.waitForEvent('download'); await click(page, 'output-dialog-export');
  const file = await waiting; return { name: file.suggestedFilename(), bytes: await fs.readFile(await file.path()) };
}
async function unchanged(page, before) {
  const after = await state(page);
  expect(after.ContentRevision).toBe(before.ContentRevision); expect(after.HistoryCount).toBe(before.HistoryCount);
  expect(after.Dirty).toBe(false); expect(after.PreparedPages).toBe(before.PreparedPages);
  expect(after.PreviewIndexBuilds).toBe(before.PreviewIndexBuilds); expect(after.Diagnostics).toEqual([]);
}

test('native export dialog validates range drafts and cancellation leaves report and output untouched', async ({ page }, info) => {
  const before = await load(page), original = await save(page); const downloads = [];
  page.on('download', file => downloads.push(file)); await openOptions(page);
  await choose(page, 'output-format', 5); await click(page, 'output-pages-custom'); await fill(page, 'output-page-numbers', '0,5');
  await click(page, 'output-dialog-export');
  await expect.poll(async () => (await state(page)).Inputs['output-error']).toContain('1 to 4');
  expect(downloads).toHaveLength(0); await bounds(page, 'output-dialog-cancel');
  await screenshot(page, info.outputPath('native-export-range-validation.png'));
  await click(page, 'output-dialog-cancel'); await expect.poll(async () => Boolean((await state(page)).Commands['output-dialog-export'])).toBe(false);
  expect(downloads).toHaveLength(0); await unchanged(page, before); expect(await save(page)).toEqual(original);
  await artifact(info, 'cancelled-export-state.json', await state(page));
});

test('native export custom and current page selections produce exact selected output and preserve the preview', async ({ page }, info) => {
  await load(page); await click(page, 'next-page'); const before = await state(page);
  await openOptions(page); await choose(page, 'output-format', 5); await click(page, 'output-pages-custom');
  await fill(page, 'output-page-numbers', '4,1-2,2');
  const selected = await download(page), json = JSON.parse(selected.bytes.toString('utf8'));
  expect(selected.name).toMatch(/\.prepared\.json$/); expect(json.pages.map(p => p.number)).toEqual([1, 2, 4]);
  expect(json.pages.flatMap(p => p.items.map(i => i.text)).filter(t => t.startsWith('Exported'))).toEqual(['Exported page 1', 'Exported page 2', 'Exported page 4']);
  await unchanged(page, before); expect((await state(page)).PreviewPage).toBe(2);
  await openOptions(page); await choose(page, 'output-format', 4); await click(page, 'output-pages-current');
  const current = await download(page); expect(current.bytes.toString('utf8')).toBe('Exported page 2'); await unchanged(page, before);
  await artifact(info, 'selected-export.prepared.json', json);
});

test('native PNG resolution controls generate requested pixel dimensions for the selected page', async ({ page }, info) => {
  const before = await load(page); await openOptions(page); await choose(page, 'output-format', 2);
  expect((await state(page)).Inputs['output-resolution-enabled']).toBe('True');
  await click(page, 'output-pages-current'); await choose(page, 'output-resolution', 0);
  await screenshot(page, info.outputPath('native-export-png-options.png'));
  const png = await download(page); expect(png.name).toMatch(/\.png$/);
  expect(png.bytes.subarray(0, 8)).toEqual(Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]));
  expect(png.bytes.readUInt32BE(16)).toBe(216); expect(png.bytes.readUInt32BE(20)).toBe(288);
  await unchanged(page, before); await artifact(info, 'png-selection.json', { name: png.name, width: 216, height: 288, byteCount: png.bytes.length });
});

test('data-only CSV disables page controls and switching formats restores the page-range draft', async ({ page }, info) => {
  const before = await load(page); await openOptions(page); await click(page, 'output-pages-custom'); await fill(page, 'output-page-numbers', '3');
  await choose(page, 'output-format', 6);
  await expect.poll(async () => (await state(page)).Inputs['output-pages-all']).toBe('True');
  expect((await state(page)).Inputs['output-pages-custom-enabled']).toBe('False'); expect((await state(page)).Inputs['output-page-numbers-enabled']).toBe('False');
  expect((await state(page)).Inputs['output-resolution-enabled']).toBe('False');
  expect((await state(page)).Inputs['output-page-summary']).toContain('all bound table rows');
  await choose(page, 'output-format', 5); await expect.poll(async () => (await state(page)).Inputs['output-pages-custom']).toBe('True');
  expect((await state(page)).Inputs['output-page-numbers']).toBe('3'); expect((await state(page)).Inputs['output-page-numbers-enabled']).toBe('True');
  await choose(page, 'output-format', 6); const csv = await download(page);
  expect(csv.name).toMatch(/\.csv$/); expect(csv.bytes.toString('utf8')).toContain('Alpha'); expect(csv.bytes.toString('utf8')).toContain('Beta');
  await unchanged(page, before); await artifact(info, 'csv-format-transition.json', await state(page));
});

test('oversized PNG export keeps the native draft open and recovers without altering the report', async ({ page }, info) => {
  const before = await load(page, true); const downloads = []; page.on('download', file => downloads.push(file));
  await openOptions(page); await choose(page, 'output-format', 2); await choose(page, 'output-resolution', 6);
  await click(page, 'output-dialog-export'); await expect.poll(async () => (await state(page)).Inputs['output-error']).toContain('pixel limit');
  expect(downloads).toHaveLength(0); await bounds(page, 'output-dialog-export'); await unchanged(page, before);
  await choose(page, 'output-resolution', 0); const png = await download(page);
  expect(png.bytes.readUInt32BE(16)).toBe(1000); expect(png.bytes.readUInt32BE(20)).toBe(1000); expect(downloads).toHaveLength(1);
  await unchanged(page, before); await artifact(info, 'export-size-limit-recovery.json', { bytes: png.bytes.length, after: await state(page) });
});
