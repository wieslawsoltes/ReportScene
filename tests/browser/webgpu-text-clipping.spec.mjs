import { test, expect } from '@playwright/test';
import { ready, state, fresh, bounds, click, choose, artifact, screenshot } from './uno-driver.mjs';
import { screenshotPixels } from './screenshot-pixels.mjs';
import { waitForScrollSettled } from './scroll-settled.mjs';

const itemId = 'clipped-large-text';
const text = Array.from('MABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%&*?').join('\n');

function fixture() {
  return { name: 'WebGPU clipped glyphs', pages: [{ name: 'Clipped text', width: 600, height: 400,
    margins: { left: 0, top: 0, right: 0, bottom: 0 },
    bands: [{ id: 'text-band', name: 'Clipped large glyphs', kind: 'ReportTitle', height: 140,
      items: [{ $type: 'text', id: itemId, name: itemId, text,
        bounds: { x: 0, y: 0, width: 170, height: 110 },
        style: { fontSize: 200, foreground: '#000000', fill: '#FFFFFF', padding: 0, lineHeight: 1, borderWidth: 0 }
      }]
    }]
  }] };
}

test('native WebGPU clips large offscreen glyphs before allocating the frame atlas', async ({ page }, info) => {
  await ready(page);
  await click(page, 'tab-report');
  const open = await bounds(page, 'open'), choosing = page.waitForEvent('filechooser');
  await page.mouse.click(open.X + open.Width / 2, open.Y + open.Height / 2);
  await (await choosing).setFiles({ name: 'webgpu-clipped-glyphs.report.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(fixture())) });
  await expect.poll(async () => (await state(page)).Document).toBe('WebGPU clipped glyphs');
  await click(page, 'tab-view'); await choose(page, 'design-zoom', 9);
  await expect.poll(async () => (await state(page)).Zoom).toBe(4);
  await click(page, 'tab-home'); await fresh(page);
  await waitForScrollSettled(page, 'DesignViewport');

  // Keep the short text box in the actual viewport. The many remaining 200pt
  // glyphs lie below its clip, even though they remain in the shaped document.
  for (let attempt = 0; attempt < 8; attempt++) {
    const snapshot = await state(page), r = snapshot.Objects.find(item => item.Id === itemId).Screen;
    const v = snapshot.Commands['design-viewport'];
    const dx = r.X - v.X - 24, dy = r.Y - v.Y - 24;
    if (Math.abs(dx) < 2 && Math.abs(dy) < 2) break;
    await page.mouse.move(v.X + v.Width / 2, v.Y + v.Height / 2);
    await page.mouse.wheel(dx, dy);
    await waitForScrollSettled(page, 'DesignViewport');
  }

  let evidence;
  await expect.poll(async () => {
    const snapshot = await state(page), r = snapshot.Objects.find(item => item.Id === itemId).Screen;
    const v = snapshot.Commands['design-viewport'];
    const pixels = screenshotPixels(await screenshot(page, info.outputPath('clipped-large-glyph.png')));
    const crop = { left: Math.ceil(Math.max(r.X + 8, v.X + 2)), top: Math.ceil(Math.max(r.Y + 8, v.Y + 2)),
      right: Math.floor(Math.min(r.X + r.Width - 8, v.X + v.Width - 2, pixels.width)),
      bottom: Math.floor(Math.min(r.Y + r.Height - 8, v.Y + v.Height - 2, pixels.height)) };
    let ink = 0, white = 0;
    for (let y = crop.top; y < crop.bottom; y += 2) for (let x = crop.left; x < crop.right; x += 2) {
      const [red, green, blue, alpha] = pixels.pixel(x, y);
      if (alpha === 255 && red < 64 && green < 64 && blue < 64) ink++;
      if (alpha === 255 && red > 250 && green > 250 && blue > 250) white++;
    }
    evidence = { crop, ink, white, gpu: snapshot.DesignGpu, viewport: snapshot.DesignViewport };
    // Atlas exhaustion used to paint a red error message in this box. Neither
    // that message nor the grey designer guide can satisfy the black-ink check.
    return snapshot.DesignGpu.State === 'WebGPU' && snapshot.DesignGpu.Error === null && ink > 100 && white > 500;
  }, { message: 'Visible large text renders while clipped glyphs consume no atlas space' }).toBe(true);

  const final = await state(page), gpu = final.DesignGpu;
  expect(gpu.Initialized).toBe(true);
  expect(gpu.SubmittedFrames).toBeGreaterThan(0);
  expect(gpu.Statistics.AtlasPages).toBeGreaterThan(0);
  expect(gpu.Statistics.AtlasPages).toBeLessThanOrEqual(3);
  expect(final.Diagnostics).toEqual([]);
  await artifact(info, 'webgpu-clipped-glyphs.json', { fontSize: 200, zoom: 4, distinctCharacters: new Set(text.replaceAll('\n', '')).size, ...evidence });
});
