import { test, expect } from '@playwright/test';
import { ready, state, fresh, bounds, click, choose, artifact, screenshot } from './uno-driver.mjs';
import { waitForScrollSettled, createWheelScroller } from './scroll-settled.mjs';
import { screenshotPixels } from './screenshot-pixels.mjs';

const labelCount = 100, text = 'Cache 012345';
// Preparation emits one explicit white background and one text object per label.
const preparedObjectCount = labelCount * 2;
const items = Array.from({ length: labelCount }, (_, index) => ({
  $type: 'text', id: `warm-label-${index}`, name: `WarmLabel${index}`, text,
  bounds: { x: 16 + index % 5 * 112, y: 32 + Math.floor(index / 5) * 22, width: 104, height: 20 },
  style: { fontSize: 12, foreground: '#18365A', fill: '#FFFFFF', padding: 0, lineHeight: 1.2, borderWidth: 0 }
}));
const report = {
  name: 'Native WebGPU warm text recording',
  pages: [{ name: 'Repeated labels', width: 600, height: 1200,
    margins: { left: 0, top: 0, right: 0, bottom: 0 },
    bands: [{ id: 'warm-text-band', name: 'Repeated labels', kind: 'ReportTitle', height: 1100, items }]
  }]
};

function evidence(snapshot) {
  if (!snapshot) return null;
  return { sequence: snapshot.SnapshotSequence, gpu: snapshot.PreviewGpu,
    viewport: snapshot.PreviewViewport, paper: snapshot.Commands['preview-paper'],
    candidates: snapshot.PreviewCandidatesExamined, indexBuilds: snapshot.PreviewIndexBuilds,
    documentId: snapshot.DocumentId, contentRevision: snapshot.ContentRevision,
    historyCount: snapshot.HistoryCount, diagnostics: snapshot.Diagnostics };
}

async function submittedFrame(page, after = 0) {
  let snapshot;
  await expect.poll(async () => {
    snapshot = await state(page);
    const gpu = snapshot?.PreviewGpu, viewport = snapshot?.PreviewViewport, paint = viewport?.Paint;
    return gpu?.State === 'WebGPU' && gpu.Initialized === true && gpu.Error === null
      && gpu.SubmittedFrames > after && !viewport.IsIntermediate
      && Math.abs(paint.PointScale - 96 / 72) < .0001
      && ['X', 'Y', 'Width', 'Height'].every(key =>
        Math.abs(paint.Viewport[key] * paint.PointScale - gpu.Viewport[key]) < .01);
  }, { message: 'The native preview submits a new frame for the settled 100% viewport' }).toBe(true);
  return snapshot;
}

function assertWarmFrame(snapshot, initial) {
  const statistics = snapshot.PreviewGpu.Statistics;
  expect(snapshot.PreviewCandidatesExamined, 'All 100 label backgrounds and 100 text objects participate in the measured frame').toBe(preparedObjectCount);
  expect(statistics.PrimitiveCount).toBeGreaterThan(labelCount);
  expect(statistics.DrawCalls).toBeGreaterThan(0);
  expect(statistics.GlyphCacheHits, 'The frame records hundreds of visible cached glyphs').toBeGreaterThanOrEqual(labelCount * 8);
  // These are per-frame native operation counts, not elapsed-time thresholds.
  // Do not poll until zero: the selected submitted frame itself must be warm.
  for (const name of ['TextShapingOperations', 'TextBoundsQueries', 'TextFontCreations', 'GlyphRasterizations', 'TextureUploads'])
    expect(statistics[name], `Warm command recording must not perform ${name}`).toBe(0);
  expect(snapshot.PreviewIndexBuilds).toBe(initial.PreviewIndexBuilds);
  expect(snapshot.DocumentId).toBe(initial.DocumentId);
  expect(snapshot.ContentRevision).toBe(initial.ContentRevision);
  expect(snapshot.HistoryCount).toBe(initial.HistoryCount);
  expect(snapshot.Dirty).toBe(false);
  expect(snapshot.Diagnostics).toEqual([]);
}

async function labelPixels(page, info, name, snapshot, ratio) {
  const pixels = screenshotPixels(await screenshot(page, info.outputPath(`${name}.png`)));
  const paper = snapshot.Commands['preview-paper'], viewport = snapshot.Commands['preview-viewport'];
  const scale = snapshot.Zoom * 96 / 72;
  return [0, 52, 99].map(index => {
    const box = items[index].bounds;
    const screen = { left: paper.X + box.x * scale, top: paper.Y + box.y * scale,
      right: paper.X + (box.x + box.width) * scale, bottom: paper.Y + (box.y + box.height) * scale };
    expect(screen.left).toBeGreaterThan(viewport.X + 1);
    expect(screen.top).toBeGreaterThan(viewport.Y + 1);
    expect(screen.right).toBeLessThan(viewport.X + viewport.Width - 1);
    expect(screen.bottom).toBeLessThan(viewport.Y + viewport.Height - 1);
    let ink = 0, white = 0;
    for (let y = Math.ceil(screen.top * ratio); y < Math.floor(screen.bottom * ratio); y++)
      for (let x = Math.ceil(screen.left * ratio); x < Math.floor(screen.right * ratio); x++) {
        const [red, green, blue, alpha] = pixels.pixel(x, y);
        if (alpha === 255 && red < 96 && green < 116 && blue < 160) ink++;
        if (alpha === 255 && red > 250 && green > 250 && blue > 250) white++;
      }
    expect(ink, `Actual label ${index} retains visible text after native scrolling`).toBeGreaterThan(50);
    expect(white, `Actual label ${index} retains its white background`).toBeGreaterThan(100);
    return { index, screen, ink, white };
  });
}

test('native WebGPU warm text recording reuses shaped runs, glyph bounds and fonts across viewport scrolling', async ({ page }, info) => {
  const frames = [], wheelEvents = [];
  let imported;
  try {
    await ready(page);
    await click(page, 'tab-report');
    const open = await bounds(page, 'open'), choosing = page.waitForEvent('filechooser');
    await page.mouse.click(open.X + open.Width / 2, open.Y + open.Height / 2);
    await (await choosing).setFiles({ name: 'warm-text.report.json', mimeType: 'application/json',
      buffer: Buffer.from(JSON.stringify(report)) });
    await expect.poll(async () => (await state(page)).Document).toBe(report.name);
    imported = await state(page);
    expect(imported.ObjectCount).toBe(labelCount);
    await click(page, 'preview-report');
    await expect.poll(async () => (await state(page)).PreparedPages).toBe(1);
    await choose(page, 'preview-zoom', 4);
    await expect.poll(async () => (await state(page)).Zoom).toBe(1);
    await fresh(page);
    await waitForScrollSettled(page, 'PreviewViewport');
    const initial = await submittedFrame(page), ratio = await page.evaluate(() => devicePixelRatio);
    expect(initial.PreviewViewport.VerticalOffset).toBe(0);
    expect(initial.PreviewViewport.ExtentHeight - initial.PreviewViewport.Height).toBeGreaterThan(100);
    expect(initial.PreviewCandidatesExamined).toBe(preparedObjectCount);
    expect(initial.PreviewGpu.Statistics.GlyphCacheHits).toBeGreaterThanOrEqual(labelCount * 8);
    expect(initial.ContentRevision).toBe(imported.ContentRevision);
    frames.push({ name: 'initial', ...evidence(initial) });
    frames.at(-1).pixels = await labelPixels(page, info, 'warm-text-initial', initial, ratio);

    // Calibrate only from acknowledged native wheel movement. The tall page
    // keeps a real scroll extent, while all duplicate labels remain visible.
    const scroll = createWheelScroller(page, 'PreviewViewport');
    let previous = initial;
    for (const [name, target] of [['down', 20], ['back', 0]]) {
      const before = previous;
      for (let attempt = 0; attempt < 8; attempt++) {
        const snapshot = await waitForScrollSettled(page, 'PreviewViewport');
        const correction = target - snapshot.PreviewViewport.VerticalOffset;
        if (Math.abs(correction) < 1) break;
        const viewport = snapshot.Commands['preview-viewport'];
        await page.mouse.move(viewport.X + viewport.Width / 2, viewport.Y + viewport.Height / 2);
        const moved = await scroll(0, correction);
        wheelEvents.push({ name, correction, before: evidence(snapshot), after: evidence(moved) });
      }
      previous = await submittedFrame(page, before.PreviewGpu.SubmittedFrames);
      frames.push({ name, targetOffset: target, ...evidence(previous) });
      expect(Math.abs(previous.PreviewViewport.VerticalOffset - target)).toBeLessThan(2);
      expect(previous.PreviewViewport.ViewChangeVersion).toBeGreaterThan(before.PreviewViewport.ViewChangeVersion);
      assertWarmFrame(previous, initial);
      frames.at(-1).pixels = await labelPixels(page, info, `warm-text-${name}`, previous, ratio);
    }
  } finally {
    await artifact(info, 'native-webgpu-warm-text.json', { report, imported: imported && evidence(imported), frames, wheelEvents,
      final: page.isClosed() ? null : evidence(await state(page)) });
  }
});
