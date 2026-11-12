import { test, expect } from '@playwright/test';
import { ready, state, fresh, bounds, click, choose, artifact, screenshot } from './uno-driver.mjs';
import { screenshotPixels } from './screenshot-pixels.mjs';
import { waitForScrollSettled } from './scroll-settled.mjs';

const blue = [0, 96, 224, 255], magenta = [208, 32, 144, 255], white = [255, 255, 255, 255];
const report = {
  name: 'Native ellipse stroke geometry',
  pages: [{ name: 'Ellipses', width: 440, height: 220,
    margins: { left: 0, top: 0, right: 0, bottom: 0 },
    bands: [{ id: 'ellipse-band', name: 'Ellipse strokes', kind: 'ReportTitle', height: 150,
      items: [
        { $type: 'shape', id: 'thick-circle', name: 'Thick circle', shape: 'Ellipse',
          bounds: { x: 50, y: 50, width: 30, height: 30 },
          style: { fill: '#00000000', borderColor: '#0060E0', borderWidth: 60 } },
        { $type: 'shape', id: 'eccentric-ring', name: 'Eccentric thin ring', shape: 'Ellipse',
          bounds: { x: 180, y: 68, width: 200, height: 24 },
          style: { fill: '#00000000', borderColor: '#D02090', borderWidth: 16 } }
      ]
    }]
  }]
};

// Report points, safely inside or outside the geometry rather than on AA edges.
// The eccentric ring's medial point (90,0) is about 5.07pt from the true ellipse;
// the former implicit-gradient estimate exceeds 10pt and misses its 8pt stroke.
const probes = [
  { name: 'thick transparent-fill circle has a solid stroked center', point: [65, 65], expected: blue },
  { name: 'thick circle reaches beyond its nominal right edge', point: [107, 65], expected: blue },
  { name: 'thick circle reaches beyond its nominal top edge', point: [65, 23], expected: blue },
  { name: 'thick circle exterior remains white', point: [114, 65], expected: white },
  { name: 'eccentric ring preserves its unfilled center', point: [280, 80], expected: white },
  { name: 'eccentric medial stroke uses the nearest boundary', point: [370, 80], expected: magenta },
  { name: 'eccentric stroke reaches beyond its nominal right edge', point: [384, 80], expected: magenta },
  { name: 'eccentric stroke reaches beyond its nominal top edge', point: [280, 63], expected: magenta },
  { name: 'eccentric right exterior remains white', point: [392, 80], expected: white },
  { name: 'eccentric top exterior remains white', point: [280, 56], expected: white }
];

test('prepared WebGPU ellipse strokes preserve solid centers, eccentric holes and true outer extents', async ({ page }, info) => {
  await ready(page);
  await click(page, 'tab-report');
  const open = await bounds(page, 'open'), choosing = page.waitForEvent('filechooser');
  await page.mouse.click(open.X + open.Width / 2, open.Y + open.Height / 2);
  await (await choosing).setFiles({ name: 'ellipse-strokes.report.json', mimeType: 'application/json',
    buffer: Buffer.from(JSON.stringify(report)) });
  await expect.poll(async () => (await state(page)).Document).toBe(report.name);
  const imported = await state(page);
  await click(page, 'preview-report');
  await expect.poll(async () => (await state(page)).PreparedPages).toBe(1);
  await choose(page, 'preview-zoom', 4);
  await expect.poll(async () => (await state(page)).Zoom).toBe(1);
  await fresh(page);
  await waitForScrollSettled(page, 'PreviewViewport');
  const ratio = await page.evaluate(() => devicePixelRatio);

  let evidence;
  try {
    await expect.poll(async () => {
      const snapshot = await state(page), paper = snapshot.Commands['preview-paper'], viewport = snapshot.Commands['preview-viewport'];
      const gpu = snapshot.PreviewGpu, scale = snapshot.Zoom * 96 / 72;
      if (!paper || !viewport || !snapshot.PreviewViewport || !gpu || snapshot.PreviewViewport.IsIntermediate || gpu.State !== 'WebGPU' ||
          gpu.Initialized !== true || gpu.Error !== null || gpu.SubmittedFrames < 1 ||
          Math.abs(paper.Width - 440 * scale) > 1 || Math.abs(paper.Height - 220 * scale) > 1) return false;
      const pixels = screenshotPixels(await screenshot(page, info.outputPath('prepared-ellipse-strokes.png')));
      const samples = probes.map(probe => {
        const x = paper.X + probe.point[0] * scale, y = paper.Y + probe.point[1] * scale;
        const visible = x - 2 > viewport.X && x + 2 < viewport.X + viewport.Width &&
          y - 2 > viewport.Y && y + 2 < viewport.Y + viewport.Height;
        const actual = [];
        if (visible) for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++)
          actual.push(pixels.pixel(x * ratio + dx, y * ratio + dy));
        return { ...probe, screenPoint: [x, y], visible, actual };
      });
      evidence = { zoom: snapshot.Zoom, devicePixelRatio: ratio, paper, viewport, gpu, samples };
      return samples.every(sample => sample.visible && sample.actual.length === 9 &&
        sample.actual.every(pixel => pixel.every((value, channel) => Math.abs(value - sample.expected[channel]) <= 3)));
    }, { message: 'Actual prepared Uno output preserves the thick and eccentric ellipse stroke geometry' }).toBe(true);
  } finally {
    await artifact(info, 'prepared-ellipse-strokes.json', { report, ...evidence });
  }

  const final = await state(page);
  expect(final.Diagnostics).toEqual([]);
  expect(final.ContentRevision).toBe(imported.ContentRevision);
  expect(final.HistoryCount).toBe(imported.HistoryCount);
  expect(final.Dirty).toBe(false);
});
