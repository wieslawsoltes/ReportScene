import { test, expect } from '@playwright/test';
import { ready, state, fresh, bounds, click, fill, save, artifact } from './uno-driver.mjs';
import { loadDesignerFixture, selectObject } from './designer-fixture.mjs';

test('discarding invalid color drafts preserves mixed selections while explicit choices still apply', async ({ page }, info) => {
  await ready(page); await loadDesignerFixture(page); await selectObject(page, 'a');
  await click(page, 'fill-color'); await click(page, 'color-palette-10'); await click(page, 'color-dialog-apply');
  await expect.poll(async () => Boolean((await state(page)).Commands['color-dialog-apply'])).toBe(false);
  await selectObject(page, 'b', true);
  const original = await save(page), before = await state(page);
  for (const [field, invalid] of [['color-hex', '#bad'], ['color-red', '256']]) {
    await click(page, 'fill-color'); await bounds(page, 'color-dialog-apply');
    expect((await state(page)).Inputs['color-selection-summary']).toContain('Mixed colors');
    const initial = (await state(page)).Inputs['color-value'];
    await fill(page, field, invalid); await click(page, 'color-dialog-apply');
    await expect.poll(async () => (await state(page)).Inputs['color-dialog-error'] || '').not.toBe('');
    expect((await state(page)).Inputs[field]).toBe(invalid);
    const r = await bounds(page, field);
    await page.mouse.click(r.X + 15, r.Y + r.Height - 15); await page.keyboard.press('Escape'); await fresh(page);
    await bounds(page, 'color-dialog-apply');
    expect((await state(page)).Inputs['color-value']).toBe(initial);
    // Empty native labels have zero width and are omitted by Snapshot.Visible.
    // All restored value fields and the open Apply button are still required.
    expect((await state(page)).Inputs['color-hex']).toBe(initial);
    expect((await state(page)).Inputs['color-validation'] ?? '').toBe('');
    await click(page, 'color-dialog-apply');
    await expect.poll(async () => Boolean((await state(page)).Commands['color-dialog-apply'])).toBe(false);
    expect((await state(page)).ContentRevision).toBe(before.ContentRevision);
    expect((await state(page)).HistoryCount).toBe(before.HistoryCount);
    expect(await save(page)).toEqual(original);
  }
  // Choosing the already-previewed palette color is intentional and must still
  // unify the editable mixed selection in exactly one undo transaction.
  await click(page, 'fill-color'); await click(page, 'color-palette-10'); await click(page, 'color-dialog-apply');
  await expect.poll(async () => Boolean((await state(page)).Commands['color-dialog-apply'])).toBe(false);
  expect((await state(page)).HistoryCount).toBe(before.HistoryCount + 1);
  expect((await save(page)).pages[0].bands[0].items.map(i => i.style.fill)).toEqual(['#FF0000', '#FF0000']);
  await click(page, 'quick-undo'); expect(await save(page)).toEqual(original);
  expect((await state(page)).Diagnostics).toEqual([]);
  await artifact(info, 'invalid-color-draft-escape.json', await state(page));
});
