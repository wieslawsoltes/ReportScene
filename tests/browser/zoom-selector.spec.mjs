import { test, expect } from '@playwright/test';
import { ready, state, fresh, bounds, click, choose, save, artifact, screenshot } from './uno-driver.mjs';
import { loadDesignerFixture } from './designer-fixture.mjs';

async function matches(page, id) {
  await expect.poll(async () => {
    const s = await state(page), text = s.Inputs[id + '-value'];
    return typeof text === 'string' && /%$/.test(text) && Math.abs(parseFloat(text) - s.Zoom * 100) <= .0051;
  }, { message: 'The native zoom selector displays the actual scale without changing it' }).toBe(true);
}

test('authoring zoom selector follows fitting and status commands without rounding the actual scale', async ({ page }, info) => {
  await ready(page); await loadDesignerFixture(page); const original = await save(page), before = await state(page);
  await click(page, 'tab-view'); await matches(page, 'design-zoom');
  await choose(page, 'design-zoom', 3); expect((await state(page)).Zoom).toBe(.85);
  await click(page, 'status-zoom-in'); await matches(page, 'design-zoom');
  expect((await state(page)).Inputs['design-zoom-value']).toBe('95%');
  await click(page, 'fit-width'); await matches(page, 'design-zoom');
  const fitted = (await state(page)).Zoom;
  expect([.25, .5, .75, .85, 1, 1.25, 1.5, 2, 3, 4]).not.toContain(fitted);
  await choose(page, 'design-zoom', 10); expect((await state(page)).Zoom).toBe(fitted);
  for (let n = 0; n < 4; n++) { await click(page, 'status-zoom-out'); await matches(page, 'design-zoom'); }
  await choose(page, 'design-zoom', 9); expect((await state(page)).Inputs['design-zoom-value']).toBe('400%');
  await choose(page, 'design-zoom', 1); expect((await state(page)).Inputs['design-zoom-value']).toBe('50%');
  const after = await state(page);
  expect(after.DesignLayoutBuilds).toBe(before.DesignLayoutBuilds); expect(after.ContentRevision).toBe(before.ContentRevision);
  expect(after.HistoryCount).toBe(before.HistoryCount); expect(after.Diagnostics).toEqual([]); expect(await save(page)).toEqual(original);
  await screenshot(page, info.outputPath('authoring-zoom-selector.png')); await artifact(info, 'authoring-zoom-state.json', after);
});

test('preview zoom selector keeps fitted fractions and independent authoring zoom across mode changes', async ({ page }, info) => {
  await ready(page); await loadDesignerFixture(page); const original = await save(page);
  await click(page, 'tab-view'); await choose(page, 'design-zoom', 3);
  const authoring = (await state(page)).Zoom;
  await click(page, 'tab-report'); await click(page, 'preview-report'); const before = await state(page);
  await matches(page, 'preview-zoom');
  for (const [width, height] of [[1600, 1000], [1280, 800]]) {
    await page.setViewportSize({ width, height }); await fresh(page); await click(page, 'preview-fit-page');
    await matches(page, 'preview-zoom'); const scale = (await state(page)).Zoom;
    await choose(page, 'preview-zoom', 10); expect((await state(page)).Zoom).toBe(scale);
  }
  await click(page, 'status-zoom-in'); await matches(page, 'preview-zoom');
  await click(page, 'preview-fit-width'); await matches(page, 'preview-zoom');
  await choose(page, 'preview-zoom', 2); expect((await state(page)).Inputs['preview-zoom-value']).toBe('75%');
  expect((await state(page)).PreviewIndexBuilds).toBe(before.PreviewIndexBuilds);
  expect((await state(page)).ContentRevision).toBe(before.ContentRevision);
  await screenshot(page, info.outputPath('preview-zoom-selector.png'));
  await click(page, 'tab-view'); await matches(page, 'design-zoom'); expect((await state(page)).Zoom).toBe(authoring);
  expect(await save(page)).toEqual(original); expect((await state(page)).Diagnostics).toEqual([]);
  await artifact(info, 'independent-zoom-state.json', await state(page));
});
