import { test, expect } from '@playwright/test';
import { ready, state, fresh, bounds, click, choose, save, screenshot, artifact } from './uno-driver.mjs';
import { designerFixture, selectObject } from './designer-fixture.mjs';
import { screenshotPixels } from './screenshot-pixels.mjs';

async function load(page) {
  const report = designerFixture(); report.name = 'Border and paragraph controls';
  report.pages[0].bands[0].items[0].style.borderWidth = 1.125;
  report.pages[0].bands[0].items[1].style.borderWidth = 2.375;
  for (const item of report.pages[0].bands[0].items) item.style.borderColor = '#108840';
  report.pages[0].bands[0].items[1].style.verticalAlignment = 'Bottom';
  const child = report.pages[0].bands[0].children[0].items[0]; child.locked = true; child.style = { verticalAlignment: 'Bottom' };
  await click(page, 'tab-report'); const r = await bounds(page, 'open'), chooser = page.waitForEvent('filechooser');
  await page.mouse.click(r.X + r.Width / 2, r.Y + r.Height / 2);
  await (await chooser).setFiles({ name: 'appearance.report.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(report)) });
  await expect.poll(async () => (await state(page)).Document).toBe(report.name);
  await click(page, 'tab-home'); await fresh(page);
}
async function input(page, id, expected) {
  await expect.poll(async () => (await state(page)).Inputs[id]).toBe(expected);
}

test('native border width keeps precise and mixed values while coherent toggles preserve existing widths', async ({ page }, info) => {
  await ready(page); await load(page); await selectObject(page, 'a'); const original = await save(page), before = await state(page);
  await input(page, 'border-width-value', '1.125 pt'); await choose(page, 'border-width', 10);
  await selectObject(page, 'b'); await input(page, 'border-width-value', '2.375 pt');
  await selectObject(page, 'a'); await input(page, 'border-width-value', '1.125 pt');
  await selectObject(page, 'b', true); await input(page, 'border-width', '-1'); await input(page, 'border-width-value', '');
  await input(page, 'border', 'True'); expect((await state(page)).HistoryCount).toBe(before.HistoryCount);
  expect((await state(page)).ContentRevision).toBe(before.ContentRevision); expect(await save(page)).toEqual(original);
  await choose(page, 'border-width', 0); await input(page, 'border-width-value', 'None'); await input(page, 'border', 'False');
  await choose(page, 'border-width', 9); await input(page, 'border-width-value', '6 pt'); await input(page, 'border', 'True');
  const expected = structuredClone(original); for (const item of expected.pages[0].bands[0].items) item.style.borderWidth = 6;
  expect(await save(page)).toEqual(expected); expect((await state(page)).HistoryCount).toBe(before.HistoryCount + 2);
  await expect.poll(async () => {
    const r = (await state(page)).Objects.find(item => item.Id === 'a').Screen;
    const pixel = screenshotPixels(await screenshot(page, info.outputPath('native-border-width.png'))).pixel(r.X + 24, r.Y + 2);
    return [16, 136, 64].every((channel, i) => Math.abs(channel - pixel[i]) <= 8);
  }, { message: 'The six-point green border is actually painted inside the selected object' }).toBe(true);
  await click(page, 'quick-undo'); await click(page, 'quick-undo'); expect(await save(page)).toEqual(original);
  await selectObject(page, 'b'); await choose(page, 'border-width', 0); const mixed = await save(page);
  await selectObject(page, 'a', true); await input(page, 'border', 'Mixed'); const mixedState = await state(page);
  await click(page, 'border'); await input(page, 'border', 'True');
  const together = structuredClone(mixed); together.pages[0].bands[0].items[1].style.borderWidth = 1;
  expect(await save(page)).toEqual(together); expect(together.pages[0].bands[0].items[0].style.borderWidth).toBe(1.125);
  expect((await state(page)).HistoryCount).toBe(mixedState.HistoryCount + 1);
  await click(page, 'quick-undo'); expect(await save(page)).toEqual(mixed); await input(page, 'border', 'Mixed');
  expect((await state(page)).Diagnostics).toEqual([]); await artifact(info, 'native-border-width-state.json', await state(page));
});

test('native paragraph alignment shows single and mixed state and respects locked targets and exact undo', async ({ page }, info) => {
  await ready(page); await load(page); await selectObject(page, 'a'); const original = await save(page), before = await state(page);
  await input(page, 'vertical-top', 'True'); await input(page, 'vertical-center', 'False'); await input(page, 'vertical-bottom', 'False');
  await click(page, 'vertical-top'); await input(page, 'vertical-top', 'True');
  expect((await state(page)).HistoryCount).toBe(before.HistoryCount); expect((await state(page)).ContentRevision).toBe(before.ContentRevision);
  await selectObject(page, 'b', true);
  for (const id of ['vertical-top', 'vertical-center', 'vertical-bottom']) await input(page, id, 'False');
  await click(page, 'vertical-center'); await input(page, 'vertical-center', 'True');
  const middle = structuredClone(original); for (const item of middle.pages[0].bands[0].items) item.style.verticalAlignment = 'Center';
  expect(await save(page)).toEqual(middle); expect((await state(page)).HistoryCount).toBe(before.HistoryCount + 1);
  await selectObject(page, 'c', true); await input(page, 'vertical-center', 'False'); await input(page, 'vertical-bottom', 'False');
  await click(page, 'vertical-bottom'); await input(page, 'vertical-bottom', 'True');
  const bottom = structuredClone(original); for (const item of bottom.pages[0].bands[0].items) item.style.verticalAlignment = 'Bottom';
  expect(await save(page)).toEqual(bottom); expect((await state(page)).HistoryCount).toBe(before.HistoryCount + 2);
  await screenshot(page, info.outputPath('native-paragraph-alignment.png'));
  await selectObject(page, 'c');
  for (const id of ['vertical-top', 'vertical-center', 'vertical-bottom', 'border', 'border-width']) await input(page, id + '-enabled', 'False');
  await choose(page, 'object-selector', 0); await input(page, 'border-width-value', '');
  for (const id of ['vertical-top', 'vertical-center', 'vertical-bottom', 'border', 'border-width']) await input(page, id + '-enabled', 'False');
  expect((await state(page)).HistoryCount).toBe(before.HistoryCount + 2);
  await click(page, 'quick-undo'); expect(await save(page)).toEqual(middle);
  await click(page, 'quick-undo'); expect(await save(page)).toEqual(original);
  expect((await state(page)).Diagnostics).toEqual([]); await artifact(info, 'native-paragraph-state.json', await state(page));
});
