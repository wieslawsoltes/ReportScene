import { test, expect } from '@playwright/test';
import { state, ready, fresh, bounds, click, choose, save, artifact, screenshot } from './uno-driver.mjs';
import { loadDesignerFixture, selectObject, typeProperty, filterProperties } from './designer-fixture.mjs';

async function paperPixel(page) {
  const bytes = await screenshot(page);
  return page.evaluate(async base64 => {
    const image = new Image(); image.src = `data:image/png;base64,${base64}`; await image.decode();
    const canvas = document.createElement('canvas'); canvas.width = image.width; canvas.height = image.height;
    const context = canvas.getContext('2d'); context.drawImage(image, 0, 0);
    return Array.from(context.getImageData(700, 800, 1, 1).data);
  }, bytes.toString('base64'));
}

test('page property selection edits paper and margins and compact paper changes only the authoring view', async ({ page }, info) => {
  await ready(page); await loadDesignerFixture(page); const initial = await state(page);
  expect(initial.FullPaper).toBe(false); expect(initial.DesignPaperHeight).toBeLessThan(initial.SourcePaperHeight);
  const compactPixel = await paperPixel(page); expect(Math.max(...compactPixel.slice(0, 3))).toBeLessThan(230);
  await click(page, 'tab-view'); await click(page, 'show-full-paper');
  await expect.poll(async () => (await state(page)).FullPaper).toBe(true);
  expect((await state(page)).DesignPaperHeight).toBe((await state(page)).SourcePaperHeight);
  const fullPixel = await paperPixel(page); expect(Math.min(...fullPixel.slice(0, 3))).toBeGreaterThan(245);
  await click(page, 'show-full-paper'); expect((await state(page)).HistoryCount).toBe(initial.HistoryCount);
  await click(page, 'tab-home'); await choose(page, 'object-selector', 0);
  await expect.poll(async () => (await state(page)).PropertyProjection).toContain('page-name');
  await filterProperties(page, 'Paper');
  await typeProperty(page, 'page-width', '620');
  await expect.poll(async () => (await state(page)).Inputs['property:page-width']).toBe('620');
  await typeProperty(page, 'page-height', '900');
  await expect.poll(async () => (await state(page)).SourcePaperHeight).toBe(900);
  await click(page, 'quick-undo'); await expect.poll(async () => (await state(page)).SourcePaperHeight).toBe(initial.SourcePaperHeight);
  await expect.poll(async () => (await state(page)).PropertyProjection).toContain('page-width');
  await click(page, 'quick-redo'); await expect.poll(async () => (await state(page)).SourcePaperHeight).toBe(900);
  await filterProperties(page, 'Margins'); const revision = (await state(page)).ContentRevision;
  await typeProperty(page, 'page-margin-left', '1000');
  await expect.poll(async () => (await state(page)).PropertyValidation.length).toBeGreaterThan(0);
  expect((await state(page)).ContentRevision).toBe(revision);
  await page.keyboard.press('Escape'); await typeProperty(page, 'page-margin-left', '40');
  await filterProperties(page, 'name'); await typeProperty(page, 'page-name', 'MainRenamed');
  const report = await save(page);
  expect(report.pages[0].name).toBe('MainRenamed'); expect(report.pages[0].width).toBe(620);
  expect(report.pages[0].height).toBe(900); expect(report.pages[0].margins.left).toBe(40);
  expect(report.pages[0].bands[0].items.map(i => i.id)).toEqual(['a', 'b']);
  await click(page, 'properties-clear'); await screenshot(page, info.outputPath('native-page-properties.png'));
  await click(page, 'preview'); expect((await state(page)).Diagnostics).toEqual([]);
  await artifact(info, 'page-properties.report.json', report);
});

test('ordinary multi-selection clicks select one object while physical dragging moves the complete selection', async ({ page }, info) => {
  await ready(page); await loadDesignerFixture(page); await selectObject(page, 'a'); await selectObject(page, 'b', true);
  await expect.poll(async () => (await state(page)).SelectedCount).toBe(2);
  const before = await state(page), r = before.Objects.find(o => o.Id === 'a').Screen, scale = before.Zoom * 96 / 72;
  await page.mouse.move(r.X + r.Width / 2, r.Y + r.Height / 2); await page.mouse.down();
  await page.mouse.move(r.X + r.Width / 2 + 12 * scale, r.Y + r.Height / 2 + 8 * scale, { steps: 5 }); await page.mouse.up();
  await expect.poll(async () => (await state(page)).ActiveDrag).toBe(false);
  await expect.poll(async () => (await state(page)).CanUndo).toBe(true);
  expect((await state(page)).SelectedCount).toBe(2);
  for (const id of ['a', 'b']) {
    const old = before.Objects.find(o => o.Id === id).Bounds;
    expect((await state(page)).Objects.find(o => o.Id === id).Bounds).toEqual({ ...old, X: old.X + 12, Y: old.Y + 8 });
  }
  await click(page, 'quick-undo'); await expect.poll(async () => (await state(page)).Dirty).toBe(false);
  await selectObject(page, 'a'); expect((await state(page)).SelectedCount).toBe(1);
  expect((await state(page)).CanUndo).toBe(false); expect((await state(page)).Diagnostics).toEqual([]);
  await artifact(info, 'click-versus-group-drag.json', await state(page));
});

test('authoring text wrapping is cached across selection and zoom and invalidates after text editing', async ({ page }, info) => {
  await ready(page); await loadDesignerFixture(page); await selectObject(page, 'a'); await screenshot(page); await fresh(page);
  const before = await state(page); expect(before.TextLayoutCacheMisses).toBeGreaterThan(0);
  await selectObject(page, 'b'); await selectObject(page, 'a');
  await click(page, 'status-zoom-in'); await screenshot(page); await fresh(page);
  expect((await state(page)).TextLayoutCacheMisses).toBe(before.TextLayoutCacheMisses);
  expect((await state(page)).TextLayoutCacheHits).toBeGreaterThan(before.TextLayoutCacheHits);
  await typeProperty(page, 'text', 'Changed authoring cache text', 'Tab');
  await expect.poll(async () => (await state(page)).Objects.find(o => o.Id === 'a').Text).toBe('Changed authoring cache text');
  await screenshot(page); await expect.poll(async () => (await state(page)).TextLayoutCacheMisses).toBeGreaterThan(before.TextLayoutCacheMisses);
  expect((await state(page)).TextLayoutCacheCharacters).toBeLessThanOrEqual(131072);
  await click(page, 'quick-undo'); await expect.poll(async () => (await state(page)).Objects.find(o => o.Id === 'a').Text).toBe('Designer heading');
  expect((await state(page)).Diagnostics).toEqual([]);
  await artifact(info, 'authoring-text-cache.json', { before, after: await state(page) });
});
