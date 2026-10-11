import { test, expect } from '@playwright/test';
import fs from 'node:fs/promises';
import { state, ready, fresh, bounds, click, fill, choose, save, artifact, screenshot } from './uno-driver.mjs';

function fixture() {
  return {
    name: 'Subreport browser contract', parameters: [{ name: 'Factor', defaultValue: 1 }],
    data: [{ name: 'Detail', columns: ['Amount'], rows: [{ Amount: 10 }, { Amount: 20 }, { Amount: 30 }] }],
    pages: [
      { name: 'Main', bands: [{ id: 'main-band', name: 'MainBand', kind: 'ReportTitle', height: 52, items: [
        { $type: 'text', id: 'prefix', name: 'Prefix', text: 'SUBREPORT CONTRACT', bounds: { x: 0, y: 0, width: 400, height: 24 } },
        { $type: 'subreport', id: 'sub', name: 'DetailReference', pageName: 'DetailPage', bounds: { x: 0, y: 28, width: 320, height: 24 } }
      ] }] },
      { name: 'DetailPage', bands: [{ id: 'detail-band', name: 'DetailBand', kind: 'Data', dataSource: 'Detail', height: 26, canSplit: false, items: [
        { $type: 'text', id: 'detail-text', name: 'DetailValue', text: '[Detail.Amount * Parameters.Factor]', bounds: { x: 0, y: 0, width: 300, height: 24 } }
      ] }] }
    ]
  };
}
async function upload(page, document) {
  await click(page, 'tab-report'); const r = await bounds(page, 'open');
  const waiting = page.waitForEvent('filechooser'); await page.mouse.click(r.X + r.Width / 2, r.Y + r.Height / 2);
  await (await waiting).setFiles({ name: 'subreport.report.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(document)) });
}
async function openFixture(page) {
  const document = fixture(); await upload(page, document);
  await expect.poll(async () => (await state(page)).Document).toBe(document.name);
  await click(page, 'tab-home'); await fresh(page);
  const r = (await state(page)).Objects.find(o => o.Id === 'sub').Screen;
  await page.mouse.click(r.X + r.Width / 2, r.Y + r.Height / 2);
  await expect.poll(async () => (await state(page)).Objects.find(o => o.Id === 'sub')?.Selected).toBe(true);
  return document;
}

test('subreport and target page insert together, undo together and support real page navigation', async ({ page }, info) => {
  await ready(page); await openFixture(page); const before = await state(page);
  await click(page, 'insert-subreport');
  await expect.poll(async () => (await state(page)).TemplatePages).toBe(before.TemplatePages + 1);
  await expect.poll(async () => (await state(page)).ObjectCount).toBe(before.ObjectCount + 2);
  expect((await state(page)).HistoryCount).toBe(before.HistoryCount + 1);
  const inserted = await save(page); const reference = inserted.pages[0].bands[0].items.find(i => i.$type === 'subreport' && i.id !== 'sub');
  expect(reference.pageName).toBe('Subreport1'); expect(inserted.pages[2].name).toBe(reference.pageName);
  await click(page, 'quick-undo'); await expect.poll(async () => (await state(page)).TemplatePages).toBe(before.TemplatePages);
  expect((await save(page)).pages[0].bands[0].items.length).toBe(2);
  await click(page, 'quick-redo'); await expect.poll(async () => (await state(page)).TemplatePages).toBe(3);
  await click(page, 'subreport-open');
  await expect.poll(async () => (await state(page)).Objects.some(o => o.Text === 'Subreport content')).toBe(true);
  expect((await state(page)).HistoryCount).toBe(before.HistoryCount + 1);
  await screenshot(page, info.outputPath('subreport-target-designer.png'));
  await click(page, 'page-tab-0'); await expect.poll(async () => (await state(page)).Objects.some(o => o.Id === 'sub')).toBe(true);
  expect((await state(page)).Diagnostics).toEqual([]); await artifact(info, 'subreport-inserted.report.json', inserted);
});

test('subreport editor validates bindings, applies atomically and preserves cancellation and undo', async ({ page }, info) => {
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await ready(page); await openFixture(page); const before = (await state(page)).HistoryCount;
  try {
    await click(page, 'subreport-edit'); await bounds(page, 'subreport-dialog-apply');
    await click(page, 'subreport-parent'); await click(page, 'subreport-add-binding');
    await fill(page, 'subreport-binding-0-name', 'Factor'); await fill(page, 'subreport-binding-0-expression', '3');
    await click(page, 'subreport-add-binding'); await fill(page, 'subreport-binding-1-name', 'Factor');
    await click(page, 'subreport-dialog-apply');
    await expect.poll(async () => (await state(page)).Inputs['subreport-dialog-error'] ?? '').toContain('unique');
    expect((await state(page)).HistoryCount).toBe(before);
    await click(page, 'subreport-binding-1-remove');
    await screenshot(page, info.outputPath('subreport-editor.png'));
    await click(page, 'subreport-dialog-apply');
    await expect.poll(async () => Boolean((await state(page)).Commands['subreport-dialog-apply'])).toBe(false);
    await expect.poll(async () => (await state(page)).HistoryCount).toBe(before + 1);
    const changed = await save(page); const sub = changed.pages[0].bands[0].items.find(i => i.id === 'sub');
    expect(sub.printOnParent).toBe(true); expect(sub.parameterBindings).toEqual([{ name: 'Factor', expression: '3' }]);
    await click(page, 'quick-undo'); expect((await save(page)).pages[0].bands[0].items.find(i => i.id === 'sub').parameterBindings).toEqual([]);
    await click(page, 'quick-redo'); await click(page, 'subreport-edit');
    await fill(page, 'subreport-binding-0-expression', '7'); await click(page, 'subreport-dialog-cancel');
    await expect.poll(async () => Boolean((await state(page)).Commands['subreport-dialog-apply'])).toBe(false);
    expect((await save(page)).pages[0].bands[0].items.find(i => i.id === 'sub').parameterBindings[0].expression).toBe('3');
    expect((await state(page)).HistoryCount).toBe(before + 1);
    await click(page, 'preview'); await expect.poll(async () => (await state(page)).Preview).toBe(true);
    const search = await bounds(page, 'preview-search'); await page.mouse.click(search.X + 25, search.Y + search.Height / 2);
    await fresh(page); await page.keyboard.insertText('90'); await page.keyboard.press('Enter');
    await expect.poll(async () => (await state(page)).Status).toContain('Match 1 of 1');
    await screenshot(page, info.outputPath('subreport-bound-preview.png'));
    const waiting = page.waitForEvent('download'); await click(page, 'export-pdf'); const download = await waiting;
    const bytes = await fs.readFile(await download.path()); expect(bytes.subarray(0, 5).toString()).toBe('%PDF-');
    expect(bytes.toString('latin1').trimEnd().endsWith('%%EOF')).toBe(true); await fs.writeFile(info.outputPath('subreport-bound.pdf'), bytes);
    expect((await state(page)).Diagnostics).toEqual([]); expect(errors).toEqual([]);
    await artifact(info, 'subreport-edited.report.json', changed);
  } finally { await artifact(info, 'subreport-editor-state.json', await state(page)); }
});

test('parallel subreport sample presents different row heights across physical pages and exports', async ({ page }, info) => {
  await ready(page); await click(page, 'tab-view'); await choose(page, 'samples', 6);
  await expect.poll(async () => (await state(page)).Document).toBe('Subreport dashboard');
  expect((await state(page)).TemplatePages).toBe(3); await screenshot(page, info.outputPath('parallel-subreport-designer.png'));
  await click(page, 'tab-home'); await click(page, 'preview');
  await expect.poll(async () => (await state(page)).Preview).toBe(true);
  await expect.poll(async () => (await state(page)).PreparedPages).toBeGreaterThan(1);
  expect((await state(page)).Diagnostics).toEqual([]);
  await screenshot(page, info.outputPath('parallel-subreport-page1.png'));
  await click(page, 'next-page'); await expect.poll(async () => (await state(page)).PreviewPage).toBe(2);
  await screenshot(page, info.outputPath('parallel-subreport-page2.png'));
  const waiting = page.waitForEvent('download'); await click(page, 'export-pdf'); const download = await waiting;
  const bytes = await fs.readFile(await download.path()); expect(bytes.length).toBeGreaterThan(3000);
  expect(bytes.subarray(0, 5).toString()).toBe('%PDF-'); await fs.writeFile(info.outputPath('parallel-subreports.pdf'), bytes);
  await artifact(info, 'parallel-subreport-state.json', await state(page));
});

test('cyclic imported subreport references are rejected without replacing the report', async ({ page }, info) => {
  await ready(page); await openFixture(page); const before = await save(page); const history = (await state(page)).HistoryCount;
  const invalid = fixture(); invalid.name = 'Invalid cyclic report';
  invalid.pages[1].bands[0].items.push({ $type: 'subreport', pageName: 'Main', bounds: { x: 0, y: 28, width: 300, height: 24 } });
  await upload(page, invalid);
  await expect.poll(async () => (await state(page)).Diagnostics.map(d => d.Message).join(' ')).toContain('cycle');
  expect((await state(page)).Document).toBe(before.name); expect((await state(page)).HistoryCount).toBe(history);
  expect(await save(page)).toEqual(before); await artifact(info, 'subreport-rejected-state.json', await state(page));
});
