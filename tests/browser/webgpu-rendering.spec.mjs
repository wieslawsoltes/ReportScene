import { test, expect } from '@playwright/test';
import { ready, state, fresh, bounds, click, choose, artifact, screenshot } from './uno-driver.mjs';

function assertNativeGpu(snapshot, key, viewportKey, ratio) {
  const gpu = snapshot[key], viewport = snapshot.Commands[viewportKey];
  expect(gpu.State).toBe('WebGPU');
  expect(gpu.Initialized).toBe(true);
  expect(gpu.Error).toBeNull();
  expect(gpu.SubmittedFrames).toBeGreaterThan(0);
  expect(gpu.Statistics.PrimitiveCount).toBeGreaterThan(0);
  expect(gpu.Statistics.DrawCalls).toBeGreaterThan(0);
  expect(gpu.PixelWidth).toBeGreaterThan(0);
  expect(gpu.PixelHeight).toBeGreaterThan(0);
  // The two-DIP culling guard on each side may enlarge the target by four DIPs.
  // Its memory footprint must follow the viewport, never the zoomed page extent.
  expect(gpu.PixelWidth).toBeLessThanOrEqual(Math.ceil((viewport.Width + 5) * ratio));
  expect(gpu.PixelHeight).toBeLessThanOrEqual(Math.ceil((viewport.Height + 5) * ratio));
  expect(gpu.PixelWidth).toBeLessThanOrEqual(Math.ceil(gpu.Viewport.Width * ratio) + 1);
  expect(gpu.PixelHeight).toBeLessThanOrEqual(Math.ceil(gpu.Viewport.Height * ratio) + 1);
}

async function waitForGpu(page, key) {
  await expect.poll(async () => {
    const gpu = (await state(page))?.[key];
    return gpu?.State === 'WebGPU' && gpu.Initialized === true && gpu.SubmittedFrames > 0;
  }, { message: 'The native Uno WebGPU canvas has submitted a successful frame' }).toBe(true);
}

test('native WebGPU surfaces use viewport targets, preserve scroll and stop submitting when idle', async ({ page }, info) => {
  await ready(page);
  const ratio = await page.evaluate(() => devicePixelRatio);
  await waitForGpu(page, 'DesignGpu');
  const initial = await state(page);
  assertNativeGpu(initial, 'DesignGpu', 'design-viewport', ratio);
  await click(page, 'tab-view');
  await choose(page, 'design-zoom', 9);
  await expect.poll(async () => (await state(page)).Zoom).toBe(4);
  await expect.poll(async () => (await state(page)).DesignGpu.SubmittedFrames).toBeGreaterThan(initial.DesignGpu.SubmittedFrames);
  const zoomed = await state(page);
  assertNativeGpu(zoomed, 'DesignGpu', 'design-viewport', ratio);
  expect(zoomed.Commands['design-paper'].Width).toBeGreaterThan(zoomed.DesignGpu.Viewport.Width * 2);
  const viewport = await bounds(page, 'design-viewport');
  await page.mouse.move(viewport.X + viewport.Width / 2, viewport.Y + viewport.Height / 2);
  await page.mouse.wheel(180, 240);
  await expect.poll(async () => (await state(page)).DesignGpu.Viewport.Y).toBeGreaterThan(zoomed.DesignGpu.Viewport.Y);
  const scrolled = await state(page);
  assertNativeGpu(scrolled, 'DesignGpu', 'design-viewport', ratio);
  expect(scrolled.DesignLayoutBuilds).toBe(initial.DesignLayoutBuilds);
  expect(scrolled.ContentRevision).toBe(initial.ContentRevision);

  // Native layout and a finite scroll animation may still finish. Wait for the
  // submission count to stabilize before checking demand-driven idle behavior.
  let previous = -1, stable = 0;
  await expect.poll(async () => {
    await fresh(page);
    const current = (await state(page)).DesignGpu.SubmittedFrames;
    stable = current === previous ? stable + 1 : 0; previous = current;
    return stable;
  }).toBeGreaterThanOrEqual(3);
  const idle = (await state(page)).DesignGpu.SubmittedFrames;
  await fresh(page); await fresh(page); await fresh(page);
  expect((await state(page)).DesignGpu.SubmittedFrames).toBe(idle);

  await click(page, 'tab-report');
  await click(page, 'preview-report');
  await waitForGpu(page, 'PreviewGpu');
  await choose(page, 'preview-zoom', 9);
  await expect.poll(async () => (await state(page)).Zoom).toBe(4);
  await fresh(page);
  const preview = await state(page);
  assertNativeGpu(preview, 'PreviewGpu', 'preview-viewport', ratio);
  expect(preview.PreviewGpu.Viewport.Height).toBeLessThan(preview.Commands['preview-paper'].Height);
  expect(preview.Diagnostics).toEqual([]);
  await screenshot(page, info.outputPath('native-webgpu-preview.png'));
  await artifact(info, 'native-webgpu-surfaces.json', {
    devicePixelRatio: ratio, initial: initial.DesignGpu, zoomed: zoomed.DesignGpu,
    scrolled: scrolled.DesignGpu, idleSubmittedFrames: idle, preview: preview.PreviewGpu
  });
});

test('WebGPU unavailable state releases the splash and reports the rendering failure visibly', async ({ page }, info) => {
  // Exercise Uno's real fallback composition backend; the custom report canvas
  // must never claim a successful GPU frame or leave the loading overlay stuck.
  await page.addInitScript(() => {
    Object.defineProperty(Navigator.prototype, 'gpu', { configurable: true, get: () => undefined });
  });
  await page.goto('?diagnostics=1');
  await page.waitForFunction(() => window.reportScene?.renderingError || window.reportSceneFatal, null, { timeout: 180_000 });
  expect(await page.evaluate(() => window.reportSceneFatal)).toBeFalsy();
  expect(await page.evaluate(() => window.reportScene.renderingError)).toContain('WebGPU');
  await page.waitForFunction(() => window.reportScene?.presented || window.reportScene?.presentationError, null, { timeout: 60_000 });
  expect(await page.evaluate(() => window.reportScene.presentationError)).toBeFalsy();
  await expect(page.locator('.uno-loader, #uno-loading')).toHaveCount(0);
  await expect.poll(async () => (await state(page))?.DesignGpu?.State).toBe('Unavailable');
  const current = await state(page);
  expect(current.DesignGpu.Initialized).toBe(false);
  expect(current.DesignGpu.SubmittedFrames).toBe(0);
  expect(current.DesignGpu.Error).toContain('requires WebGPU');
  expect(current.Status).toContain('WebGPU');
  await screenshot(page, info.outputPath('webgpu-unavailable-message.png'));
  await artifact(info, 'webgpu-unavailable.json', current.DesignGpu);
});
