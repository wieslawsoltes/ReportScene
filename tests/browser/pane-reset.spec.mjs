import { test, expect } from '@playwright/test';
import { ready, state, bounds, fresh, save, artifact, screenshot } from './uno-driver.mjs';
import { loadDesignerFixture } from './designer-fixture.mjs';

test('pane double-click resets on release while rapid resize Escape preserves committed sizes and storage', async ({ page }, info) => {
  await page.setViewportSize({ width: 1280, height: 860 });
  await ready(page); await loadDesignerFixture(page); const original = await save(page), initial = await state(page);
  const evidence = [];
  for (const [id, key, arrow, normal, dx, dy] of [
    ['sidebar-splitter', 'SidebarWidth', 'ArrowLeft', 280, -32, 0],
    ['tree-splitter', 'TreePaneHeight', 'ArrowDown', 210, 0, 32]
  ]) {
    let r = await bounds(page, id);
    const point = r => id === 'sidebar-splitter' ? [r.X + r.Width / 2, r.Y + 80] : [r.X + 70, r.Y + r.Height / 2];
    let [x, y] = point(r); await page.mouse.click(x, y); await page.keyboard.press(arrow);
    await expect.poll(async () => (await state(page))[key]).toBe(normal + 8);
    r = await bounds(page, id); [x, y] = point(r);
    await page.mouse.dblclick(x, y);
    await expect.poll(async () => (await state(page))[key], { message: 'A genuine native double click still resets layout' }).toBe(normal);
    r = await bounds(page, id); [x, y] = point(r);
    await page.mouse.click(x, y); await page.keyboard.press(arrow);
    await expect.poll(async () => (await state(page))[key]).toBe(normal + 8);
    await expect.poll(async () => (await state(page)).PreferencesPending).toBe(false);
    const before = await state(page); r = await bounds(page, id); [x, y] = point(r);
    // Real adjacent native taps: keep the second button held, then move and cancel.
    // No delay, synthetic application event, diagnostic mutation or retry hides reset.
    await page.mouse.click(x, y); await page.mouse.move(x, y); await page.mouse.down();
    await page.mouse.move(x + dx, y + dy, { steps: 3 });
    await page.keyboard.press('Escape'); await page.mouse.up(); await fresh(page);
    await expect.poll(async () => (await state(page))[key]).toBe(before[key]);
    await expect.poll(async () => (await state(page)).PreferencesPending).toBe(false);
    const after = await state(page);
    expect(after.PreferencesGeneration).toBe(before.PreferencesGeneration);
    expect(after.PreferencesWriteCount).toBe(before.PreferencesWriteCount);
    expect(after.ContentRevision).toBe(initial.ContentRevision); expect(after.HistoryCount).toBe(initial.HistoryCount);
    evidence.push({ id, before, after });
  }
  expect(await save(page)).toEqual(original); expect((await state(page)).Diagnostics).toEqual([]);
  await screenshot(page, info.outputPath('pane-reset-cancellation.png'));
  await artifact(info, 'pane-reset-cancellation.json', evidence);
});
