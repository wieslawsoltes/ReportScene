import { test, expect } from '@playwright/test';
import fs from 'node:fs/promises';
import { state, ready, fresh, bounds, click, fill, choose, save, artifact } from './uno-driver.mjs';

function fixture() {
  const rows = [];
  for (let r = 0; r < 4; r++) for (let c = 0; c < 3; c++) rows.push({ Row: `R0${r}`, Column: `Q${c + 1}`, Value: 100 + 10 * r + c });
  return {
    name: 'Matrix browser contract', data: [{ name: 'Sales', columns: ['Row', 'Column', 'Value'], rows }],
    pages: [{ name: 'Page1', bands: [{ id: 'band', name: 'MatrixBand', kind: 'ReportTitle', height: 210, items: [{
      $type: 'matrix', id: 'matrix', name: 'SalesMatrix', dataSource: 'Sales', bounds: { x: 0, y: 0, width: 500, height: 180 },
      rowDimensions: [{ expression: 'Sales.Row', title: 'Region' }], columnDimensions: [{ expression: 'Sales.Column', title: 'Quarter' }],
      measures: [{ expression: 'Sales.Value', title: 'Revenue', format: 'N0' }], rowHeaderWidth: 130, cellWidth: 90
    }] }] }]
  };
}
async function openFixture(page) {
  await click(page, 'tab-report');
  const r = await bounds(page, 'open');
  const waiting = page.waitForEvent('filechooser');
  await page.mouse.click(r.X + r.Width / 2, r.Y + r.Height / 2);
  await (await waiting).setFiles({ name: 'matrix.report.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(fixture())) });
  await expect.poll(async () => (await state(page)).Document).toBe('Matrix browser contract');
  await click(page, 'tab-home');
  await expect.poll(async () => (await state(page)).Objects.find(o => o.Id === 'matrix')?.Screen).toBeTruthy();
  const item = (await state(page)).Objects.find(o => o.Id === 'matrix').Screen;
  await page.mouse.click(item.X + 35, item.Y + 30);
  await expect.poll(async () => (await state(page)).SelectedCount).toBe(1); await fresh(page);
}

test('matrix editor uses real inputs, validates, applies one transaction and cancels cleanly', async ({ page }, info) => {
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  try {
    await ready(page); await openFixture(page);
    expect((await state(page)).Diagnostics).toEqual([]);
    const initialHistory = (await state(page)).HistoryCount;
    await click(page, 'matrix-edit'); await bounds(page, 'matrix-dialog-apply');
    await click(page, 'matrix-rows-add');
    await fill(page, 'matrix-rows-item-1-expression', 'Sales.Column');
    await fill(page, 'matrix-rows-item-1-title', 'Quarter');
    await click(page, 'matrix-rows-item-0-subtotal');
    await click(page, 'matrix-tab-measures');
    await fill(page, 'matrix-measures-item-0-title', 'Sales total');
    await fill(page, 'matrix-measures-item-0-format', 'N2');
    await choose(page, 'matrix-measures-item-0-aggregate', 0);
    await click(page, 'matrix-tab-options');
    await fill(page, 'matrix-row-height', '0');
    await click(page, 'matrix-dialog-apply'); await bounds(page, 'matrix-dialog-apply');
    expect((await state(page)).HistoryCount).toBe(initialHistory);
    await fill(page, 'matrix-row-height', '28');
    await page.screenshot({ path: info.outputPath('matrix-editor.png'), fullPage: true });
    await click(page, 'matrix-dialog-apply');
    await expect.poll(async () => Boolean((await state(page)).Commands['matrix-dialog-apply'])).toBe(false);
    await expect.poll(async () => (await state(page)).HistoryCount).toBe(initialHistory + 1);
    const edited = await save(page); const matrix = edited.pages[0].bands[0].items[0];
    expect(matrix.rowDimensions.map(d => d.expression)).toEqual(['Sales.Row', 'Sales.Column']);
    expect(matrix.rowDimensions[0].subtotal).toBe(true);
    expect(matrix.measures[0]).toMatchObject({ title: 'Sales total', format: 'N2', aggregate: 'Sum' });
    expect(matrix.rowHeight).toBe(28);
    await click(page, 'quick-undo');
    const undone = await save(page); expect(undone.pages[0].bands[0].items[0].rowDimensions.length).toBe(1);
    expect(undone.pages[0].bands[0].items[0].rowHeight).toBe(24);
    await click(page, 'quick-redo'); expect((await save(page)).pages[0].bands[0].items[0].rowHeight).toBe(28);
    await click(page, 'matrix-edit'); await click(page, 'matrix-tab-options');
    await fill(page, 'matrix-row-height', '99'); await click(page, 'matrix-dialog-cancel');
    await expect.poll(async () => Boolean((await state(page)).Commands['matrix-dialog-apply'])).toBe(false);
    const cancelled = await save(page); expect(cancelled.pages[0].bands[0].items[0].rowHeight).toBe(28);
    expect((await state(page)).HistoryCount).toBe(initialHistory + 1);
    await click(page, 'preview'); await expect.poll(async () => (await state(page)).Preview).toBe(true);
    expect((await state(page)).Diagnostics).toEqual([]);
    const pdfWaiting = page.waitForEvent('download'); await click(page, 'export-pdf');
    const pdf = await pdfWaiting; const bytes = await fs.readFile(await pdf.path());
    expect(bytes.subarray(0, 5).toString()).toBe('%PDF-');
    expect(bytes.toString('latin1').trimEnd().endsWith('%%EOF')).toBe(true);
    await fs.writeFile(info.outputPath('matrix.pdf'), bytes);
    await page.screenshot({ path: info.outputPath('matrix-preview.png'), fullPage: true });
    await artifact(info, 'matrix-saved.json', edited); expect(errors).toEqual([]);
  } finally {
    await artifact(info, 'matrix-final-state.json', await state(page));
    await artifact(info, 'matrix-errors.json', errors);
  }
});

test('matrix insertion qualifies reserved fields and supports undo and redo', async ({ page }, info) => {
  await ready(page); await openFixture(page);
  const initial = (await state(page)).ObjectCount;
  await click(page, 'insert-matrix'); await expect.poll(async () => (await state(page)).ObjectCount).toBe(initial + 1);
  const document = await save(page); const added = document.pages[0].bands[0].items.find(i => i.id !== 'matrix');
  expect(added.$type).toBe('matrix'); expect(added.rowDimensions[0].expression).toBe('[Sales.Row]');
  expect(added.columnDimensions[0].expression).toBe('[Sales.Column]'); expect(added.measures[0].expression).toBe('[Sales.Value]');
  expect((await state(page)).Diagnostics).toEqual([]);
  await click(page, 'quick-undo'); await expect.poll(async () => (await state(page)).ObjectCount).toBe(initial);
  await click(page, 'quick-redo'); await expect.poll(async () => (await state(page)).ObjectCount).toBe(initial + 1);
  await artifact(info, 'matrix-insertion.json', document);
});

test('matrix sample renders through the real Uno preview and exports all tiles', async ({ page }, info) => {
  await ready(page); await click(page, 'tab-view'); await choose(page, 'samples', 5);
  await expect.poll(async () => (await state(page)).Document).toBe('Matrix sales');
  expect((await state(page)).Objects.some(o => o.Kind === 'MatrixItem')).toBe(true);
  await page.screenshot({ path: info.outputPath('matrix-sample-designer.png'), fullPage: true });
  await click(page, 'tab-home'); await click(page, 'preview');
  await expect.poll(async () => (await state(page)).Preview).toBe(true);
  await expect.poll(async () => (await state(page)).PreparedPages).toBeGreaterThan(1);
  expect((await state(page)).Diagnostics).toEqual([]);
  await page.screenshot({ path: info.outputPath('matrix-sample-page1.png'), fullPage: true });
  await click(page, 'next-page'); await expect.poll(async () => (await state(page)).PreviewPage).toBe(2);
  await page.screenshot({ path: info.outputPath('matrix-sample-page2.png'), fullPage: true });
  const waiting = page.waitForEvent('download'); await click(page, 'export-pdf');
  const pdf = await waiting; const bytes = await fs.readFile(await pdf.path()); expect(bytes.length).toBeGreaterThan(3000);
  await fs.writeFile(info.outputPath('matrix-sample.pdf'), bytes);
  await artifact(info, 'matrix-sample-state.json', await state(page));
});
