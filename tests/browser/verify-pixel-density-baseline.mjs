import assert from 'node:assert/strict';
import { readFile, readdir } from 'node:fs/promises';
import path from 'node:path';
import { screenshotPixels } from './screenshot-pixels.mjs';

// Optional historical qualification, never a substitute for passing candidate
// tests. Supply an unchanged spec's evidence and JSON report from a separately
// served baseline app. No remote artifact, branch or CI dependency is embedded.
const [evidenceRoot, reportPath] = process.argv.slice(2);
if (!evidenceRoot || !reportPath) throw new Error(
  'Usage: node verify-pixel-density-baseline.mjs EVIDENCE_DIRECTORY PLAYWRIGHT_REPORT_JSON');
const browserReport = JSON.parse(await readFile(reportPath, 'utf8'));
const tests = [];
function visit(suite) {
  for (const spec of suite.specs ?? [])
    for (const result of spec.tests ?? []) tests.push({ ...result, title: spec.title });
  for (const child of suite.suites ?? []) visit(child);
}
for (const suite of browserReport.suites ?? []) visit(suite);
assert.equal(tests.length, 2, 'Run only the two historical density scenarios.');
assert.equal(browserReport.errors?.length ?? 0, 0, 'Infrastructure failures are not baseline regression evidence.');
assert.equal(browserReport.stats.unexpected, 2);
assert.equal(browserReport.stats.flaky, 0);
assert.equal(browserReport.stats.skipped, 0);
for (const density of [1.25, 2]) {
  const title = 'native WebGPU surfaces preserve sharp physical pixels at DPR ' + density;
  const matching = tests.filter(test => test.title === title);
  assert.equal(matching.length, 1, 'Missing or duplicate baseline scenario: ' + title);
  const test = matching[0];
  assert.equal(test.expectedStatus, 'passed', 'Do not mark the baseline assertions as expected failures.');
  assert.equal(test.results.length, 1, 'Retain the first execution without retries.');
  const result = test.results[0];
  assert.equal(result.status, 'failed');
  assert.equal(result.retry ?? 0, 0);
  const message = result.error?.message ?? result.errors?.[0]?.message ?? '';
  assert.ok(message.includes('target Width must cover the physical viewport without rescaling'),
    'The baseline must fail on measured target density, not startup, input, missing diagnostics or timeouts.');
}

async function findEvidence(directory) {
  const result = [];
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const file = path.join(directory, entry.name);
    if (entry.isDirectory()) result.push(...await findEvidence(file));
    else if (entry.name === 'native-webgpu-pixel-density.json') result.push(file);
  }
  return result;
}
const files = await findEvidence(evidenceRoot);
assert.equal(files.length, 2, 'Retain one complete native observation set per density.');
const phaseNames = ['design-100', 'design-85', 'preview-100', 'preview-85', 'preview-resized-85', 'preview-scrolled-85'];
const densities = new Set(), summaries = [];
for (const file of files) {
  const evidence = JSON.parse(await readFile(file, 'utf8'));
  const { density, frames } = evidence;
  assert.ok([1.25, 2].includes(density) && !densities.has(density));
  densities.add(density);
  assert.deepEqual(frames.map(frame => frame.name), phaseNames, 'All six real native phases must finish.');
  assert.ok(evidence.wheelEvents.length > 0, 'The historical app must receive real wheel input.');
  const densityRanges = [], profiles = [];
  let intermediatePixels = 0;
  for (const frame of frames) {
    const gpu = frame.gpu;
    assert.equal(gpu.State, 'WebGPU');
    assert.equal(gpu.Initialized, true);
    assert.equal(gpu.Error, null);
    assert.ok(gpu.SubmittedFrames > 0 && gpu.Statistics.PrimitiveCount > 0);
    assert.equal(frame.scroll.IsIntermediate, false);
    assert.deepEqual(frame.diagnostics, []);
    const measured = {};
    for (const axis of ['Width', 'Height']) {
      const logical = gpu.Viewport[axis], physical = gpu['Pixel' + axis];
      assert.ok(logical > 100 && Number.isSafeInteger(physical) && physical > 0);
      // The old XAML child can round its logical extent; at most two pixels
      // accommodate that observation while still proving an approximately 1x
      // backing target, far below either requested physical density.
      assert.ok(Math.abs(physical - logical) < 2.01, frame.name + ': the historical target must remain about 1x.');
      assert.ok(Math.abs(physical - logical * density) > logical * .2,
        frame.name + ': independently establish material physical undersampling.');
      measured[axis] = physical / logical;
    }
    densityRanges.push({ name: frame.name, ...measured });
    if (frame.mode !== 'preview') continue;
    assert.equal(frame.barcode.visible, true);
    assert.equal(frame.barcode.rows.length, 3);
    const pixels = screenshotPixels(await readFile(path.join(path.dirname(file), frame.name + '.png')));
    assert.equal(pixels.width, frame.screenshot.width);
    assert.equal(pixels.height, frame.screenshot.height);
    for (const row of frame.barcode.rows) {
      let black = 0, white = 0, intermediate = 0, transitions = 0, mixedRun = 0, maximumMixedRun = 0, previous = -1;
      const profile = [];
      for (let x = row.firstX; x < row.endX; x++) {
        const [red, green, blue, alpha] = pixels.pixel(x, row.y);
        assert.equal(alpha, 255);
        assert.ok(Math.max(red, green, blue) - Math.min(red, green, blue) <= 1);
        profile.push(red);
        if (Math.max(red, green, blue) <= 1) { black++; mixedRun = 0; }
        else if (Math.min(red, green, blue) >= 254) { white++; mixedRun = 0; }
        else { intermediate++; maximumMixedRun = Math.max(maximumMixedRun, ++mixedRun); }
        const next = red >= 128 ? 1 : 0;
        if (previous !== -1 && next !== previous) transitions++;
        previous = next;
      }
      assert.deepEqual(profile, row.profile, 'The retained PNG must reproduce every raw barcode sample.');
      assert.deepEqual({ black, white, intermediate, transitions, maximumMixedRun },
        { black: row.black, white: row.white, intermediate: row.intermediate,
          transitions: row.transitions, maximumMixedRun: row.maximumMixedRun });
      assert.ok(black > profile.length * .1 && white > profile.length * .1 && transitions > 30);
      intermediatePixels += intermediate;
      profiles.push({ name: frame.name, y: row.y, intermediate, maximumMixedRun, transitions });
    }
  }
  assert.ok(intermediatePixels > 0, 'Presented baseline hard edges must independently demonstrate filtering.');
  summaries.push({ density, frames: frames.length, backingPixelsPerDip: densityRanges, intermediatePixels, barcodeProfiles: profiles });
}
console.log(JSON.stringify({ status: 'expected-baseline-density-regression-reproduced',
  scope: 'Historical actual Uno target density and presented hard-edge pixels; both unchanged tests failed.',
  densities: summaries.sort((a, b) => a.density - b.density) }, null, 2));
