import { test, expect } from '@playwright/test';
import { ready, state, fresh, bounds, click, fill, choose, save, artifact, screenshot } from './uno-driver.mjs';
import { loadDesignerFixture, selectObject } from './designer-fixture.mjs';

const key = 'reportscene.designer.preferences.v1';
const stored = page => page.evaluate(k => localStorage.getItem(k), key);
async function loaded(page) { await expect.poll(async () => (await state(page))?.PreferencesLoaded).toBe(true); }
async function settled(page) {
  await expect.poll(async () => (await state(page))?.PreferencesPending).toBe(false);
  expect((await state(page)).PreferencesError).toBe('');
}
async function openOptions(page) { await click(page, 'quick-options'); await bounds(page, 'options-dialog-apply'); }
async function dismiss(page, apply = false) {
  await click(page, apply ? 'options-dialog-apply' : 'options-dialog-cancel');
  await expect.poll(async () => Boolean((await state(page))?.Commands['options-dialog-apply'])).toBe(false);
  if (apply) await settled(page);
}
async function configureLargePanes(page) {
  await openOptions(page); await fill(page, 'options-sidebar-width', '560'); await fill(page, 'options-tree-height', '420'); await dismiss(page, true);
}

test('designer options draft cancel defaults and validation never change report content or saved preferences', async ({ page }, info) => {
  await ready(page); await loaded(page); await loadDesignerFixture(page); await selectObject(page, 'a');
  const reportBefore = await save(page); await settled(page); const before = await state(page); const originalStorage = await stored(page);
  await openOptions(page); await fill(page, 'options-sidebar-width', '500');
  await click(page, 'options-tab-grid'); await click(page, 'options-show-grid'); await fill(page, 'options-grid-size', '12');
  await screenshot(page, info.outputPath('options-grid-draft.png'));
  expect((await state(page)).GridSpacing).toBe(4); expect((await state(page)).GridVisible).toBe(true);
  await dismiss(page); expect(await stored(page)).toBe(originalStorage);
  expect((await state(page)).PreferencesGeneration).toBe(before.PreferencesGeneration);
  await openOptions(page); await click(page, 'options-dialog-defaults');
  await expect.poll(async () => (await state(page)).Inputs['options-error']).toContain('Defaults are in the draft');
  await dismiss(page); expect((await state(page)).PreferencesWriteCount).toBe(before.PreferencesWriteCount);
  await openOptions(page); await fill(page, 'options-sidebar-width', '0'); await click(page, 'options-dialog-apply');
  await expect.poll(async () => (await state(page)).Inputs['options-error'] ?? '').toContain('SidebarWidth');
  expect((await state(page)).PreferencesGeneration).toBe(before.PreferencesGeneration);
  await bounds(page, 'options-dialog-apply'); await screenshot(page, info.outputPath('options-validation.png')); await dismiss(page);
  expect(await save(page)).toEqual(reportBefore);
  expect((await state(page)).ContentRevision).toBe(before.ContentRevision); expect((await state(page)).HistoryCount).toBe(before.HistoryCount);
  expect((await state(page)).Diagnostics).toEqual([]); await artifact(info, 'options-draft-invariants.json', { before, after: await state(page) });
});

test('designer preferences apply atomically persist separately from reports and restore after browser reload', async ({ page }, info) => {
  await ready(page); await loaded(page); await loadDesignerFixture(page); const reportBefore = await save(page); const before = await state(page);
  await openOptions(page); await fill(page, 'options-sidebar-width', '560'); await fill(page, 'options-tree-height', '420');
  await choose(page, 'options-explorer-tab', 1);
  await click(page, 'options-tab-grid'); await click(page, 'options-show-grid'); await click(page, 'options-snap-grid');
  await fill(page, 'options-grid-size', '12'); await choose(page, 'options-units', 2);
  await click(page, 'options-tab-display'); await click(page, 'options-full-paper'); await click(page, 'options-alphabetical');
  await fill(page, 'options-design-zoom', '100'); await fill(page, 'options-preview-zoom', '75');
  await screenshot(page, info.outputPath('options-display.png')); await dismiss(page, true);
  let current = await state(page);
  expect(current.PreferredSidebarWidth).toBe(560); expect(current.PreferredTreeHeight).toBe(420);
  expect(current.DisplayUnit).toBe('Inches'); expect(current.GridSpacing).toBe(12); expect(current.SnapEnabled).toBe(false);
  expect(current.GridVisible).toBe(false); expect(current.FullPaper).toBe(true); expect(current.PropertiesAlphabetical).toBe(true);
  expect(current.DesignZoom).toBe(1); expect(current.PreviewZoom).toBe(.75); expect(current.ActiveTree).toBe('Report');
  expect(current.ContentRevision).toBe(before.ContentRevision); expect(current.HistoryCount).toBe(before.HistoryCount);
  expect(await save(page)).toEqual(reportBefore);
  const json = JSON.parse(await stored(page));
  expect(json).toMatchObject({ version: 1, sidebarWidth: 560, treeHeight: 420, unit: 'Inches', gridSize: 12, snapToGrid: false, designZoom: 1, previewZoom: .75, explorerTab: 'Report' });
  expect(Object.keys(json)).not.toContain('report'); expect(Object.keys(json)).not.toContain('filePath');
  await screenshot(page, info.outputPath('configured-workspace.png'));
  await ready(page); await loaded(page); current = await state(page);
  expect(current.PreferredSidebarWidth).toBe(560); expect(current.PreferredTreeHeight).toBe(420);
  expect(current.DisplayUnit).toBe('Inches'); expect(current.GridSpacing).toBe(12); expect(current.SnapEnabled).toBe(false);
  expect(current.GridVisible).toBe(false); expect(current.FullPaper).toBe(true); expect(current.PropertiesAlphabetical).toBe(true);
  expect(current.DesignZoom).toBe(1); expect(current.PreviewZoom).toBe(.75); expect(current.ActiveTree).toBe('Report');
  expect(current.PreferencesWriteCount).toBe(0); expect(current.PreferencesPending).toBe(false); expect(current.PreferencesError).toBe('');
  await screenshot(page, info.outputPath('restored-workspace.png')); await artifact(info, 'persisted-preferences.json', json);
});

test('viewport constraints and cancelled pane drags preserve preferred sizes without storage writes', async ({ page }, info) => {
  await page.setViewportSize({ width: 1600, height: 1000 }); await ready(page); await loaded(page); await loadDesignerFixture(page);
  await configureLargePanes(page); const before = await state(page); const storedBefore = await stored(page); const reportBefore = await save(page);
  await page.setViewportSize({ width: 700, height: 620 }); await fresh(page);
  await expect.poll(async () => (await state(page)).SidebarWidth).toBeLessThan(560);
  const small = await state(page);
  expect(small.SidebarWidth).toBeCloseTo(Math.min(640, small.WorkspaceWidth - 277), 4);
  expect(small.TreePaneHeight).toBeCloseTo(Math.min(420, 480, small.WorkspaceHeight - 204), 4);
  expect(small.PreferredSidebarWidth).toBe(560); expect(small.PreferredTreeHeight).toBe(420);
  const r = await bounds(page, 'sidebar-splitter');
  await page.mouse.move(r.X + r.Width / 2, r.Y + 80); await page.mouse.down(); await page.mouse.move(r.X + 30, r.Y + 80, { steps: 4 });
  await expect.poll(async () => (await state(page)).PaneDragActive).toBe(true);
  await page.keyboard.press('Escape'); await page.mouse.up(); await expect.poll(async () => (await state(page)).PaneDragActive).toBe(false);
  expect((await state(page)).PreferredSidebarWidth).toBe(560); expect((await state(page)).SidebarWidth).toBeCloseTo(small.SidebarWidth, 4);
  await page.setViewportSize({ width: 1600, height: 1000 }); await fresh(page);
  await expect.poll(async () => (await state(page)).SidebarWidth).toBe(560); expect((await state(page)).TreePaneHeight).toBe(420);
  await page.setViewportSize({ width: 440, height: 620 }); await fresh(page);
  await expect.poll(async () => (await state(page)).EffectivePropertiesShown).toBe(false); expect((await state(page)).EffectiveTreeShown).toBe(false);
  expect((await state(page)).TreeShown && (await state(page)).PropertiesShown).toBe(true);
  await screenshot(page, info.outputPath('narrow-workspace.png'));
  await page.setViewportSize({ width: 1000, height: 400 }); await fresh(page);
  await expect.poll(async () => (await state(page)).EffectiveTreeShown).toBe(false); expect((await state(page)).EffectivePropertiesShown).toBe(true);
  await page.setViewportSize({ width: 1600, height: 1000 }); await fresh(page);
  await expect.poll(async () => (await state(page)).SidebarWidth).toBe(560); expect((await state(page)).TreePaneHeight).toBe(420);
  expect((await state(page)).PreferencesGeneration).toBe(before.PreferencesGeneration);
  expect((await state(page)).PreferencesWriteCount).toBe(before.PreferencesWriteCount); expect(await stored(page)).toBe(storedBefore);
  expect(await save(page)).toEqual(reportBefore); expect((await state(page)).Diagnostics).toEqual([]);
  await screenshot(page, info.outputPath('preferred-layout-restored.png')); await artifact(info, 'viewport-preferences.json', { before, small, after: await state(page) });
});

test('invalid stored preferences keep the designer usable and explicit defaults repair storage', async ({ page }, info) => {
  const invalid = '{"version":1,"sidebarWidth":-5}';
  await page.addInitScript(({ key, invalid }) => {
    if (!sessionStorage.getItem('preferences-fixture-seeded')) { localStorage.setItem(key, invalid); sessionStorage.setItem('preferences-fixture-seeded', 'true'); }
  }, { key, invalid });
  await ready(page); await loaded(page);
  expect((await state(page)).PreferencesError).not.toBe(''); expect((await state(page)).PreferredSidebarWidth).toBe(280);
  expect((await state(page)).PreferencesWriteCount).toBe(0); expect(await stored(page)).toBe(invalid);
  const count = (await state(page)).ObjectCount; await click(page, 'insert-text');
  await expect.poll(async () => (await state(page)).ObjectCount).toBe(count + 1);
  const report = await save(page); await openOptions(page); await click(page, 'options-dialog-defaults');
  expect(await stored(page)).toBe(invalid); await dismiss(page, true);
  const repaired = JSON.parse(await stored(page)); expect(repaired.sidebarWidth).toBe(280); expect(repaired.version).toBe(1);
  expect(await save(page)).toEqual(report);
  await ready(page); await loaded(page); expect((await state(page)).PreferencesError).toBe(''); expect((await state(page)).PreferredSidebarWidth).toBe(280);
  await screenshot(page, info.outputPath('repaired-options.png')); await artifact(info, 'repaired-preferences.json', repaired);
});

test('preference storage failure stays pending without blocking report edits and retries explicitly', async ({ page }, info) => {
  await page.addInitScript(key => {
    const original = Storage.prototype.setItem;
    window.preferenceStoreFails = true;
    Storage.prototype.setItem = function(name, value) {
      if (this === localStorage && name === key && window.preferenceStoreFails) throw new DOMException('Test preference quota failure', 'QuotaExceededError');
      return original.call(this, name, value);
    };
  }, key);
  await ready(page); await loaded(page); await loadDesignerFixture(page); const report = await save(page);
  await openOptions(page); await fill(page, 'options-sidebar-width', '500'); await click(page, 'options-dialog-apply');
  await expect.poll(async () => Boolean((await state(page)).Commands['options-dialog-apply'])).toBe(false);
  await expect.poll(async () => (await state(page)).PreferencesError).toContain('Test preference quota failure');
  expect((await state(page)).PreferencesPending).toBe(true); expect((await state(page)).PreferredSidebarWidth).toBe(500);
  expect(await save(page)).toEqual(report); const count = (await state(page)).ObjectCount;
  await click(page, 'insert-text'); await expect.poll(async () => (await state(page)).ObjectCount).toBe(count + 1);
  // Only the fault-injected host storage is released. No application command or report state is changed through diagnostics.
  await page.evaluate(() => { window.preferenceStoreFails = false; });
  await openOptions(page); await dismiss(page, true);
  expect(JSON.parse(await stored(page)).sidebarWidth).toBe(500); expect((await state(page)).PreferencesPending).toBe(false);
  expect((await state(page)).PreferencesWriteCount).toBe(1); expect((await state(page)).Diagnostics).toEqual([]);
  await screenshot(page, info.outputPath('storage-retry.png')); await artifact(info, 'preference-storage-retry.json', await state(page));
});

test('direct view snap sorting and pane commands persist through notifications without report edits', async ({ page }, info) => {
  await ready(page); await loaded(page); await loadDesignerFixture(page); const report = await save(page); const before = await state(page);
  await click(page, 'tab-view'); await click(page, 'snap-grid'); await settled(page);
  expect(JSON.parse(await stored(page)).snapToGrid).toBe(false);
  await click(page, 'properties-alphabetical'); await settled(page); await click(page, 'close-tree'); await settled(page);
  const values = JSON.parse(await stored(page)); expect(values.alphabeticalProperties).toBe(true); expect(values.showTree).toBe(false);
  expect((await state(page)).ContentRevision).toBe(before.ContentRevision); expect((await state(page)).HistoryCount).toBe(before.HistoryCount);
  expect(await save(page)).toEqual(report);
  await ready(page); await loaded(page); expect((await state(page)).SnapEnabled).toBe(false);
  expect((await state(page)).PropertiesAlphabetical).toBe(true); expect((await state(page)).TreeShown).toBe(false);
  expect((await state(page)).EffectiveTreeShown).toBe(false); expect((await state(page)).PreferencesWriteCount).toBe(0);
  await screenshot(page, info.outputPath('direct-preferences-restored.png')); await artifact(info, 'direct-view-settings.json', values);
});
