import { test, expect } from '@playwright/test';
import { state, ready, fresh, bounds, click, fill, save, artifact, screenshot } from './uno-driver.mjs';
import { designerFixture, loadDesignerFixture, selectObject } from './designer-fixture.mjs';

async function switchTree(page, name) {
  await click(page, name === 'Data' ? 'tree-data' : 'tree-report');
  await expect.poll(async () => (await state(page)).OutlineContextKey).toBe(name);
}

test('independent explorer tabs retain search expansion and native nodes without changing report history', async ({ page }, info) => {
  await ready(page); await loadDesignerFixture(page);
  await click(page, 'outline-collapse'); await fill(page, 'outline-filter', 'RowNumber');
  const dataCount = (await state(page)).OutlineProjectedCount;
  await switchTree(page, 'Report'); expect((await state(page)).OutlineFilter).toBe('');
  await click(page, 'outline-collapse'); await fill(page, 'outline-filter', 'NestedNote');
  await expect.poll(async () => (await state(page)).OutlineProjectedCount).toBe(4);
  const before = await state(page); expect(before.OutlineContextCount).toBe(2);
  for (let n = 0; n < 3; n++) {
    await switchTree(page, 'Data'); expect((await state(page)).OutlineFilter).toBe('RowNumber');
    expect((await state(page)).OutlineProjectedCount).toBe(dataCount);
    await switchTree(page, 'Report'); expect((await state(page)).OutlineFilter).toBe('NestedNote');
    expect((await state(page)).OutlineProjectedCount).toBe(4);
  }
  expect((await state(page)).OutlineCreatedNodes).toBe(before.OutlineCreatedNodes);
  expect((await state(page)).OutlineUpdates).toBe(before.OutlineUpdates);
  expect((await state(page)).OutlineSnapshotBuilds).toBe(before.OutlineSnapshotBuilds);
  await click(page, 'outline-clear'); await expect.poll(async () => (await state(page)).OutlineVisibleRows).toBe(2);
  await switchTree(page, 'Data'); expect((await state(page)).OutlineFilter).toBe('RowNumber');
  const r = await bounds(page, 'outline-filter'); await page.mouse.click(r.X + 20, r.Y + r.Height / 2); await page.keyboard.press('Escape');
  await expect.poll(async () => (await state(page)).OutlineFilter).toBe('');
  await expect.poll(async () => (await state(page)).OutlineVisibleRows).toBe(3);
  expect((await state(page)).HistoryCount).toBe(before.HistoryCount); expect((await state(page)).Dirty).toBe(false);
  expect((await state(page)).Diagnostics).toEqual([]);
  await screenshot(page, info.outputPath('independent-explorer-tabs.png')); await artifact(info, 'explorer-contexts.json', await state(page));
});

test('native report-tree keyboard navigation selects report objects but data-tree navigation never inserts them', async ({ page }, info) => {
  await ready(page); await loadDesignerFixture(page); await switchTree(page, 'Report');
  await fill(page, 'outline-filter', 'Main'); await click(page, 'outline-item:a');
  await expect.poll(async () => (await state(page)).Objects.find(o => o.Id === 'a').Selected).toBe(true);
  const before = await state(page);
  await page.keyboard.press('ArrowDown');
  await expect.poll(async () => (await state(page)).Objects.find(o => o.Id === 'b').Selected).toBe(true);
  await expect.poll(async () => (await state(page)).Inputs['font-size-value']).toBe('9');
  await page.keyboard.press('ArrowUp');
  await expect.poll(async () => (await state(page)).Objects.find(o => o.Id === 'a').Selected).toBe(true);
  await expect.poll(async () => (await state(page)).Inputs['font-size-value']).toBe('14');
  await switchTree(page, 'Data'); await fill(page, 'outline-filter', 'System Variables');
  await click(page, 'outline-system'); await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown'); await fresh(page);
  expect((await state(page)).ObjectCount).toBe(before.ObjectCount);
  expect((await state(page)).HistoryCount).toBe(before.HistoryCount); expect((await state(page)).Dirty).toBe(false);
  expect((await state(page)).Diagnostics).toEqual([]);
  await screenshot(page, info.outputPath('native-tree-keyboard.png')); await artifact(info, 'keyboard-tree-navigation.json', await state(page));
});

test('first selected object resize handles act on the selection and click jitter cannot create an edit', async ({ page }, info) => {
  await ready(page); await loadDesignerFixture(page); await selectObject(page, 'a'); await selectObject(page, 'b', true);
  const before = await state(page), r = before.Objects.find(o => o.Id === 'a').Screen, scale = before.Zoom * 96 / 72;
  await page.mouse.move(r.X + r.Width, r.Y + r.Height); await page.mouse.down();
  await page.mouse.move(r.X + r.Width + 12 * scale, r.Y + r.Height + 8 * scale, { steps: 5 }); await page.mouse.up();
  await expect.poll(async () => (await state(page)).ActiveDrag).toBe(false);
  await expect.poll(async () => (await state(page)).CanUndo).toBe(true);
  for (const id of ['a', 'b']) {
    const original = before.Objects.find(o => o.Id === id).Bounds;
    expect((await state(page)).Objects.find(o => o.Id === id).Bounds).toEqual({ ...original, Width: original.Width + 12, Height: original.Height + 8 });
  }
  expect((await state(page)).SelectedCount).toBe(2); expect((await state(page)).HistoryCount).toBe(1);
  await click(page, 'quick-undo'); await expect.poll(async () => (await state(page)).Dirty).toBe(false);
  await click(page, 'tab-view'); await click(page, 'snap-grid'); await click(page, 'tab-home');
  const old = await state(page), rect = old.Objects.find(o => o.Id === 'a').Screen;
  await page.mouse.move(rect.X + rect.Width / 2, rect.Y + rect.Height / 2); await page.mouse.down();
  await page.mouse.move(rect.X + rect.Width / 2 + 2, rect.Y + rect.Height / 2 + 2, { steps: 2 }); await page.mouse.up();
  await expect.poll(async () => (await state(page)).ActiveDrag).toBe(false);
  await expect.poll(async () => (await state(page)).SelectedCount).toBe(1); await fresh(page);
  for (const id of ['a', 'b']) expect((await state(page)).Objects.find(o => o.Id === id).Bounds).toEqual(before.Objects.find(o => o.Id === id).Bounds);
  expect((await state(page)).CanUndo).toBe(false); expect((await state(page)).Dirty).toBe(false);
  expect((await state(page)).Diagnostics).toEqual([]);
  await screenshot(page, info.outputPath('multi-selection-handle.png')); await artifact(info, 'selection-handles-and-jitter.json', await state(page));
});

test('save and page navigation reuse outline metadata while loading a report resets both explorer contexts', async ({ page }, info) => {
  await ready(page); await loadDesignerFixture(page); await fill(page, 'outline-filter', 'RowNumber');
  await switchTree(page, 'Report'); await fill(page, 'outline-filter', 'NestedNote');
  const before = await state(page); await save(page); await fresh(page);
  await click(page, 'page-tab-1'); await click(page, 'page-tab-0');
  expect((await state(page)).OutlineSnapshotBuilds).toBe(before.OutlineSnapshotBuilds);
  expect((await state(page)).OutlineCreatedNodes).toBe(before.OutlineCreatedNodes);
  expect((await state(page)).DocumentId).toBe(before.DocumentId);
  await click(page, 'tab-report'); const r = await bounds(page, 'open'), waiting = page.waitForEvent('filechooser');
  await page.mouse.click(r.X + r.Width / 2, r.Y + r.Height / 2);
  const replacement = designerFixture(); replacement.name = 'Replacement explorer report'; replacement.data[0].name = 'ReplacementData';
  await (await waiting).setFiles({ name: 'replacement.report.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(replacement)) });
  await expect.poll(async () => (await state(page)).Document).toBe(replacement.name);
  await expect.poll(async () => (await state(page)).DocumentId).not.toBe(before.DocumentId);
  expect((await state(page)).OutlineFilter).toBe('');
  await switchTree(page, 'Data'); expect((await state(page)).OutlineFilter).toBe('');
  await expect.poll(async () => (await state(page)).Inputs['outline-table:0']).toBe('ReplacementData (1 rows)');
  expect((await state(page)).OutlineContextCount).toBe(2);
  expect((await state(page)).CanUndo).toBe(false); expect((await state(page)).Dirty).toBe(false);
  expect((await state(page)).Diagnostics).toEqual([]);
  await screenshot(page, info.outputPath('replacement-explorer-contexts.png')); await artifact(info, 'outline-metadata-lifecycle.json', { before, after: await state(page) });
});
