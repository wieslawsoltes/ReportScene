import { test, expect } from '@playwright/test';
import { state, ready, fresh, bounds, click, choose, fill, save, artifact, screenshot } from './uno-driver.mjs';

function fixture(bookmarks = true) {
  const item = (id, text, bookmark, x, y, width = 120, height = 48) => ({
    $type: 'text', id, name: id, text, bookmark: bookmarks ? bookmark : null,
    bounds: { x, y, width, height }, style: { fontSize: 12, fill: '#108840', foreground: '#FFFFFF' }
  });
  return { name: bookmarks ? 'Bookmark navigation contract' : 'Report without bookmarks', pages: [
    { name: 'First', width: 1100, height: 1200, bands: [{ id: 'first-band', name: 'FirstTitle', kind: 'ReportTitle', height: 980,
      items: [item('start', 'Start', 'Start', 20, 20), item('repeat1', 'First repeated', 'Repeated', 20, 120)] }] },
    { name: 'Second', width: 1100, height: 1200, bands: [{ id: 'second-band', name: 'SecondTitle', kind: 'ReportTitle', height: 980,
      items: [item('repeat2', 'Second repeated', 'Repeated', 20, 20), item('far', 'needle-right-bottom', 'Far target', 820, 860),
        { ...item('hidden', 'hidden', 'Invisible bookmark', 20, 200), visibleExpression: 'false' }] }] }
  ] };
}
async function openReport(page, report = fixture()) {
  await click(page, 'tab-report'); const r = await bounds(page, 'open'), waiting = page.waitForEvent('filechooser');
  await page.mouse.click(r.X + r.Width / 2, r.Y + r.Height / 2);
  await (await waiting).setFiles({ name: 'outline.report.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(report)) });
  await expect.poll(async () => (await state(page)).Document).toBe(report.name);
  await click(page, 'preview-report'); await expect.poll(async () => (await state(page)).PreparedPages).toBe(2);
  await fresh(page);
}
async function bookmarkControl(page, pageIndex, label) {
  // Keys contain prepared drawing-list ordinals, not design-object indices.
  // Locate the real native caption within its page, then retain that exact
  // occurrence key. Do not synthesize model targets or bypass pointer input.
  const prefix = `preview-outline-bookmark:${pageIndex}:`;
  const matches = async () => {
    const s = await state(page);
    return Object.keys(s.Commands).filter(key => key.startsWith(prefix) && s.Inputs[key] === label);
  };
  await expect.poll(async () => (await matches()).length, { message: `One realized ${label} bookmark on page ordinal ${pageIndex}` }).toBe(1);
  return (await matches())[0];
}
async function expectRevealed(page) {
  await expect.poll(async () => {
    const s = await state(page), paper = s.Commands['preview-paper'], viewport = s.Commands['preview-viewport'], target = s.PreviewHighlight;
    if (!paper || !viewport || !target || s.PreviewRevealPending) return false;
    const scale = s.Zoom * 96 / 72;
    const left = paper.X + target.X * scale, top = paper.Y + target.Y * scale;
    return left >= viewport.X && top >= viewport.Y && left + target.Width * scale <= viewport.X + viewport.Width
      && top + target.Height * scale <= viewport.Y + viewport.Height;
  }, { message: 'Actual highlighted target must be visible on both viewport axes' }).toBe(true);
}
async function visibleTargetPixels(page, info) {
  await expect.poll(async () => {
    const s = await state(page), paper = s.Commands['preview-paper'], target = s.PreviewHighlight, scale = s.Zoom * 96 / 72;
    const x = Math.floor(paper.X + (target.X + 4) * scale), y = Math.floor(paper.Y + (target.Y + target.Height - 4) * scale);
    const bytes = await screenshot(page, info.outputPath('visible-bookmark-target.png'));
    return page.evaluate(async ({ base64, x, y }) => {
      const image = new Image(); image.src = `data:image/png;base64,${base64}`; await image.decode();
      const canvas = document.createElement('canvas'); canvas.width = image.width; canvas.height = image.height;
      const context = canvas.getContext('2d'); context.drawImage(image, 0, 0);
      const [r, g, b] = context.getImageData(x, y, 1, 1).data;
      return r > 100 && r < 160 && g > 155 && g < 200 && b < 100;
    }, { base64: bytes.toString('base64'), x, y });
  }, { message: 'The revealed bookmark is visibly painted and highlighted' }).toBe(true);
}

test('preview outline preserves duplicate bookmark occurrences and reuses its metadata across close resize and navigation', async ({ page }, info) => {
  await ready(page); await openReport(page); const original = await save(page), before = await state(page);
  expect(before.PreviewOutlineBuilds).toBe(0); expect(before.PreviewOutlineVisible).toBe(false);
  await click(page, 'preview-outline');
  await expect.poll(async () => (await state(page)).PreviewBookmarkCount).toBe(4);
  await expect.poll(async () => (await state(page)).PreviewOutlineEffectiveVisible).toBe(true);
  expect((await state(page)).PreviewOutlineBuilds).toBe(1);
  await fill(page, 'preview-outline-filter', 'Repeated');
  await expect.poll(async () => (await state(page)).PreviewOutlineProjectedCount).toBe(4);
  const first = await bookmarkControl(page, 0, 'Repeated'), second = await bookmarkControl(page, 1, 'Repeated');
  expect(first).not.toBe(second);
  expect((await state(page)).Inputs[first]).toBe('Repeated'); expect((await state(page)).Inputs[second]).toBe('Repeated');
  await click(page, second);
  await expect.poll(async () => (await state(page)).PreviewPage).toBe(2);
  await expect.poll(async () => (await state(page)).PreviewSelectedBookmark).toBe(second.slice('preview-outline-'.length));
  await expectRevealed(page);
  await click(page, first); await expect.poll(async () => (await state(page)).PreviewPage).toBe(1);
  await expect.poll(async () => (await state(page)).PreviewSelectedBookmark).toBe(first.slice('preview-outline-'.length));
  await expectRevealed(page); const retained = await state(page);
  await click(page, 'preview-outline-close'); await expect.poll(async () => (await state(page)).PreviewOutlineVisible).toBe(false);
  await click(page, 'preview-outline'); await expect.poll(async () => (await state(page)).PreviewOutlineFilter).toBe('Repeated');
  expect((await state(page)).PreviewOutlineBuilds).toBe(retained.PreviewOutlineBuilds);
  expect((await state(page)).PreviewOutlineNodes).toBe(retained.PreviewOutlineNodes);
  expect((await state(page)).PreviewIndexBuilds).toBe(retained.PreviewIndexBuilds);
  const r = await bounds(page, 'preview-outline-splitter');
  await page.mouse.move(r.X + r.Width / 2, r.Y + 120); await page.mouse.down();
  await page.mouse.move(r.X + r.Width / 2 + 40, r.Y + 120, { steps: 5 }); await page.mouse.up();
  await expect.poll(async () => (await state(page)).PreviewOutlineWidth).toBeCloseTo(260, 0);
  expect((await state(page)).PreviewOutlineBuilds).toBe(1); expect((await state(page)).PreviewIndexBuilds).toBe(retained.PreviewIndexBuilds);
  expect((await state(page)).ContentRevision).toBe(before.ContentRevision); expect((await state(page)).HistoryCount).toBe(before.HistoryCount);
  expect(await save(page)).toEqual(original); expect((await state(page)).Diagnostics).toEqual([]);
  await screenshot(page, info.outputPath('preview-bookmark-outline.png')); await artifact(info, 'bookmark-outline-state.json', await state(page));
});

test('bookmark and search navigation reveal offscreen targets on both axes at high zoom without rebuilding the current index', async ({ page }, info) => {
  await ready(page); await openReport(page); const before = await state(page);
  await choose(page, 'preview-zoom', 9); await expect.poll(async () => (await state(page)).Zoom).toBe(4);
  await click(page, 'preview-outline'); await fill(page, 'preview-outline-filter', 'Far target');
  await click(page, await bookmarkControl(page, 1, 'Far target'));
  await expect.poll(async () => (await state(page)).PreviewPage).toBe(2); await expectRevealed(page); await visibleTargetPixels(page, info);
  const revealed = await state(page);
  expect(revealed.PreviewViewport.HorizontalOffset).toBeGreaterThan(0); expect(revealed.PreviewViewport.VerticalOffset).toBeGreaterThan(0);
  await click(page, 'first-page'); await expect.poll(async () => (await state(page)).PreviewPage).toBe(1);
  await fill(page, 'preview-search', 'needle-right-bottom'); await click(page, 'find-next');
  await expect.poll(async () => (await state(page)).PreviewPage).toBe(2); await expectRevealed(page); await visibleTargetPixels(page, info);
  const searched = await state(page); expect(searched.SearchMatches).toBe(1);
  await click(page, 'find-next'); await expectRevealed(page);
  expect((await state(page)).PreviewIndexBuilds).toBe(searched.PreviewIndexBuilds);
  expect((await state(page)).PreviewOutlineBuilds).toBe(1);
  expect((await state(page)).ContentRevision).toBe(before.ContentRevision); expect((await state(page)).Dirty).toBe(false);
  expect((await state(page)).Diagnostics).toEqual([]); await artifact(info, 'bookmark-and-search-reveal.json', { revealed, searched, final: await state(page) });
});

test('replacing a prepared report clears old bookmark targets and filters and displays the empty outline state', async ({ page }, info) => {
  await ready(page); await openReport(page); await click(page, 'preview-outline');
  await fill(page, 'preview-outline-filter', 'Far target'); await click(page, await bookmarkControl(page, 1, 'Far target')); await expectRevealed(page);
  const before = await state(page); await click(page, 'back-to-design'); await openReport(page, fixture(false));
  await expect.poll(async () => (await state(page)).PreviewBookmarkCount).toBe(0);
  await expect.poll(async () => (await state(page)).PreviewOutlineFilter).toBe('');
  expect((await state(page)).PreviewOutlineBuilds).toBe(before.PreviewOutlineBuilds + 1);
  expect((await state(page)).PreviewSelectedBookmark).toBeNull(); expect((await state(page)).PreviewHighlight).toBeNull();
  expect((await state(page)).PreviewRevealPending).toBe(false);
  await expect.poll(async () => (await state(page)).Inputs['preview-outline-summary']).toBe('No bookmarks in this report.');
  expect((await state(page)).PreviewOutlineProjectedCount).toBe(0);
  await click(page, 'preview-outline-close'); await click(page, 'preview-outline');
  expect((await state(page)).PreviewOutlineBuilds).toBe(before.PreviewOutlineBuilds + 1);
  expect((await state(page)).Diagnostics).toEqual([]); await screenshot(page, info.outputPath('empty-preview-outline.png')); await artifact(info, 'outline-replacement.json', await state(page));
});
