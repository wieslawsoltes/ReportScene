import { test, expect } from '@playwright/test';
import { ready, state, fresh, bounds, click, fill, save, artifact, screenshot } from './uno-driver.mjs';

async function openPreview(page, count = 3) {
  await ready(page); await click(page, 'tab-report');
  const report = { name: 'Preview navigation contract', pages: Array.from({ length: count }, (_, n) => ({
    name: `Page${n + 1}`, width: 595.276, height: 841.89,
    bands: [{ name: `Title${n + 1}`, kind: 'ReportTitle', height: 60,
      items: [{ $type: 'text', id: `text-${n}`, name: `Caption${n + 1}`, text: `Page ${n + 1} needle-${n + 1}`,
        bounds: { x: 0, y: 0, width: 480, height: 40 }, style: { fontSize: 18 } }] }]
  })) };
  const r = await bounds(page, 'open'), waiting = page.waitForEvent('filechooser');
  await page.mouse.click(r.X + r.Width / 2, r.Y + r.Height / 2);
  await (await waiting).setFiles({ name: 'preview.report.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(report)) });
  await expect.poll(async () => (await state(page)).Document).toBe(report.name);
  await click(page, 'preview-report');
  await expect.poll(async () => (await state(page)).PreparedPages).toBe(count);
  await expect.poll(async () => (await state(page)).Inputs['preview-page']).toBe('1');
  return await state(page);
}
async function entry(page, text, key = 'Enter') {
  const r = await bounds(page, 'preview-page'); await page.mouse.click(r.X + 14, r.Y + r.Height / 2);
  await page.keyboard.press('Control+a'); await page.keyboard.insertText(text);
  await expect.poll(async () => (await state(page)).Inputs['preview-page']).toBe(text);
  if (key) await page.keyboard.press(key); await fresh(page);
}
async function expectPage(page, number) {
  await expect.poll(async () => (await state(page)).PreviewPage).toBe(number);
  await expect.poll(async () => (await state(page)).Inputs['preview-page']).toBe(String(number));
}
async function focusPaper(page) {
  const r = await bounds(page, 'preview-paper');
  await page.mouse.click(r.X + Math.min(80, r.Width / 2), r.Y + 90); await fresh(page);
}

test('preview first last and strict page entry synchronize native controls without stale navigation or edits', async ({ page }, info) => {
  const before = await openPreview(page);
  expect(before.Inputs['first-page-enabled']).toBe('False'); expect(before.Inputs['previous-page-enabled']).toBe('False');
  expect(before.Inputs['last-page-enabled']).toBe('True'); expect(before.Inputs['next-page-enabled']).toBe('True');
  expect(before.Inputs['preview-page-count']).toBe('of 3');
  await click(page, 'last-page'); await expectPage(page, 3);
  expect((await state(page)).Inputs['last-page-enabled']).toBe('False'); expect((await state(page)).Inputs['next-page-enabled']).toBe('False');
  const lastBuilds = (await state(page)).PreviewIndexBuilds;
  await click(page, 'next-page'); await expectPage(page, 3); expect((await state(page)).PreviewIndexBuilds).toBe(lastBuilds);
  await click(page, 'first-page'); await expectPage(page, 1);
  await entry(page, '2'); await expectPage(page, 2);
  const builds = (await state(page)).PreviewIndexBuilds;
  for (const invalid of ['0', '-1', '4', '2.5', '2147483648']) {
    await entry(page, invalid);
    await expect.poll(async () => (await state(page)).Inputs['preview-page-error'] || '').toContain('1 to 3');
    expect((await state(page)).PreviewPage).toBe(2); expect((await state(page)).PreviewIndexBuilds).toBe(builds);
    await page.keyboard.press('Escape'); await expectPage(page, 2);
    await expect.poll(async () => (await state(page)).Inputs['preview-page-error'] || '').toBe('');
  }
  await entry(page, ' 002 '); await expectPage(page, 2); expect((await state(page)).PreviewIndexBuilds).toBe(builds);
  await fill(page, 'preview-search', 'needle-3'); await click(page, 'find-next'); await expectPage(page, 3);
  expect((await state(page)).Inputs['next-page-enabled']).toBe('False'); expect((await state(page)).Status).toContain('Match 1 of 1');
  await click(page, 'previous-page'); await expectPage(page, 2); expect((await state(page)).Inputs['next-page-enabled']).toBe('True');
  expect((await state(page)).ContentRevision).toBe(before.ContentRevision); expect((await state(page)).HistoryCount).toBe(before.HistoryCount);
  expect((await state(page)).Dirty).toBe(false); expect((await state(page)).Diagnostics).toEqual([]);
  await screenshot(page, info.outputPath('preview-page-navigation.png')); await artifact(info, 'preview-navigation.json', await state(page));
});

test('preview keyboard page navigation remains synchronized after rebuilding the ribbon and at boundaries', async ({ page }, info) => {
  const before = await openPreview(page); await focusPaper(page);
  await page.keyboard.press('PageDown'); await expectPage(page, 2);
  await page.keyboard.press('Control+End'); await expectPage(page, 3);
  const last = (await state(page)).PreviewIndexBuilds;
  await page.keyboard.press('PageDown'); await fresh(page); expect((await state(page)).PreviewIndexBuilds).toBe(last);
  await page.keyboard.press('PageUp'); await expectPage(page, 2);
  await page.keyboard.press('Control+Home'); await expectPage(page, 1);
  for (let n = 0; n < 2; n++) {
    await click(page, 'back-to-design'); await click(page, 'preview-report'); await expectPage(page, 1);
    const rebuilt = (await state(page)).PreviewIndexBuilds;
    await focusPaper(page); await page.keyboard.press('PageDown'); await expectPage(page, 2);
    expect((await state(page)).PreviewIndexBuilds).toBe(rebuilt + 1);
  }
  // Text editing keeps ordinary Home/End semantics and cannot navigate the surface.
  await entry(page, '123', null); await page.keyboard.press('Control+Home'); await page.keyboard.press('Control+End');
  expect((await state(page)).PreviewPage).toBe(2);
  await page.keyboard.press('Escape'); await expectPage(page, 2);
  expect((await state(page)).ContentRevision).toBe(before.ContentRevision); expect((await state(page)).Dirty).toBe(false);
  expect((await state(page)).Diagnostics).toEqual([]); await artifact(info, 'preview-keyboard.json', await state(page));
});

test('whole-page fitting uses both viewport axes and preserves prepared content and the current page index', async ({ page }, info) => {
  const before = await openPreview(page, 1), original = await save(page);
  expect((await state(page)).Inputs['first-page-enabled']).toBe('False'); expect((await state(page)).Inputs['last-page-enabled']).toBe('False');
  const builds = (await state(page)).PreviewIndexBuilds;
  for (const [width, height] of [[1600, 1000], [1280, 800]]) {
    await page.setViewportSize({ width, height }); await fresh(page); await click(page, 'preview-fit-page');
    await expect.poll(async () => {
      const snapshot = await state(page), paper = snapshot.Commands['preview-paper'], viewport = snapshot.Commands['preview-viewport'];
      return Boolean(paper && viewport && paper.Width <= viewport.Width - 30 && paper.Height <= viewport.Height - 30
        && paper.X >= viewport.X && paper.Y >= viewport.Y && paper.X + paper.Width <= viewport.X + viewport.Width
        && paper.Y + paper.Height <= viewport.Y + viewport.Height);
    }, { message: 'The entire actual paper element fits inside the viewport with gutters' }).toBe(true);
    const r = await bounds(page, 'preview-paper');
    await expect.poll(async () => {
      const image = await screenshot(page, info.outputPath(`whole-page-${width}.png`));
      return page.evaluate(async ({ base64, r }) => {
        const image = new Image(); image.src = `data:image/png;base64,${base64}`; await image.decode();
        const canvas = document.createElement('canvas'); canvas.width = image.width; canvas.height = image.height;
        const context = canvas.getContext('2d'); context.drawImage(image, 0, 0);
        const inside = context.getImageData(Math.floor(r.X + 10), Math.floor(r.Y + r.Height - 10), 1, 1).data;
        return Math.min(...Array.from(inside).slice(0, 3));
      }, { base64: image.toString('base64'), r });
    }, { message: 'The fitted lower paper region is visibly white in the presented frame' }).toBeGreaterThan(245);
    expect((await state(page)).PreviewIndexBuilds).toBe(builds); expect((await state(page)).PreparedPages).toBe(1);
    expect((await state(page)).ContentRevision).toBe(before.ContentRevision);
  }
  const fitted = (await state(page)).Zoom;
  await click(page, 'preview-fit-width'); await expect.poll(async () => (await state(page)).Zoom).toBeGreaterThan(fitted);
  expect((await state(page)).PreviewIndexBuilds).toBe(builds);
  expect(await save(page)).toEqual(original); expect((await state(page)).Diagnostics).toEqual([]);
  await artifact(info, 'preview-fit-state.json', await state(page));
});
