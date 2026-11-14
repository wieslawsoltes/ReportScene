import { test, expect } from '@playwright/test';
import { ready, state, fresh, bounds, click, choose, artifact, screenshot } from './uno-driver.mjs';
import { waitForScrollSettled, createWheelScroller } from './scroll-settled.mjs';
import { screenshotPixels } from './screenshot-pixels.mjs';

const barcode = { x: 48, y: 180, width: 340, height: 44 };
const report = {
  name: 'Native WebGPU physical pixel density',
  pages: [{ name: 'Sharp edges and small text', width: 440, height: 1200,
    margins: { left: 0, top: 0, right: 0, bottom: 0 },
    bands: [{ id: 'pixel-density-band', name: 'Physical pixel probes', kind: 'ReportTitle', height: 1100,
      items: [
        ...[8, 10, 12, 14].map((size, index) => ({
          $type: 'text', id: 'density-text-' + size, name: 'Text at ' + size + ' points',
          text: 'Hamburgefonts HIl1 0123456789 — ' + size + ' pt',
          bounds: { x: 48, y: 40 + index * 28, width: 340, height: 24 },
          style: { fontSize: size, foreground: '#000000', fill: '#FFFFFF', padding: 0, borderWidth: 0 }
        })),
        { $type: 'barcode', id: 'density-barcode', name: 'Hard edge density probe',
          text: 'DPI-SHARP-12345', symbology: 'Code128', showText: false, bounds: barcode,
          style: { foreground: '#000000', fill: '#FFFFFF', borderWidth: 0 } }
      ]
    }]
  }]
};

function surfaceObservation(snapshot, mode) {
  const key = mode === 'design' ? 'Design' : 'Preview';
  return { sequence: snapshot.SnapshotSequence, mode, zoom: snapshot.Zoom,
    gpu: snapshot[key + 'Gpu'], scroll: snapshot[key + 'Viewport'],
    viewport: snapshot.Commands[mode + '-viewport'], paper: snapshot.Commands[mode + '-paper'],
    paintViewport: mode === 'design' ? snapshot.DesignPaintViewport : snapshot.PreviewViewport.Paint.Viewport,
    documentId: snapshot.DocumentId, revision: snapshot.ContentRevision,
    historyCount: snapshot.HistoryCount, dirty: snapshot.Dirty, diagnostics: snapshot.Diagnostics,
    designLayoutBuilds: snapshot.DesignLayoutBuilds, previewIndexBuilds: snapshot.PreviewIndexBuilds };
}

async function submittedFrame(page, mode, zoom, after = 0) {
  const key = mode === 'design' ? 'Design' : 'Preview';
  await waitForScrollSettled(page, key + 'Viewport');
  let snapshot, previous = '', previousSequence = -1, stable = 0;
  await expect.poll(async () => {
    snapshot = await state(page);
    const gpu = snapshot?.[key + 'Gpu'], scroll = snapshot?.[key + 'Viewport'];
    const painted = mode === 'design' ? snapshot?.DesignPaintViewport : scroll?.Paint?.Viewport;
    const scale = zoom * 96 / 72;
    if (!gpu || !painted || snapshot.Zoom !== zoom || scroll.IsIntermediate
        || gpu.State !== 'WebGPU' || gpu.Initialized !== true || gpu.Error !== null
        || gpu.SubmittedFrames <= after || gpu.PixelWidth <= 0 || gpu.PixelHeight <= 0
        || !['X', 'Y', 'Width', 'Height'].every(axis => Math.abs(painted[axis] * scale - gpu.Viewport[axis]) < .01)
        || (mode === 'preview' && Math.abs(scroll.Paint.PointScale - scale) > .0001)) {
      previous = ''; stable = 0; return false;
    }
    if (snapshot.SnapshotSequence === previousSequence) return false;
    previousSequence = snapshot.SnapshotSequence;
    const current = JSON.stringify([gpu.SubmittedFrames, gpu.Viewport, gpu.PixelWidth, gpu.PixelHeight,
      gpu.PresentationBounds, snapshot.Commands[mode + '-paper'], scroll.HorizontalOffset, scroll.VerticalOffset]);
    stable = current === previous ? stable + 1 : 1; previous = current;
    return stable >= 3;
  }, { message: 'The ' + mode + ' surface submits and settles at zoom ' + zoom }).toBe(true);
  await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
  return snapshot;
}

function assertPhysicalSurface(frame, density) {
  const gpu = frame.gpu, observed = gpu.PresentationBounds;
  expect(gpu.State).toBe('WebGPU');
  expect(gpu.Initialized).toBe(true);
  expect(gpu.Error).toBeNull();
  expect(gpu.Statistics.PrimitiveCount).toBeGreaterThan(0);
  // Both bounds are essential: the old upper-only check accepted a 1x target
  // enlarged by the compositor on a Retina display.
  for (const axis of ['Width', 'Height']) {
    expect(Math.abs(gpu['Pixel' + axis] - gpu.Viewport[axis] * density),
      frame.name + ': target ' + axis + ' must cover the physical viewport without rescaling').toBeLessThan(.01);
    expect(gpu['Pixel' + axis], 'Targets remain bounded by the visible viewport and its guard').toBeLessThanOrEqual(
      Math.ceil((frame.viewport[axis] + 6) * density));
  }
  expect(gpu.RasterizationScale, 'The native root must observe the browser density').toBeCloseTo(density, 6);
  expect(observed, 'Inspect actual arranged child bounds in the root, not only intended viewport arithmetic').toBeTruthy();
  for (const axis of ['X', 'Y']) {
    const physical = observed[axis] * density;
    expect(Math.abs(physical - Math.round(physical)),
      frame.name + ': compositor ' + axis + ' origin must lie on a physical pixel').toBeLessThan(.01);
  }
  for (const axis of ['Width', 'Height'])
    expect(Math.abs(observed[axis] * density - gpu['Pixel' + axis]),
      frame.name + ': the compositor must present each target pixel at native size').toBeLessThan(.01);
  if (gpu.RequestedViewport) {
    const requested = gpu.RequestedViewport, actual = gpu.Viewport;
    for (const [origin, size] of [['X', 'Width'], ['Y', 'Height']]) {
      const before = (requested[origin] - actual[origin]) * density;
      const after = (actual[origin] + actual[size] - requested[origin] - requested[size]) * density;
      expect(before, 'Pixel alignment must not clip the requested leading edge').toBeGreaterThanOrEqual(-.01);
      expect(after, 'Pixel alignment must not clip the requested trailing edge').toBeGreaterThanOrEqual(-.01);
      expect(before, 'Outward alignment needs less than one extra leading pixel').toBeLessThan(1.01);
      expect(after, 'Outward alignment needs less than one extra trailing pixel').toBeLessThan(1.01);
    }
  }
}

function barcodeProfiles(pixels, frame, density) {
  const scale = frame.zoom * 96 / 72, paper = frame.paper, viewport = frame.viewport;
  const box = { x: paper.X + barcode.x * scale, y: paper.Y + barcode.y * scale,
    width: barcode.width * scale, height: barcode.height * scale };
  const visible = box.x > viewport.X + 2 && box.y > viewport.Y + 2
    && box.x + box.width < viewport.X + viewport.Width - 2
    && box.y + box.height < viewport.Y + viewport.Height - 2;
  if (!visible) return { visible, box, rows: [] };
  const firstX = Math.ceil(box.x * density) + 2, endX = Math.floor((box.x + box.width) * density) - 2;
  const rows = [.25, .5, .75].map(fraction => {
    const y = Math.floor((box.y + box.height * fraction) * density);
    const profile = [], unexpected = [];
    let black = 0, white = 0, intermediate = 0, transitions = 0, mixedRun = 0, maximumMixedRun = 0, previous = -1;
    for (let x = firstX; x < endX; x++) {
      const [red, green, blue, alpha] = pixels.pixel(x, y);
      profile.push(red);
      if (alpha !== 255 || Math.max(red, green, blue) - Math.min(red, green, blue) > 1)
        unexpected.push({ x, rgba: [red, green, blue, alpha] });
      // Only a one-step RGBA8 rounding allowance at the black/white endpoints.
      // This ordinary barcode path disables AA; intermediate shades demonstrate
      // resampling, not glyph hinting or intentional analytic edge coverage.
      if (Math.max(red, green, blue) <= 1) { black++; mixedRun = 0; }
      else if (Math.min(red, green, blue) >= 254) { white++; mixedRun = 0; }
      else { intermediate++; maximumMixedRun = Math.max(maximumMixedRun, ++mixedRun); }
      const next = red >= 128 ? 1 : 0;
      if (previous !== -1 && next !== previous) transitions++;
      previous = next;
    }
    return { y, firstX, endX, black, white, intermediate, transitions, maximumMixedRun, unexpected, profile };
  });
  return { visible, box, rows };
}

function assertBarcode(frame) {
  expect(frame.barcode.visible, 'The complete barcode must be inside the actual native preview viewport').toBe(true);
  expect(frame.barcode.rows).toHaveLength(3);
  for (const row of frame.barcode.rows) {
    expect(row.unexpected, frame.name + ': barcode pixels remain opaque and neutral').toEqual([]);
    expect(row.black).toBeGreaterThan((row.endX - row.firstX) * .1);
    expect(row.white).toBeGreaterThan((row.endX - row.firstX) * .1);
    expect(row.transitions, 'Inspect a real dense barcode, not a blank or solid target').toBeGreaterThan(30);
    expect(row.intermediate, frame.name + ': physical barcode edges must not acquire compositor filtering').toBe(0);
  }
}

for (const density of [1.25, 2]) test.describe('Native physical density ' + density, () => {
  test.use({ deviceScaleFactor: density });
  test('native WebGPU surfaces preserve sharp physical pixels at DPR ' + density, async ({ page }, info) => {
    const frames = [], wheelEvents = [];
    let imported;
    async function observe(name, mode, zoom, after = 0) {
      const snapshot = await submittedFrame(page, mode, zoom, after);
      const frame = { name, ...surfaceObservation(snapshot, mode) };
      frames.push(frame);
      const pixels = screenshotPixels(await screenshot(page, info.outputPath(name + '.png')));
      frame.screenshot = { width: pixels.width, height: pixels.height };
      if (mode === 'preview') frame.barcode = barcodeProfiles(pixels, frame, density);
      return snapshot;
    }
    try {
      await ready(page);
      expect(await page.evaluate(() => devicePixelRatio)).toBe(density);
      await click(page, 'tab-report');
      const open = await bounds(page, 'open'), choosing = page.waitForEvent('filechooser');
      await page.mouse.click(open.X + open.Width / 2, open.Y + open.Height / 2);
      await (await choosing).setFiles({ name: 'pixel-density.report.json', mimeType: 'application/json',
        buffer: Buffer.from(JSON.stringify(report)) });
      await expect.poll(async () => (await state(page)).Document).toBe(report.name);
      imported = await state(page);
      await click(page, 'tab-view'); await choose(page, 'design-zoom', 4); await click(page, 'tab-home');
      const design = await observe('design-100', 'design', 1);
      await click(page, 'tab-view'); await choose(page, 'design-zoom', 3); await click(page, 'tab-home');
      await observe('design-85', 'design', .85, design.DesignGpu.SubmittedFrames);

      await click(page, 'tab-report'); await click(page, 'preview-report');
      await expect.poll(async () => (await state(page)).PreparedPages).toBe(1);
      await choose(page, 'preview-zoom', 4);
      const initialPreview = await observe('preview-100', 'preview', 1);
      await choose(page, 'preview-zoom', 3);
      const fractional = await observe('preview-85', 'preview', .85, initialPreview.PreviewGpu.SubmittedFrames);

      // Odd dimensions change the centered paper's root-space phase. The target
      // must remain pixel-sized and aligned after native layout is recomputed.
      await page.setViewportSize({ width: 1537, height: 997 });
      const resized = await observe('preview-resized-85', 'preview', .85, fractional.PreviewGpu.SubmittedFrames);
      expect(resized.PreviewViewport.ExtentHeight - resized.PreviewViewport.Height).toBeGreaterThan(100);

      const scroll = createWheelScroller(page, 'PreviewViewport');
      let scrolled = resized;
      for (const delta of [17.25, 13.75, 9.125, 7.625]) {
        const viewport = scrolled.Commands['preview-viewport'];
        await page.mouse.move(viewport.X + viewport.Width / 2, viewport.Y + viewport.Height / 2);
        const before = scrolled;
        scrolled = await scroll(0, delta);
        wheelEvents.push({ delta, before: before.PreviewViewport, after: scrolled.PreviewViewport });
        const offset = scrolled.PreviewViewport.VerticalOffset;
        if (Math.abs(offset - Math.round(offset)) > .01) break;
      }
      const offset = scrolled.PreviewViewport.VerticalOffset;
      await page.mouse.move(1, 1); await fresh(page);
      await observe('preview-scrolled-85', 'preview', .85, resized.PreviewGpu.SubmittedFrames);

      // Collect the complete presented evidence before checking density. This
      // also makes the historical undersampled build's barcode failure visible
      // without changing the assertions or substituting a synthetic renderer.
      for (const frame of frames) {
        assertPhysicalSurface(frame, density);
        if (frame.barcode) assertBarcode(frame);
        expect(frame.documentId).toBe(imported.DocumentId);
        expect(frame.revision).toBe(imported.ContentRevision);
        expect(frame.historyCount).toBe(imported.HistoryCount);
        expect(frame.dirty).toBe(false);
        expect(frame.diagnostics).toEqual([]);
        expect(frame.designLayoutBuilds).toBe(imported.DesignLayoutBuilds);
        if (frame.mode === 'preview') expect(frame.previewIndexBuilds).toBe(initialPreview.PreviewIndexBuilds);
      }
      expect(Math.abs(offset - Math.round(offset)), 'Exercise an actual fractional-DIP native scroll offset').toBeGreaterThan(.01);
      expect(scrolled.PreviewViewport.ViewChangeVersion).toBeGreaterThan(resized.PreviewViewport.ViewChangeVersion);
    } finally {
      await artifact(info, 'native-webgpu-pixel-density.json', { density, report, frames, wheelEvents,
        final: page.isClosed() ? null : await state(page) });
    }
  });
});
