import { test, expect } from '@playwright/test';
import { ready, state, fresh, bounds, click, fill, save, artifact, screenshot } from './uno-driver.mjs';

async function initialize(context, page, blockFirst = false) {
  // The real host opens and renders the real print HTML. Only the final OS print
  // device invocation is recorded: CI has no physical printer or interactive spooler.
  await context.addInitScript(({ blockFirst }) => {
    window.__printInvocations = 0;
    window.print = () => { window.__printInvocations++; };
    if (blockFirst) {
      let blocked = false; const open = window.open.bind(window);
      window.open = (...args) => {
        if (!blocked && args[0] === '' && args[1] === '_blank') { blocked = true; return null; }
        return open(...args);
      };
    }
  }, { blockFirst });
  await ready(page); await click(page, 'tab-report');
  const colors = ['#B03030', '#108840', '#194BBA', '#D09020'];
  const report = { name: 'Print page options contract', pages: colors.map((fill, n) => ({ name: `Template${n + 1}`, width: 216, height: 288,
    bands: [{ id: `band-${n}`, name: `Title${n + 1}`, kind: 'ReportTitle', height: 50, items: [
      { $type: 'text', id: `text-${n}`, name: `Text${n + 1}`, text: `Printed position ${n + 1}`,
        bounds: { x: 0, y: 0, width: 130, height: 30 }, style: { fill, foreground: '#FFFFFF', fontSize: 10 } }
    ] }] })) };
  const r = await bounds(page, 'open'), waiting = page.waitForEvent('filechooser');
  await page.mouse.click(r.X + r.Width / 2, r.Y + r.Height / 2);
  await (await waiting).setFiles({ name: 'print.report.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(report)) });
  await expect.poll(async () => (await state(page)).Document).toBe(report.name);
  await click(page, 'preview-report'); await expect.poll(async () => (await state(page)).PreparedPages).toBe(4);
}
async function openPrint(page, shortcut = false) {
  if (shortcut) { await page.keyboard.press('Control+p'); await fresh(page); }
  else await click(page, 'print');
  await bounds(page, 'print-dialog-print');
  await expect.poll(async () => (await state(page)).Inputs['print-report-name']).toBe('Print page options contract');
}
async function closed(page) { await expect.poll(async () => Boolean((await state(page)).Commands['print-dialog-print'])).toBe(false); }
async function submit(page) {
  const waiting = page.waitForEvent('popup'); await click(page, 'print-dialog-print'); const output = await waiting;
  await expect.poll(() => output.locator('.page').count()).toBeGreaterThan(0);
  await expect.poll(() => output.evaluate(() => window.__printInvocations)).toBe(1);
  await closed(page); return output;
}
async function labels(output) { return output.locator('.page').evaluateAll(pages => pages.map(p => p.getAttribute('aria-label'))); }
async function unchanged(page, before) {
  const after = await state(page);
  expect(after.ContentRevision).toBe(before.ContentRevision); expect(after.HistoryCount).toBe(before.HistoryCount);
  expect(after.PreviewPage).toBe(before.PreviewPage); expect(after.PreparedPages).toBe(before.PreparedPages);
  expect(after.PreviewIndexBuilds).toBe(before.PreviewIndexBuilds); expect(after.Dirty).toBe(false); expect(after.Diagnostics).toEqual([]);
}

test('native print dialog invalid and cancelled ranges never open a print document or edit report content', async ({ context, page }, info) => {
  await initialize(context, page); const original = await save(page), before = await state(page); const popups = [];
  page.on('popup', popup => popups.push(popup));
  await openPrint(page); expect((await state(page)).Inputs['print-destination']).toBe('System print dialog');
  await click(page, 'output-pages-custom'); await fill(page, 'output-page-numbers', '0,5');
  await click(page, 'print-dialog-print'); await expect.poll(async () => (await state(page)).Inputs['print-error']).toContain('1 to 4');
  expect(popups).toHaveLength(0); await unchanged(page, before);
  await screenshot(page, info.outputPath('native-print-range-validation.png'));
  await click(page, 'print-dialog-cancel'); await closed(page); expect(popups).toHaveLength(0);
  expect(await save(page)).toEqual(original); await artifact(info, 'cancelled-print-state.json', await state(page));
});

test('native print ranges and reverse order reach the actual print document while current-page shortcut stays ordinal', async ({ context, page }, info) => {
  await initialize(context, page); await click(page, 'next-page'); const original = await save(page), before = await state(page);
  await openPrint(page); await click(page, 'output-pages-custom'); await fill(page, 'output-page-numbers', '4,1-2,2');
  await click(page, 'print-reverse'); await screenshot(page, info.outputPath('native-print-options.png'));
  const reversed = await submit(page); expect(await labels(reversed)).toEqual(['Page 4', 'Page 2', 'Page 1']);
  expect(await reversed.locator('.page > svg').count()).toBe(3);
  const paper = await reversed.locator('.page').first().boundingBox(); expect(paper.width).toBeCloseTo(288, 0); expect(paper.height).toBeCloseTo(384, 0);
  expect(await reversed.evaluate(() => window.opener)).toBeNull();
  await reversed.screenshot({ path: info.outputPath('reversed-print-document.png'), fullPage: true }); await reversed.close();
  await unchanged(page, before); await page.bringToFront(); await openPrint(page, true); await click(page, 'output-pages-current');
  const current = await submit(page); expect(await labels(current)).toEqual(['Page 2']);
  await current.close(); await page.bringToFront(); await unchanged(page, before); expect(await save(page)).toEqual(original);
  await artifact(info, 'print-order-and-current.json', { reversed: [4, 2, 1], current: 2, final: await state(page) });
});

test('native print popup refusal preserves the range draft and permits an explicit successful retry', async ({ context, page }, info) => {
  await initialize(context, page, true); const before = await state(page);
  await openPrint(page); await click(page, 'output-pages-custom'); await fill(page, 'output-page-numbers', '2-3');
  await click(page, 'print-dialog-print');
  await expect.poll(async () => (await state(page)).Inputs['print-error']).toContain('blocked');
  expect((await state(page)).Inputs['output-page-numbers']).toBe('2-3'); await unchanged(page, before);
  const output = await submit(page); expect(await labels(output)).toEqual(['Page 2', 'Page 3']);
  await output.close(); await page.bringToFront(); await unchanged(page, before);
  await artifact(info, 'print-popup-refusal-recovery.json', await state(page));
});
