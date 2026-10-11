import { test, expect } from '@playwright/test';
import { state, ready, fresh, bounds, click, fill, choose, save, artifact, screenshot } from './uno-driver.mjs';

const fixture = () => ({
  name: 'Search lifecycle contract',
  pages: ['cat', 'CAT', 'cats'].map((text, n) => ({
    name: `Page${n + 1}`, bands: [{ name: `Title${n + 1}`, kind: 'ReportTitle', height: 60,
      items: [{ $type: 'text', id: `text-${n}`, name: `Text${n}`, text,
        bounds: { x: 0, y: 0, width: 400, height: 40 }, style: { fontSize: 18 } }] }]
  }))
});
async function loadFixture(page) {
  await click(page, 'tab-report'); const r = await bounds(page, 'open');
  const waiting = page.waitForEvent('filechooser'); await page.mouse.click(r.X + r.Width / 2, r.Y + r.Height / 2);
  await (await waiting).setFiles({ name: 'search.report.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(fixture())) });
  await expect.poll(async () => (await state(page)).Document).toBe('Search lifecycle contract');
  await click(page, 'tab-home');
}
async function query(page, text) { await fill(page, 'preview-search', text); await click(page, 'find-next'); }
async function property(page, key, value) {
  await expect.poll(async () => Boolean((await state(page)).Properties[key])).toBe(true);
  const r = (await state(page)).Properties[key]; await page.mouse.click(r.X + 25, r.Y + r.Height / 2);
  await fresh(page); await page.keyboard.press('Control+a'); await page.keyboard.insertText(value); await page.keyboard.press('Tab'); await fresh(page);
}

test('advanced find uses real native controls and cached bidirectional search', async ({ page }, info) => {
  await ready(page); await loadFixture(page); await click(page, 'preview');
  await expect.poll(async () => (await state(page)).PreparedPages).toBe(3);
  await query(page, 'cat'); await expect.poll(async () => (await state(page)).Status).toContain('Match 1 of 3');
  const scans = (await state(page)).SearchQueryScans;
  await click(page, 'find-previous'); await expect.poll(async () => (await state(page)).Status).toContain('Match 3 of 3');
  expect((await state(page)).PreviewPage).toBe(3); expect((await state(page)).Inputs['preview-page']).toBe('3');
  await click(page, 'find-next'); await expect.poll(async () => (await state(page)).PreviewPage).toBe(1);
  expect((await state(page)).SearchQueryScans).toBe(scans);
  await click(page, 'find-whole-word'); await expect.poll(async () => (await state(page)).SearchMatches).toBe(2);
  await click(page, 'find-match-case'); await expect.poll(async () => (await state(page)).SearchMatches).toBe(1);
  await click(page, 'find-match-case'); await expect.poll(async () => (await state(page)).SearchMatches).toBe(2);
  const r = await bounds(page, 'preview-search'); await page.mouse.click(r.X + 30, r.Y + r.Height / 2);
  await page.keyboard.press('Shift+Enter'); await expect.poll(async () => (await state(page)).Status).toContain('Match 2 of 2');
  expect((await state(page)).Diagnostics).toEqual([]);
  await screenshot(page, info.outputPath('advanced-search.png')); await artifact(info, 'advanced-search-state.json', await state(page));
});

test('saving and zooming preserve the prepared snapshot, edits invalidate stale same-term matches', async ({ page }, info) => {
  await ready(page); await loadFixture(page); await click(page, 'preview'); await query(page, 'cat');
  await expect.poll(async () => (await state(page)).SearchMatches).toBe(3);
  const before = await state(page); await save(page); await fresh(page);
  expect((await state(page)).Preview).toBe(true); expect((await state(page)).PreparedPages).toBe(3);
  expect((await state(page)).ContentRevision).toBe(before.ContentRevision);
  expect((await state(page)).PreviewIndexBuilds).toBe(before.PreviewIndexBuilds);
  await choose(page, 'preview-zoom', 5); await expect.poll(async () => (await state(page)).Zoom).toBe(1.25);
  expect((await state(page)).PreviewIndexBuilds).toBe(before.PreviewIndexBuilds);
  await click(page, 'find-next'); await expect.poll(async () => (await state(page)).Status).toContain('Match 2 of 3');
  expect((await state(page)).SearchQueryScans).toBe(before.SearchQueryScans);
  await click(page, 'back-to-design'); const r = (await state(page)).Objects.find(o => o.Id === 'text-0').Screen;
  await page.mouse.click(r.X + r.Width / 2, r.Y + r.Height / 2);
  await expect.poll(async () => (await state(page)).Objects.find(o => o.Id === 'text-0')?.Selected).toBe(true);
  await property(page, 'text', 'dog'); await expect.poll(async () => (await state(page)).Objects.find(o => o.Id === 'text-0')?.Text).toBe('dog');
  await expect.poll(async () => (await state(page)).PreparedPages).toBe(0);
  await click(page, 'preview'); await click(page, 'find-next');
  await expect.poll(async () => (await state(page)).Status).toContain('Match 1 of 2');
  expect((await state(page)).PreviewPage).toBe(2); expect((await state(page)).Diagnostics).toEqual([]);
  await expect.poll(async () => (await state(page)).RecoveryPending).toBe(false);
  await screenshot(page, info.outputPath('refreshed-search.png')); await artifact(info, 'search-lifecycle-state.json', await state(page));
});
