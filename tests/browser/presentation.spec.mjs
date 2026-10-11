import { test, expect } from '@playwright/test';
import { state, ready, fresh, click, choose, screenshot, artifact } from './uno-driver.mjs';

test('cold application dismisses the splash and presents both matrix tiles', async ({ page }, info) => {
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await ready(page); await screenshot(page, info.outputPath('startup-presented.png'));
  await click(page, 'tab-view'); await choose(page, 'samples', 5);
  await expect.poll(async () => (await state(page)).Document).toBe('Matrix sales');
  await fresh(page); await screenshot(page, info.outputPath('matrix-design-presented.png'));
  await click(page, 'tab-home'); await click(page, 'preview');
  await expect.poll(async () => (await state(page)).Preview).toBe(true);
  await fresh(page); await screenshot(page, info.outputPath('matrix-page1-presented.png'));
  await click(page, 'next-page'); await expect.poll(async () => (await state(page)).PreviewPage).toBe(2);
  await fresh(page); await screenshot(page, info.outputPath('matrix-page2-presented.png'));
  expect((await state(page)).Diagnostics).toEqual([]); expect(errors).toEqual([]);
  await artifact(info, 'presentation-state.json', await state(page));
});

test('ordinary non-diagnostic startup also completes the presentation handoff', async ({ page }, info) => {
  await page.goto('./');
  await page.waitForFunction(() => window.reportScene?.presented || window.reportScene?.presentationError || window.reportSceneFatal, { timeout: 180_000 });
  expect(await page.evaluate(() => window.reportSceneFatal || window.reportScene.presentationError)).toBeFalsy();
  expect(await page.evaluate(() => window.reportScene.state)).toBeNull();
  await screenshot(page, info.outputPath('ordinary-startup-presented.png'));
});
