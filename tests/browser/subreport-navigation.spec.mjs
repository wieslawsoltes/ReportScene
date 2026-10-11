import { test, expect } from '@playwright/test';
import { state, ready, click, save, artifact } from './uno-driver.mjs';

test('undoing subreport creation from its target page restores an editable owner band', async ({ page }, info) => {
  await ready(page); const original = await state(page);
  await click(page, 'insert-subreport');
  await expect.poll(async () => (await state(page)).TemplatePages).toBe(original.TemplatePages + 1);
  const added = (await state(page)).Objects.find(o => o.Kind === 'SubreportItem' && o.Selected);
  expect(added).toBeTruthy();
  await click(page, 'subreport-open');
  await expect.poll(async () => (await state(page)).Objects.some(o => o.Text === 'Subreport content')).toBe(true);
  await click(page, 'quick-undo');
  await expect.poll(async () => (await state(page)).TemplatePages).toBe(original.TemplatePages);
  await expect.poll(async () => (await state(page)).Objects.some(o => o.Name === 'Company')).toBe(true);
  await click(page, 'quick-redo');
  await expect.poll(async () => (await state(page)).Objects.some(o => o.Id === added.Id && o.Selected)).toBe(true);
  await click(page, 'subreport-open'); await click(page, 'quick-undo');
  await expect.poll(async () => (await state(page)).TemplatePages).toBe(original.TemplatePages);
  await click(page, 'insert-text');
  await expect.poll(async () => (await state(page)).ObjectCount).toBe(original.ObjectCount + 1);
  expect((await state(page)).Diagnostics).toEqual([]);
  const document = await save(page); expect(document.pages.length).toBe(original.TemplatePages);
  await artifact(info, 'subreport-navigation-undo.report.json', document);
});
