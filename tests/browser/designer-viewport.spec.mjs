import { test, expect } from '@playwright/test';
import { ready, state, fresh, bounds, click, choose, save, artifact, screenshot } from './uno-driver.mjs';
import { screenshotPixels } from './screenshot-pixels.mjs';

function fixture() {
  const text = (id, x, y, width, height, style) => ({ $type: 'text', id, name: id, text: '', bounds: { x, y, width, height }, style });
  return { name: 'Authoring viewport contract', pages: [{ name: 'LargePage', width: 2000, height: 2200,
    bands: [{ id: 'band', name: 'Title', kind: 'ReportTitle', height: 1800, items: [
      text('near', 20, 20, 90, 90, { fill: '#194BBA' }),
      text('far', 1200, 1400, 90, 90, { fill: '#108840' }),
      text('border', 550, 800, 100, 100, { fill: '#FFFFFF', borderColor: '#D02030', borderWidth: 80 }),
      ...Array.from({ length: 60 }, (_, n) => text(`offscreen${n}`, 280, 30 + n * 2, 20, 1, { fill: '#996633' }))
    ] }] }] };
}
async function load(page) {
  await ready(page); await click(page, 'tab-report');
  const r = await bounds(page, 'open'), waiting = page.waitForEvent('filechooser');
  await page.mouse.click(r.X + r.Width / 2, r.Y + r.Height / 2);
  await (await waiting).setFiles({ name: 'authoring.report.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(fixture())) });
  await expect.poll(async () => (await state(page)).Document).toBe('Authoring viewport contract');
  await click(page, 'tab-home');
  return await state(page);
}
async function selected(page, index, id) {
  await choose(page, 'object-selector', index);
  await expect.poll(async () => (await state(page)).Objects.find(o => o.Id === id)?.Selected).toBe(true);
  await fresh(page);
}
async function expectColor(page, info, file, sample, expected) {
  await expect.poll(async () => {
    const s = await state(page), point = sample(s);
    if (!point) return false;
    const bytes = await screenshot(page, info.outputPath(file));
    const pixels = screenshotPixels(bytes);
    if (point.x < 0 || point.y < 0 || point.x >= pixels.width || point.y >= pixels.height) return false;
    const actual = pixels.pixel(point.x, point.y);
    return expected.every((value, i) => Math.abs(actual[i] - value) <= 8);
  }, { message: `Native authoring pixels match ${file}, not only object metadata` }).toBe(true);
}

test('authoring viewport paints distant content at multiple scales and scrolls without rebuilding document layout', async ({ page }, info) => {
  const initial = await load(page), original = await save(page); const observations = [];
  for (const [index, zoom] of [[1, .5], [3, .85], [9, 4]]) {
    await click(page, 'tab-view'); await choose(page, 'design-zoom', index);
    await expect.poll(async () => (await state(page)).Zoom).toBe(zoom); await click(page, 'tab-home');
    await selected(page, 2, 'near'); await selected(page, 3, 'far');
    await expect.poll(async () => {
      const s = await state(page), r = s.Objects.find(o => o.Id === 'far').Screen, v = s.Commands['design-viewport'];
      return r.X >= v.X && r.Y >= v.Y && r.X + r.Width <= v.X + v.Width && r.Y + r.Height <= v.Y + v.Height;
    }).toBe(true);
    await expectColor(page, info, `far-authoring-${index}.png`, s => {
      const r = s.Objects.find(o => o.Id === 'far').Screen;
      return { x: r.X + r.Width / 2, y: r.Y + r.Height / 2 };
    }, [16, 136, 64]);
    await expect.poll(async () => (await state(page)).DesignDrawnObjects).toBeLessThan(initial.ObjectCount / 2);
    expect((await state(page)).DesignDrawnObjects).toBeGreaterThan(0);
    const beforeScroll = await state(page), v = beforeScroll.Commands['design-viewport'];
    await page.mouse.move(v.X + v.Width / 2, v.Y + v.Height / 2); await page.mouse.wheel(0, -90); await fresh(page);
    await page.mouse.wheel(0, 90); await fresh(page);
    await expectColor(page, info, `far-authoring-rescrolled-${index}.png`, s => {
      const r = s.Objects.find(o => o.Id === 'far').Screen;
      return { x: r.X + r.Width / 2, y: r.Y + r.Height / 2 };
    }, [16, 136, 64]);
    expect((await state(page)).DesignLayoutBuilds).toBe(beforeScroll.DesignLayoutBuilds);
    observations.push({ zoom, viewport: (await state(page)).DesignPaintViewport, drawn: (await state(page)).DesignDrawnObjects });
  }
  expect((await state(page)).ContentRevision).toBe(initial.ContentRevision); expect((await state(page)).HistoryCount).toBe(initial.HistoryCount);
  expect((await state(page)).Dirty).toBe(false); expect((await state(page)).Diagnostics).toEqual([]); expect(await save(page)).toEqual(original);
  await artifact(info, 'authoring-viewport-culling.json', observations);
});

test('authoring culling retains a thick border when its nominal rectangle is outside the native viewport', async ({ page }, info) => {
  const initial = await load(page);
  await click(page, 'tab-view'); await choose(page, 'design-zoom', 4); await click(page, 'tab-home');
  await selected(page, 4, 'border');
  // Scroll with actual wheel input until the object's right edge is 16 DIPs left
  // of the viewport. Its 80-point border still extends into the visible region.
  for (let step = 0; step < 12; step++) {
    const s = await state(page), r = s.Objects.find(o => o.Id === 'border').Screen, v = s.Commands['design-viewport'];
    const delta = r.X + r.Width - (v.X - 16);
    if (Math.abs(delta) < 2) break;
    await page.mouse.move(v.X + v.Width / 2, v.Y + v.Height / 2);
    await page.mouse.wheel(delta, 0); await fresh(page);
  }
  await expect.poll(async () => {
    const s = await state(page), r = s.Objects.find(o => o.Id === 'border').Screen, v = s.Commands['design-viewport'];
    return Math.abs(r.X + r.Width - (v.X - 16));
  }).toBeLessThan(2);
  const before = await state(page); expect(before.Objects.find(o => o.Id === 'border').Screen.X + before.Objects.find(o => o.Id === 'border').Screen.Width).toBeLessThan(before.Commands['design-viewport'].X);
  await expectColor(page, info, 'visible-offscreen-border.png', s => {
    const r = s.Objects.find(o => o.Id === 'border').Screen, v = s.Commands['design-viewport'];
    return { x: v.X + 12, y: r.Y + r.Height / 2 };
  }, [208, 32, 48]);
  expect((await state(page)).ContentRevision).toBe(initial.ContentRevision); expect((await state(page)).CanUndo).toBe(false);
  expect((await state(page)).Diagnostics).toEqual([]); await artifact(info, 'authoring-border-visibility.json', await state(page));
});
