import { test, expect } from '@playwright/test';
import { PNG } from 'pngjs';
import { ready, state, fresh, bounds, click, choose, artifact, screenshot } from './uno-driver.mjs';
import { screenshotPixels } from './screenshot-pixels.mjs';
import { waitForScrollSettled } from './scroll-settled.mjs';

const sourceWidth = 9001, sourceHeight = 257;
const expected = [143, 191, 239, 255]; // RGBA(32,128,224,128), premultiplied over white.
const colors = [[32, 128, 224], [224, 64, 96], [64, 208, 96]];
const overWhite = color => [...color.map(value => Math.round(value * 128 / 255) + 127), 255];

function fixture() {
  const png = new PNG({ width: sourceWidth, height: sourceHeight });
  for (let offset = 0; offset < png.data.length; offset += 4) {
    const x = offset / 4 % sourceWidth, y = Math.floor(offset / 4 / sourceWidth);
    const color = y < 128 ? colors[0] : colors[Math.min(2, Math.floor(x * 3 / sourceWidth))];
    png.data[offset] = color[0]; png.data[offset + 1] = color[1];
    png.data[offset + 2] = color[2]; png.data[offset + 3] = 128;
  }
  const data = PNG.sync.write(png).toString('base64');
  const image = (id, y, width, height) => ({
    $type: 'image', id, name: id, data, preserveAspect: false,
    bounds: { x: 16, y, width, height }, style: { fill: '#FFFFFF', borderWidth: 0 }
  });
  return { name: 'WebGPU tiled images', pages: [{ name: 'Wide image', width: 7000, height: 450,
    bands: [{ id: 'image-band', name: 'Images', kind: 'ReportTitle', height: 340,
      items: [image('minified', 16, 500, 60), image('native-size', 110, sourceWidth * .75, sourceHeight * .75)]
    }]
  }] };
}

async function expectImagePixels(page, info, file, points) {
  await expect.poll(async () => {
    const current = await state(page), samples = points(current);
    const pixels = screenshotPixels(await screenshot(page, info.outputPath(file)));
    return samples.length > 0 && samples.every(([x, y, color = expected]) => x >= 0 && y >= 0 && x < pixels.width && y < pixels.height
      && pixels.pixel(x, y).every((value, channel) => Math.abs(value - color[channel]) <= 2));
  }, { message: 'Actual WebGPU image pixels preserve premultiplied alpha across tile boundaries' }).toBe(true);
}

test('native WebGPU tiles oversized odd images without seams and bounds visible image residency', async ({ page }, info) => {
  await ready(page);
  await click(page, 'tab-report');
  const open = await bounds(page, 'open'), choosing = page.waitForEvent('filechooser');
  await page.mouse.click(open.X + open.Width / 2, open.Y + open.Height / 2);
  await (await choosing).setFiles({ name: 'webgpu-images.report.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(fixture())) });
  await expect.poll(async () => (await state(page)).Document).toBe('WebGPU tiled images');
  await click(page, 'tab-view'); await choose(page, 'design-zoom', 4);
  await expect.poll(async () => (await state(page)).Zoom).toBe(1);
  await click(page, 'tab-home'); await fresh(page);

  await expectImagePixels(page, info, 'minified-odd-image.png', snapshot => {
    const r = snapshot.Objects.find(item => item.Id === 'minified').Screen;
    return [.15, .5, .85].flatMap((fraction, index) => [
      [r.X + r.Width * fraction, r.Y + r.Height * .25],
      [r.X + r.Width * fraction, r.Y + r.Height * .75, overWhite(colors[index])]
    ]);
  });
  const initial = await state(page);
  const tileSize = initial.DesignGpu.Statistics.ImageTileSize;
  expect(tileSize).toBeGreaterThan(0);
  expect(tileSize).toBeLessThanOrEqual(2048);
  expect(initial.DesignGpu.Statistics.MaximumTextureDimension).toBeGreaterThanOrEqual(tileSize + 8);

  // Move the first and second source-tile boundaries into the actual viewport
  // with native wheel input. Each row must remain uniform even at the shared
  // fractional edge: a gap or double alpha blend changes these pixels.
  const observations = [];
  for (const boundary of [tileSize, tileSize * 2]) {
    await waitForScrollSettled(page, 'DesignViewport');
    for (let attempt = 0; attempt < 12; attempt++) {
      const snapshot = await state(page), r = snapshot.Objects.find(item => item.Id === 'native-size').Screen;
      const v = snapshot.Commands['design-viewport'];
      const delta = r.X + r.Width * boundary / sourceWidth - (v.X + v.Width / 2);
      if (Math.abs(delta) < 2) break;
      await page.mouse.move(v.X + v.Width / 2, v.Y + v.Height / 2);
      await page.mouse.wheel(delta, 0); await waitForScrollSettled(page, 'DesignViewport');
    }
    await expect.poll(async () => {
      const snapshot = await state(page), r = snapshot.Objects.find(item => item.Id === 'native-size').Screen;
      const v = snapshot.Commands['design-viewport'];
      return Math.abs(r.X + r.Width * boundary / sourceWidth - (v.X + v.Width / 2));
    }).toBeLessThan(2);
    await expectImagePixels(page, info, `tile-boundary-${boundary}.png`, snapshot => {
      const r = snapshot.Objects.find(item => item.Id === 'native-size').Screen;
      const x = r.X + r.Width * boundary / sourceWidth;
      const color = overWhite(colors[Math.min(2, Math.floor(boundary * 3 / sourceWidth))]);
      return Array.from({ length: 11 }, (_, index) => [x + index - 5, r.Y + r.Height * .25])
        .concat([[x, r.Y + r.Height * .75, color]]);
    });
    const snapshot = await state(page), gpu = snapshot.DesignGpu;
    expect(gpu.Error).toBeNull();
    expect(gpu.Statistics.CachedImageBytes).toBeGreaterThan(0);
    expect(gpu.Statistics.CachedImageBytes).toBeLessThan(32 * 1024 * 1024);
    expect(gpu.Statistics.ImageStagingBytes).toBeLessThanOrEqual(sourceWidth * sourceHeight * 4);
    expect(gpu.PixelWidth).toBeLessThanOrEqual(Math.ceil(snapshot.Commands['design-viewport'].Width + 5));
    observations.push({ boundary, gpu });
  }
  const final = await state(page);
  expect(final.ContentRevision).toBe(initial.ContentRevision);
  expect(final.HistoryCount).toBe(initial.HistoryCount);
  expect(final.DesignLayoutBuilds).toBe(initial.DesignLayoutBuilds);
  expect(final.Diagnostics).toEqual([]);
  await artifact(info, 'webgpu-tiled-images.json', { sourceWidth, sourceHeight, initial: initial.DesignGpu, observations });
});
