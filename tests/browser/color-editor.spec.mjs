import { test, expect } from '@playwright/test';
import { ready, state, fresh, bounds, click, fill, save, artifact, screenshot } from './uno-driver.mjs';
import { loadDesignerFixture, selectObject } from './designer-fixture.mjs';

async function setup(page) {
  await ready(page); await loadDesignerFixture(page); await selectObject(page, 'a');
}
async function openColor(page, command = 'fill-color') {
  await click(page, command); await bounds(page, 'color-dialog-apply');
  await expect.poll(async () => (await state(page)).Inputs['color-value'] || '').toMatch(/^#[0-9A-F]{6}([0-9A-F]{2})?$/);
}
async function closed(page) {
  await expect.poll(async () => Boolean((await state(page)).Commands['color-dialog-apply'])).toBe(false);
}
function items(report) { return report.pages[0].bands[0].items; }
async function previewPixel(page, info, expected, name) {
  await expect.poll(async () => {
    const r = await bounds(page, 'color-current-preview');
    const bytes = await screenshot(page, info.outputPath(name));
    return page.evaluate(async ({ base64, r, expected }) => {
      const image = new Image(); image.src = `data:image/png;base64,${base64}`; await image.decode();
      const canvas = document.createElement('canvas'); canvas.width = image.width; canvas.height = image.height;
      const context = canvas.getContext('2d'); context.drawImage(image, 0, 0);
      // The first checkerboard cell is white; avoid text, border and other cells.
      const pixel = context.getImageData(Math.floor(r.X + 8), Math.floor(r.Y + 8), 1, 1).data;
      return expected.every((value, i) => Math.abs(pixel[i] - value) <= 8);
    }, { base64: bytes.toString('base64'), r, expected });
  }, { message: 'Actual checkerboard preview must paint the selected RGBA color' }).toBe(true);
}

test('native color editor retains invalid and incrementally typed ARGB drafts without changing the report', async ({ page }, info) => {
  await setup(page); const original = await save(page), before = await state(page);
  await openColor(page); const initial = (await state(page)).Inputs['color-value'];
  await fill(page, 'color-hex', '#123'); await click(page, 'color-dialog-apply');
  await expect.poll(async () => (await state(page)).Inputs['color-dialog-error']).toContain('#RRGGBB');
  expect((await state(page)).Inputs['color-hex']).toBe('#123'); expect((await state(page)).Inputs['color-value']).toBe(initial);
  expect((await state(page)).ContentRevision).toBe(before.ContentRevision);
  const r = await bounds(page, 'color-hex'); await page.mouse.click(r.X + 25, r.Y + r.Height - 15);
  await page.keyboard.press('Control+a'); await page.keyboard.type('80112233', { delay: 50 });
  await expect.poll(async () => (await state(page)).Inputs['color-hex']).toBe('80112233');
  await expect.poll(async () => (await state(page)).Inputs['color-value']).toBe('#80112233');
  for (const [id, value] of [['red', '17'], ['green', '34'], ['blue', '51'], ['alpha', '128']])
    expect((await state(page)).Inputs['color-' + id]).toBe(value);
  await previewPixel(page, info, [136, 144, 153], 'argb-native-preview.png');
  await fill(page, 'color-red', '256'); await click(page, 'color-dialog-apply');
  await expect.poll(async () => (await state(page)).Inputs['color-dialog-error']).toContain('0 to 255');
  expect((await state(page)).Inputs['color-value']).toBe('#80112233');
  await click(page, 'color-dialog-cancel'); await closed(page);
  expect((await state(page)).HistoryCount).toBe(before.HistoryCount); expect((await state(page)).Dirty).toBe(false);
  expect(await save(page)).toEqual(original); expect((await state(page)).Diagnostics).toEqual([]);
  await artifact(info, 'color-draft-cancellation.json', await state(page));
});

test('native color palette keyboard selection commits once across objects and mixed untouched Apply stays unchanged', async ({ page }, info) => {
  await setup(page); const original = await save(page);
  await selectObject(page, 'b', true); const before = await state(page);
  await openColor(page); await click(page, 'color-palette-10');
  await page.keyboard.press('ArrowRight'); await page.keyboard.press('Space'); await fresh(page);
  await expect.poll(async () => (await state(page)).Inputs['color-value']).toBe('#FF8080');
  await previewPixel(page, info, [255, 128, 128], 'palette-native-preview.png');
  expect((await state(page)).ContentRevision).toBe(before.ContentRevision);
  await click(page, 'color-dialog-apply'); await closed(page);
  expect((await state(page)).HistoryCount).toBe(before.HistoryCount + 1);
  expect(items(await save(page)).map(i => i.style.fill)).toEqual(['#FF8080', '#FF8080']);
  await click(page, 'quick-undo'); expect(await save(page)).toEqual(original);
  await selectObject(page, 'a'); await openColor(page); await click(page, 'color-palette-10'); await click(page, 'color-dialog-apply'); await closed(page);
  await selectObject(page, 'b', true); const mixedReport = await save(page), mixed = await state(page);
  await openColor(page); expect((await state(page)).Inputs['color-selection-summary']).toContain('Mixed colors');
  await click(page, 'color-dialog-apply'); await closed(page);
  expect((await state(page)).ContentRevision).toBe(mixed.ContentRevision); expect((await state(page)).HistoryCount).toBe(mixed.HistoryCount);
  expect(await save(page)).toEqual(mixedReport); expect((await state(page)).Diagnostics).toEqual([]);
  await artifact(info, 'color-batch-and-mixed.json', await state(page));
});

test('native color spectrum RGBA transparency and text border targets round trip through report files', async ({ page }, info) => {
  await setup(page); await openColor(page); await click(page, 'color-palette-29');
  await expect.poll(async () => (await state(page)).Inputs['color-value']).toBe('#0000FF');
  const spectrum = await bounds(page, 'color-spectrum');
  await page.mouse.click(spectrum.X + spectrum.Width * .3, spectrum.Y + spectrum.Height * .3); await fresh(page);
  await expect.poll(async () => (await state(page)).Inputs['color-value']).not.toBe('#0000FF');
  for (const [id, value] of [['red', '17'], ['green', '34'], ['blue', '51'], ['alpha', '128']]) await fill(page, 'color-' + id, value);
  await expect.poll(async () => (await state(page)).Inputs['color-value']).toBe('#80112233');
  await previewPixel(page, info, [136, 144, 153], 'spectrum-rgba-preview.png');
  await click(page, 'color-dialog-apply'); await closed(page);
  expect(items(await save(page))[0].style.fill).toBe('#80112233');
  const applied = await state(page); await openColor(page); await click(page, 'color-transparent');
  await expect.poll(async () => (await state(page)).Inputs['color-value']).toBe('#00000000');
  await click(page, 'color-restore'); await expect.poll(async () => (await state(page)).Inputs['color-value']).toBe('#80112233');
  await click(page, 'color-dialog-apply'); await closed(page); expect((await state(page)).ContentRevision).toBe(applied.ContentRevision);
  await openColor(page, 'text-color'); await click(page, 'color-palette-22'); await click(page, 'color-dialog-apply'); await closed(page);
  await openColor(page, 'border-color'); await click(page, 'color-palette-6'); await click(page, 'color-dialog-apply'); await closed(page);
  const report = await save(page), first = items(report)[0];
  expect(first.style.fill).toBe('#80112233'); expect(first.style.foreground).toBe('#00A000'); expect(first.style.borderColor).toBe('#244F75');
  expect(first.style.borderWidth ?? 0).toBe(0); expect((await state(page)).Diagnostics).toEqual([]);
  await artifact(info, 'native-color-roundtrip.report.json', report);
});

test('native color actions respect locked selections and cancel restores all saved colors', async ({ page }, info) => {
  await setup(page); await selectObject(page, 'b'); await click(page, 'tab-layout'); await click(page, 'lock-objects'); await click(page, 'tab-home');
  for (const id of ['fill-color', 'text-color', 'border-color']) expect((await state(page)).Inputs[id + '-enabled']).toBe('False');
  await selectObject(page, 'a'); await selectObject(page, 'b', true); const original = await save(page), before = await state(page);
  await openColor(page); await click(page, 'color-palette-9'); await click(page, 'color-dialog-apply'); await closed(page);
  const changed = await save(page); expect(items(changed)[0].style.fill).toBe('#C43E3E');
  expect(items(changed)[1]).toEqual(items(original)[1]); expect((await state(page)).HistoryCount).toBe(before.HistoryCount + 1);
  await click(page, 'quick-undo'); expect(await save(page)).toEqual(original);
  await openColor(page); await click(page, 'color-palette-5'); await click(page, 'color-dialog-cancel'); await closed(page);
  expect(await save(page)).toEqual(original); expect((await state(page)).Diagnostics).toEqual([]);
  await artifact(info, 'color-locked-selection.json', await state(page));
});
