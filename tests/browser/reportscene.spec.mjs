import { test, expect } from '@playwright/test';
import fs from 'node:fs/promises';

const state = page => page.evaluate(() => window.reportScene?.state);
async function ready(page, selftest = false) {
  await page.goto(`?${selftest ? 'selftest=1' : 'diagnostics=1'}`);
  await page.waitForFunction(() => window.reportScene?.ready || window.reportSceneFatal || window.reportScene?.testFatal, { timeout: 180_000 });
  const fatal = await page.evaluate(() => window.reportSceneFatal || window.reportScene?.testFatal);
  expect(fatal, 'Uno startup must not fail').toBeFalsy();
  await expect.poll(async () => (await state(page))?.RenderCount ?? 0).toBeGreaterThan(0);
}
async function click(page, id) {
  await expect.poll(async () => Boolean((await state(page))?.Commands[id]), { message: `Visible Uno command ${id}` }).toBeTruthy();
  const r = (await state(page)).Commands[id];
  await page.mouse.click(r.X + r.Width / 2, r.Y + r.Height / 2);
}
async function property(page, id, text) {
  await expect.poll(async () => Boolean((await state(page))?.Properties[id])).toBeTruthy();
  const r = (await state(page)).Properties[id];
  await page.mouse.click(r.X + Math.min(30, r.Width / 2), r.Y + Math.min(12, r.Height / 2));
  await page.keyboard.press('Control+a');
  await page.keyboard.insertText(text);
  await page.keyboard.press('Tab');
}
async function artifact(info, name, content, contentType = 'application/json') {
  await fs.writeFile(info.outputPath(name), typeof content === 'string' || Buffer.isBuffer(content) ? content : JSON.stringify(content, null, 2));
  await info.attach(name, { path: info.outputPath(name), contentType });
}

test('actual .NET WebAssembly conformance suite and rendered Uno shell', async ({ page }, info) => {
  const consoleLog = []; const errors = [];
  page.on('console', m => consoleLog.push(`${m.type()}: ${m.text()}`));
  page.on('pageerror', e => errors.push(e.message));
  try {
    await ready(page, true);
    await page.waitForFunction(() => Array.isArray(window.reportScene?.tests) || window.reportScene?.testFatal, { timeout: 120_000 });
    const result = await page.evaluate(() => ({ tests: window.reportScene.tests, fatal: window.reportScene.testFatal, state: window.reportScene.state }));
    await artifact(info, 'wasm-conformance.json', result);
    expect(result.fatal).toBeFalsy();
    expect(result.tests.length).toBeGreaterThan(100);
    expect(result.tests.filter(t => !t.Passed), 'All native tests must also pass in real .NET WebAssembly').toEqual([]);
    expect(result.state.ObjectCount).toBeGreaterThan(20);
    expect(result.state.Diagnostics).toEqual([]);
    await page.screenshot({ path: info.outputPath('designer.png'), fullPage: true });
    expect(errors).toEqual([]);
  } finally {
    await artifact(info, 'browser-console.txt', consoleLog.join('\n'), 'text/plain');
    await artifact(info, 'browser-errors.json', errors);
    await artifact(info, 'startup-state.json', await page.evaluate(() => ({ state: window.reportScene?.state, fatal: window.reportSceneFatal, html: document.body.innerText })));
  }
});

test('real pointer and keyboard edits, transactional undo, save and PDF export', async ({ page }, info) => {
  await ready(page);
  const initial = await state(page);
  await click(page, 'insert-text');
  await expect.poll(async () => (await state(page)).ObjectCount).toBe(initial.ObjectCount + 1);
  await expect.poll(async () => (await state(page)).SelectedCount).toBe(1);
  await property(page, 'text', 'Browser validation text');
  await expect.poll(async () => (await state(page)).Objects.find(o => o.Selected)?.Text).toBe('Browser validation text');
  const selected = (await state(page)).Objects.find(o => o.Selected);
  const before = selected.Bounds;
  const screen = selected.Screen;
  expect(screen).toBeTruthy();
  const historyBefore = (await state(page)).HistoryCount;
  await page.mouse.move(screen.X + screen.Width / 2, screen.Y + screen.Height / 2);
  await page.mouse.down();
  await page.mouse.move(screen.X + screen.Width / 2 + 36, screen.Y + screen.Height / 2 + 18, { steps: 12 });
  await page.mouse.up();
  await expect.poll(async () => (await state(page)).HistoryCount).toBe(historyBefore + 1);
  await expect.poll(async () => (await state(page)).Objects.find(o => o.Id === selected.Id).Bounds.X).toBeGreaterThan(before.X);
  await click(page, 'quick-undo');
  await expect.poll(async () => (await state(page)).Objects.find(o => o.Id === selected.Id).Bounds.X).toBe(before.X);
  await click(page, 'quick-redo');
  await expect.poll(async () => (await state(page)).Objects.find(o => o.Id === selected.Id).Bounds.X).toBeGreaterThan(before.X);
  const downloadPromise = page.waitForEvent('download');
  await click(page, 'quick-save');
  const download = await downloadPromise;
  const saved = await fs.readFile(await download.path());
  const document = JSON.parse(saved.toString('utf8'));
  expect(document.pages.length).toBeGreaterThan(0);
  expect(saved.toString('utf8')).toContain('Browser validation text');
  await artifact(info, 'saved-report.json', saved);
  await expect.poll(async () => (await state(page)).Dirty).toBe(false);
  await click(page, 'preview');
  await expect.poll(async () => (await state(page)).Preview).toBe(true);
  await expect.poll(async () => (await state(page)).PreparedPages).toBeGreaterThan(0);
  expect((await state(page)).Diagnostics).toEqual([]);
  await page.screenshot({ path: info.outputPath('preview.png'), fullPage: true });
  const pdfPromise = page.waitForEvent('download');
  await click(page, 'export-pdf');
  const pdf = await pdfPromise;
  const bytes = await fs.readFile(await pdf.path());
  expect(bytes.subarray(0, 5).toString()).toBe('%PDF-');
  expect(bytes.toString('latin1').trimEnd().endsWith('%%EOF')).toBe(true);
  expect(bytes.length).toBeGreaterThan(2000);
  await artifact(info, 'report.pdf', bytes, 'application/pdf');
  await artifact(info, 'final-state.json', await state(page));
  await click(page, 'back-to-design');
  await expect.poll(async () => (await state(page)).Preview).toBe(false);
});

test('opening an actual local report, finding text and changing prepared pages', async ({ page }, info) => {
  await ready(page);
  const rows = Array.from({ length: 75 }, (_, n) => ({ Number: n + 1, Label: `Record ${n + 1}` }));
  const report = {
    formatVersion: 1, name: 'Browser imported report', culture: 'en-US',
    data: [{ name: 'Rows', columns: ['Number', 'Label'], rows }],
    pages: [{ name: 'Page1', width: 595.276, height: 841.89, margins: { left: 36, top: 36, right: 36, bottom: 36 }, bands: [
      { id: 'title', name: 'Title', kind: 'ReportTitle', height: 50, items: [{ $type: 'text', id: 'title-text', name: 'TitleText', text: 'IMPORTED REPORT', bounds: { x: 0, y: 0, width: 500, height: 40 }, style: { fontSize: 24, bold: true } }] },
      { id: 'data', name: 'Data', kind: 'Data', height: 28, canSplit: false, dataSource: 'Rows', items: [{ $type: 'text', id: 'label', name: 'Label', text: '[Rows.Label]', bounds: { x: 0, y: 3, width: 450, height: 22 } }] }
    ] }]
  };
  await click(page, 'tab-report');
  const chooserPromise = page.waitForEvent('filechooser');
  await click(page, 'open');
  const chooser = await chooserPromise;
  await chooser.setFiles({ name: 'browser-import.report.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(report)) });
  await expect.poll(async () => (await state(page)).Document).toBe(report.name);
  await click(page, 'preview-report');
  await expect.poll(async () => (await state(page)).PreparedPages).toBeGreaterThan(2);
  await click(page, 'next-page');
  await expect.poll(async () => (await state(page)).PreviewPage).toBe(2);
  const search = (await state(page)).Commands['preview-search'];
  await page.mouse.click(search.X + 30, search.Y + search.Height / 2);
  await page.keyboard.insertText('Record 70');
  await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).Status).toContain('Match 1 of 1');
  await expect.poll(async () => (await state(page)).PreviewPage).toBeGreaterThan(2);
  expect((await state(page)).Diagnostics).toEqual([]);
  await page.screenshot({ path: info.outputPath('multipage-search.png'), fullPage: true });
  await artifact(info, 'multipage-state.json', await state(page));
});
