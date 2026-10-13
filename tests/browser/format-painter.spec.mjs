import { test, expect } from '@playwright/test';
import { ready, state, fresh, bounds, click, save, screenshot, artifact } from './uno-driver.mjs';
import { designerFixture, selectObject } from './designer-fixture.mjs';
import { screenshotPixels } from './screenshot-pixels.mjs';

async function load(page) {
  const report = designerFixture(); report.name = 'Format Painter contract';
  report.pages[0].bands[0].items[0].style = { fontFamily: 'Arial', fontSize: 14, bold: true, italic: true, underline: true,
    foreground: '#FFFFFF', fill: '#108840', borderColor: '#244F75', borderWidth: 1, padding: 4, lineHeight: 1.4,
    alignment: 'Right', verticalAlignment: 'Bottom' };
  const previous = (await state(page)).DocumentId;
  await click(page, 'tab-report'); const r = await bounds(page, 'open'), chooser = page.waitForEvent('filechooser');
  await page.mouse.click(r.X + r.Width / 2, r.Y + r.Height / 2);
  await (await chooser).setFiles({ name: 'format.report.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(report)) });
  await expect.poll(async () => (await state(page)).DocumentId).not.toBe(previous);
  await expect.poll(async () => (await state(page)).Document).toBe(report.name);
  await click(page, 'tab-home'); await fresh(page);
}
async function active(page, value) {
  await expect.poll(async () => (await state(page)).Inputs['format-painter']).toBe(String(value ? 'True' : 'False'));
}
async function rawObjectClick(page, id) {
  const r = (await state(page)).Objects.find(o => o.Id === id).Screen;
  await page.mouse.click(r.X + r.Width / 2, r.Y + r.Height / 2); await fresh(page);
}
async function paintedFill(page, info) {
  await expect.poll(async () => {
    const r = (await state(page)).Objects.find(o => o.Id === 'b').Screen;
    const pixel = screenshotPixels(await screenshot(page, info.outputPath('native-format-painter.png'))).pixel(r.X + 10, r.Y + 10);
    return [16, 136, 64].every((channel, i) => Math.abs(channel - pixel[i]) <= 8);
  }, { message: 'The target is actually painted green, not merely reported with a new style' }).toBe(true);
}

test('native format painter copies appearance with one click and exact undo without changing content or geometry', async ({ page }, info) => {
  await ready(page); await load(page); await selectObject(page, 'a'); const original = await save(page), before = await state(page);
  await click(page, 'format-painter'); await active(page, true);
  expect((await state(page)).ContentRevision).toBe(before.ContentRevision);
  expect((await state(page)).DesignLayoutBuilds).toBe(before.DesignLayoutBuilds);
  expect((await state(page)).HistoryCount).toBe(before.HistoryCount);
  await selectObject(page, 'b'); await active(page, false);
  await expect.poll(async () => (await state(page)).Inputs['font-family-value']).toBe('Arial');
  expect((await state(page)).Inputs['font-size-value']).toBe('14');
  expect((await state(page)).HistoryCount).toBe(before.HistoryCount + 1); expect((await state(page)).ActiveDrag).toBe(false);
  await paintedFill(page, info);
  const expected = structuredClone(original); expected.pages[0].bands[0].items[1].style = original.pages[0].bands[0].items[0].style;
  expect(await save(page)).toEqual(expected);
  await click(page, 'quick-undo'); expect(await save(page)).toEqual(original);
  await expect.poll(async () => (await state(page)).Objects.find(o => o.Id === 'a').Selected).toBe(true);
  await click(page, 'quick-redo'); expect(await save(page)).toEqual(expected); await active(page, false);
  expect((await state(page)).Diagnostics).toEqual([]); await artifact(info, 'format-painter-roundtrip.report.json', expected);
});

test('native format painter repeat mode skips locked targets and Escape cancels without another edit', async ({ page }, info) => {
  await ready(page); await load(page); await selectObject(page, 'b'); await click(page, 'tab-layout'); await click(page, 'lock-objects');
  await click(page, 'tab-home'); await selectObject(page, 'a'); const original = await save(page), before = await state(page);
  async function repeat() {
    const r = await bounds(page, 'format-painter'); await page.mouse.dblclick(r.X + r.Width / 2, r.Y + r.Height / 2); await fresh(page);
    await active(page, true); await expect.poll(async () => (await state(page)).Status).toContain('multiple objects');
  }
  await repeat();
  // The next ordinary click cancels; the double-tap release must not consume it.
  await click(page, 'format-painter'); await active(page, false);
  expect((await state(page)).HistoryCount).toBe(before.HistoryCount);
  await repeat();
  await rawObjectClick(page, 'b'); await active(page, true); expect(await save(page)).toEqual(original);
  expect((await state(page)).HistoryCount).toBe(before.HistoryCount); expect((await state(page)).Objects.find(o => o.Id === 'a').Selected).toBe(true);
  await selectObject(page, 'c'); await active(page, true);
  expect((await state(page)).HistoryCount).toBe(before.HistoryCount + 1);
  const painted = await save(page); expect(painted.pages[0].bands[0].children[0].items[0].style).toEqual(original.pages[0].bands[0].items[0].style);
  expect(painted.pages[0].bands[0].items).toEqual(original.pages[0].bands[0].items);
  // An equal-style target does not generate a second history entry in repeat mode.
  await selectObject(page, 'a'); await active(page, true); expect((await state(page)).HistoryCount).toBe(before.HistoryCount + 1);
  await page.keyboard.press('Escape'); await fresh(page); await active(page, false);
  expect((await state(page)).Objects.find(o => o.Id === 'a').Selected).toBe(true); expect(await save(page)).toEqual(painted);
  await click(page, 'quick-undo'); expect(await save(page)).toEqual(original); await active(page, false);
  expect((await state(page)).Diagnostics).toEqual([]); await artifact(info, 'repeat-format-painter-state.json', await state(page));
});

test('native format painter cancels stale captures and rejects ambiguous mixed-selection sources', async ({ page }, info) => {
  await ready(page); await load(page); await selectObject(page, 'a'); await selectObject(page, 'b', true);
  await expect.poll(async () => (await state(page)).Inputs['format-painter-enabled']).toBe('False');
  await selectObject(page, 'a'); await click(page, 'format-painter'); await active(page, true);
  // Keyboard activation still follows the native ToggleButton path.
  await page.keyboard.press('Space'); await fresh(page); await active(page, false);
  await page.keyboard.press('Space'); await fresh(page); await active(page, true);
  await page.keyboard.press('Escape'); await fresh(page); await active(page, false);
  expect((await state(page)).HistoryCount).toBe(0); expect((await state(page)).Objects.find(o => o.Id === 'a').Selected).toBe(true);
  await click(page, 'format-painter'); await active(page, true); await click(page, 'bold'); await active(page, false);
  await click(page, 'quick-undo'); await active(page, false);
  await click(page, 'format-painter'); await active(page, true); await load(page); await active(page, false);
  expect((await state(page)).HistoryCount).toBe(0); expect((await state(page)).Dirty).toBe(false);
  await selectObject(page, 'b'); expect((await state(page)).Inputs['font-size-value']).toBe('9');
  expect((await state(page)).Diagnostics).toEqual([]); await artifact(info, 'format-painter-invalidation.json', await state(page));
});
