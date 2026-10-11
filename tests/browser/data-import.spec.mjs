import { test, expect } from '@playwright/test';
import { state, ready, fresh, bounds, click, fill, save, artifact } from './uno-driver.mjs';

async function upload(page, id, name, text) {
  const r = await bounds(page, id); const waiting = page.waitForEvent('filechooser');
  await page.mouse.click(r.X + r.Width / 2, r.Y + r.Height / 2);
  await (await waiting).setFiles({ name, mimeType: name.endsWith('.xml') ? 'application/xml' : 'application/json', buffer: Buffer.from(text) });
}
const template = {
  name: 'XML source report', data: [{ name: 'Sales', columns: ['Name', 'Amount'], rows: [] }],
  pages: [{ name: 'Page1', bands: [
    { id: 'rows', name: 'Rows', kind: 'Data', dataSource: 'Sales', height: 26, items: [{ $type: 'text', id: 'name', name: 'Name', text: '[Sales.Name]', bounds: { x: 0, y: 0, width: 300, height: 24 } }] },
    { id: 'total', name: 'Total', kind: 'ReportSummary', height: 30, items: [{ $type: 'text', id: 'sum', name: 'Sum', text: '[Sum(Sales.Amount)]', bounds: { x: 0, y: 0, width: 300, height: 26 } }] }
  ] }]
};

test('typed XML import previews, binds, saves and reverses as one document edit', async ({ page }, info) => {
  await ready(page); await click(page, 'tab-report'); await upload(page, 'open', 'template.report.json', JSON.stringify(template));
  await expect.poll(async () => (await state(page)).Document).toBe(template.name); await fresh(page);
  const before = (await state(page)).HistoryCount;
  const xml = `<data xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xs="http://www.w3.org/2001/XMLSchema"><row><Name>Łódź</Name><Amount xsi:type="xs:decimal">12.50</Amount></row><row><Name>Gdańsk</Name><Amount xsi:type="xs:decimal">7.25</Amount></row></data>`;
  await upload(page, 'import-data', 'Sales.xml', xml); await bounds(page, 'data-import-apply');
  await click(page, 'data-import-preview');
  await page.screenshot({ path: info.outputPath('xml-import-preview.png'), fullPage: true });
  await click(page, 'data-import-apply');
  await expect.poll(async () => (await state(page)).HistoryCount).toBe(before + 1);
  await expect.poll(async () => Boolean((await state(page)).Commands['data-import-apply'])).toBe(false);
  const imported = await save(page);
  expect(imported.data[0].rows).toEqual([{ Name: 'Łódź', Amount: 12.5 }, { Name: 'Gdańsk', Amount: 7.25 }]);
  await click(page, 'quick-undo'); expect((await save(page)).data[0].rows).toEqual([]);
  await click(page, 'quick-redo'); expect((await save(page)).data[0].rows.length).toBe(2);
  await click(page, 'preview-report'); await expect.poll(async () => (await state(page)).Preview).toBe(true);
  expect((await state(page)).Diagnostics).toEqual([]);
  const r = await bounds(page, 'preview-search'); await page.mouse.click(r.X + 30, r.Y + r.Height / 2);
  await fresh(page); await page.keyboard.insertText('19.75'); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).Status).toContain('Match 1 of 1');
  await page.screenshot({ path: info.outputPath('xml-report-preview.png'), fullPage: true });
  await artifact(info, 'xml-imported.report.json', imported);
});

test('invalid XML and cancelled imports leave the document and history unchanged', async ({ page }, info) => {
  await ready(page); const initial = await save(page); await click(page, 'tab-report');
  const history = (await state(page)).HistoryCount;
  await upload(page, 'import-data', 'Bad.xml', '<!DOCTYPE d [<!ENTITY x SYSTEM "file:///not-read">]><d><row><A>&x;</A></row></d>');
  await bounds(page, 'data-import-apply'); await click(page, 'data-import-apply');
  await expect.poll(async () => (await state(page)).Inputs['data-import-error'] ?? '').toContain('DTD');
  expect((await state(page)).HistoryCount).toBe(history);
  await click(page, 'data-import-cancel'); await expect.poll(async () => Boolean((await state(page)).Commands['data-import-apply'])).toBe(false);
  expect(await save(page)).toEqual(initial);
  await upload(page, 'import-data', 'Cancelled.xml', '<d><row><Name>not applied</Name></row></d>');
  await bounds(page, 'data-import-apply'); await fill(page, 'data-import-name', 'Cancelled');
  await click(page, 'data-import-cancel'); await expect.poll(async () => Boolean((await state(page)).Commands['data-import-apply'])).toBe(false);
  expect(await save(page)).toEqual(initial); expect((await state(page)).HistoryCount).toBe(history);
  await artifact(info, 'xml-rejected-state.json', await state(page));
});
