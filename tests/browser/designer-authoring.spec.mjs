import { test, expect } from '@playwright/test';
import { state, ready, fresh, bounds, click, choose, save, artifact, screenshot } from './uno-driver.mjs';
import { loadDesignerFixture, selectObject } from './designer-fixture.mjs';

const handles = [
  ['TopLeft', 0, 0, 52, 48, 188, 52], ['Top', .5, 0, 40, 48, 200, 52], ['TopRight', 1, 0, 40, 48, 212, 52],
  ['Right', 1, .5, 40, 40, 212, 60], ['BottomRight', 1, 1, 40, 40, 212, 68], ['Bottom', .5, 1, 40, 40, 200, 68],
  ['BottomLeft', 0, 1, 52, 40, 188, 68], ['Left', 0, .5, 52, 40, 188, 60]
];

test('all eight visible resize handles anchor opposite edges and preserve undo cancellation and locking', async ({ page }, info) => {
  await ready(page); await loadDesignerFixture(page); await selectObject(page, 'a');
  const original = (await state(page)).Objects.find(o => o.Id === 'a').Bounds;
  for (const [name, hx, hy, x, y, width, height] of handles) {
    const snapshot = await state(page), r = snapshot.Objects.find(o => o.Id === 'a').Screen, scale = snapshot.Zoom * 96 / 72;
    await page.mouse.move(r.X + r.Width * hx, r.Y + r.Height * hy); await page.mouse.down();
    await page.mouse.move(r.X + r.Width * hx + 12 * scale, r.Y + r.Height * hy + 8 * scale, { steps: 4 }); await page.mouse.up();
    await expect.poll(async () => (await state(page)).Objects.find(o => o.Id === 'a').Bounds, { message: name }).toEqual({ X: x, Y: y, Width: width, Height: height });
    expect((await state(page)).HistoryCount).toBe(1); expect((await state(page)).CanUndo).toBe(true);
    await click(page, 'quick-undo'); await expect.poll(async () => (await state(page)).Objects.find(o => o.Id === 'a').Bounds).toEqual(original);
    expect((await state(page)).Dirty).toBe(false);
  }
  let r = (await state(page)).Objects.find(o => o.Id === 'a').Screen;
  await page.mouse.move(r.X, r.Y); await page.mouse.down(); await page.mouse.move(r.X + 25, r.Y + 25, { steps: 4 }); await page.keyboard.press('Escape'); await page.mouse.up();
  await expect.poll(async () => (await state(page)).ActiveDrag).toBe(false); expect((await state(page)).Objects.find(o => o.Id === 'a').Bounds).toEqual(original);
  expect((await state(page)).CanUndo).toBe(false);
  await selectObject(page, 'a'); await click(page, 'tab-layout'); await click(page, 'lock-objects');
  await expect.poll(async () => (await state(page)).Objects.find(o => o.Id === 'a').Locked).toBe(true);
  r = (await state(page)).Objects.find(o => o.Id === 'a').Screen;
  await page.mouse.move(r.X + r.Width / 2, r.Y + r.Height / 2); await page.mouse.down(); await page.mouse.move(r.X + r.Width / 2 + 30, r.Y + r.Height / 2 + 30, { steps: 4 }); await page.mouse.up();
  expect((await state(page)).Objects.find(o => o.Id === 'a').Bounds).toEqual(original);
  await click(page, 'unlock-objects'); await expect.poll(async () => (await state(page)).Objects.find(o => o.Id === 'a').Locked).toBe(false);
  expect((await state(page)).Diagnostics).toEqual([]); await screenshot(page, info.outputPath('eight-resize-handles.png'));
  await artifact(info, 'resize-native-input.json', await state(page));
});

test('selection saving and zoom reuse authoring layout while edits update geometry without rebuilding tree nodes', async ({ page }, info) => {
  await ready(page); await loadDesignerFixture(page); await click(page, 'tree-report'); await selectObject(page, 'a');
  const before = await state(page); await selectObject(page, 'b'); await selectObject(page, 'a'); await save(page); await fresh(page);
  expect((await state(page)).DesignLayoutBuilds).toBe(before.DesignLayoutBuilds);
  expect((await state(page)).OutlineCreatedNodes).toBe(before.OutlineCreatedNodes);
  await click(page, 'status-zoom-in'); await expect.poll(async () => (await state(page)).Zoom).toBeCloseTo(before.Zoom + .1, 4);
  await click(page, 'tab-view'); await choose(page, 'design-zoom', 5); await expect.poll(async () => (await state(page)).Zoom).toBe(1.25);
  expect((await state(page)).DesignLayoutBuilds).toBe(before.DesignLayoutBuilds);
  await click(page, 'tab-home'); await selectObject(page, 'a'); const original = (await state(page)).Objects.find(o => o.Id === 'a').Bounds;
  await page.keyboard.press('ArrowRight'); await expect.poll(async () => (await state(page)).Objects.find(o => o.Id === 'a').Bounds.X).toBe(original.X + 1);
  await expect.poll(async () => (await state(page)).DesignLayoutBuilds).toBe(before.DesignLayoutBuilds + 1);
  expect((await state(page)).OutlineCreatedNodes).toBe(before.OutlineCreatedNodes); expect((await state(page)).OutlineUpdates).toBe(before.OutlineUpdates);
  expect((await state(page)).ReplacementScanCount).toBe(4);
  await click(page, 'quick-undo'); await expect.poll(async () => (await state(page)).Objects.find(o => o.Id === 'a').Bounds).toEqual(original);
  await click(page, 'quick-redo'); await expect.poll(async () => (await state(page)).Objects.find(o => o.Id === 'a').Bounds.X).toBe(original.X + 1);
  expect((await state(page)).Diagnostics).toEqual([]); await artifact(info, 'authoring-layout-reuse.json', { before, after: await state(page) });
});
