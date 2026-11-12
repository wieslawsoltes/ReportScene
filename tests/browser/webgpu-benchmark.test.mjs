import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { parseOptions } from './webgpu-benchmark.mjs';
import { buildWorkload, comparePixelBytes, compareTrialTimings, decodeTimestampPair, distribution,
  primitiveStrideBytes, shaderVariantOrder, summarizeFrames, textureFixture } from './webgpu-benchmark-workloads.mjs';

test('benchmark rejects unbounded or ambiguous configurations before opening a browser', () => {
  const defaults = parseOptions([]);
  assert.equal(defaults.software, false);
  assert.equal(defaults.timestamps, 'off');
  assert.equal(defaults.uploads, 'instances');
  assert.equal(defaults.baselineShader, null);
  for (const args of [
    ['--software', '--require-hardware'], ['--instances', '0'], ['--instances', '262145'],
    ['--width', '8192', '--height', '8192'], ['--dpr', 'NaN'], ['--samples', '2'],
    ['--warmup', '1000', '--samples', '1000', '--repeats', '10'],
    ['--workloads', 'shapes,shapes'], ['--workloads', 'constructor'], ['--workloads', 'shapes,'],
    ['--timestamps', 'yes'], ['--width', '800', '--width', '800'], ['--unknown'], ['--baseline-shader', ''],
    ['--workloads', 'shapes', '--warmup', '1000', '--samples', '1000', '--repeats', '10', '--baseline-shader', 'baseline.wgsl']
  ]) assert.throws(() => parseOptions(args), undefined, args.join(' '));
  const smoke = parseOptions(['--software', '--instances', '128', '--width', '640', '--height', '480',
    '--warmup', '2', '--samples', '3', '--repeats', '1', '--timestamps', 'auto']);
  assert.equal(smoke.software, true); assert.equal(smoke.timestamps, 'auto');
  assert.equal(smoke.workloads.length, 4);
  const comparison = parseOptions(['--baseline-shader', 'baseline.wgsl', '--workloads', 'diagonal-lines,ellipse-strokes']);
  assert.ok(comparison.baselineShader.endsWith('/baseline.wgsl'));
  assert.deepEqual(comparison.workloads, ['diagonal-lines', 'ellipse-strokes']);
});

test('historical default workload bytes remain unchanged from revision 22939aa', () => {
  const expected = {
    shapes: 'c1b5f3b5d9f58e8bec347afe99faeebb20f0662d9fd273128b832613cb3b87e3',
    glyphs: '7df09a4442eaadec0a80285f2c95e732a21dfd586fad6aae7f5a481f64e36f0c',
    images: '422d406e421ba330f6a1a263d54000dfa57b441f5cac432e4a8b05fa73d85982',
    mixed: '718968d9550a99082c11ef90bbcf4c1e1ecc1c84912bc92213be581d4eda7276'
  };
  const config = { width: 800, height: 500, instances: 512, seed: 104729, batchLimit: 262144 };
  for (const [name, hash] of Object.entries(expected)) {
    const bytes = buildWorkload(name, config).primitives;
    assert.equal(createHash('sha256').update(new Uint8Array(bytes.buffer, bytes.byteOffset, bytes.byteLength)).digest('hex'), hash);
  }
});

test('targeted line and ellipse fixtures retain full caps, clipping, translucency and bounded ordered input', () => {
  const config = { width: 800, height: 500, dpr: 1.25, instances: 96, seed: 104729, batchLimit: 262144 };
  for (const name of ['diagonal-lines', 'ellipse-strokes']) {
    const fixture = buildWorkload(name, config), split = buildWorkload(name, { ...config, batchLimit: 5 });
    assert.deepEqual(fixture.primitives, split.primitives);
    assert.equal(fixture.kindCounts[name === 'diagonal-lines' ? 3 : 2], config.instances);
    assert.equal(fixture.batches.length, 1);
    assert.ok(split.batches.every(batch => batch.count <= 5));
    const caps = new Set(), directions = new Set(), antialias = new Set();
    for (let index = 0; index < fixture.count; index++) {
      const p = fixture.primitives.subarray(index * 32, (index + 1) * 32);
      assert.ok(p.every(Number.isFinite));
      assert.ok(p[7] >= .25 && p[7] < .9);
      assert.ok(p[4] <= p[7] && p[5] <= p[7] && p[6] <= p[7]);
      assert.ok(p[26] > 0 && p[27] > 0);
      assert.ok(p[24] >= p[8] - .001 && p[25] >= p[9] - .001);
      assert.ok(p[24] + p[26] <= p[10] + .001 && p[25] + p[27] <= p[11] + .001);
      antialias.add(p[14]);
      if (name === 'diagonal-lines') {
        caps.add(p[15]); directions.add(Math.sign((p[18] - p[16]) * (p[19] - p[17])));
        assert.ok(p[2] > config.width * .67 && p[3] > config.height * .67);
        if (index % 7 !== 0 && p[15] === 1) {
          const cap = p[13] * (p[2] + p[3]) / Math.hypot(p[2], p[3]);
          assert.ok(p[24] < p[0] - cap && p[25] < p[1] - cap, 'Square-cap extrema must be inside the baseline quad.');
        }
      } else {
        assert.equal(p[16], 1); assert.equal(p[17], 1);
        assert.ok(p[18] >= 10 && p[18] <= 24);
        assert.ok(p[13] < Math.min(p[2], p[3]) * .5, 'Targeted ellipse strokes must retain a large interior hole.');
      }
    }
    assert.deepEqual([...antialias].sort(), [0, 1]);
    if (name === 'diagonal-lines') { assert.deepEqual([...caps].sort(), [0, 1, 2]); assert.deepEqual([...directions].sort(), [-1, 1]); }
  }
});

test('paired comparisons alternate variant order and detect every changed RGBA byte without tolerance', () => {
  assert.deepEqual(shaderVariantOrder(0, true), ['baseline', 'candidate']);
  assert.deepEqual(shaderVariantOrder(1, true), ['candidate', 'baseline']);
  assert.deepEqual(shaderVariantOrder(2, true), ['baseline', 'candidate']);
  assert.deepEqual(shaderVariantOrder(1, false), ['candidate']);
  assert.throws(() => shaderVariantOrder(-1, true));
  const baseline = new Uint8Array([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]);
  assert.equal(comparePixelBytes(baseline, baseline.slice()).differingBytes, 0);
  const actual = new Uint8Array([1, 3, 3, 4, 5, 6, 8, 8, 0, 10, 11, 12]);
  const comparison = comparePixelBytes(baseline, actual);
  assert.equal(comparison.differingBytes, 3); assert.equal(comparison.differingPixels, 3);
  assert.equal(comparison.maximumChannelDifference, 9); assert.equal(comparison.bytesCompared, 12);
  assert.deepEqual(comparison.examples[0], { pixel: 0, baseline: [1, 2, 3, 4], candidate: [1, 3, 3, 4] });
  assert.throws(() => comparePixelBytes(baseline, actual.subarray(0, 4)));
  assert.throws(() => comparePixelBytes(new Uint8Array(3), new Uint8Array(3)));
});

test('comparison ratios keep missing or zero GPU medians unavailable and retain separate CPU intervals', () => {
  const frame = gpu => ({ cpuPrepareMs: 1, cpuSubmitCallMs: 2, queueCompletionWallMs: 20, postSubmitWaitMs: 17,
    queryReadbackWallMs: 0, gpuRenderPassMs: gpu, timestampRejection: null });
  const trial = gpu => ({ statistics: summarizeFrames([frame(gpu), frame(gpu), frame(gpu)]) });
  const comparison = compareTrialTimings(trial(8), trial(2));
  assert.equal(comparison.gpuRenderPassMs.baselineOverCandidateP50, 4);
  assert.equal(comparison.queueCompletionWallMs.baselineOverCandidateP50, 1);
  assert.equal(compareTrialTimings(trial(null), trial(2)).gpuRenderPassMs.baselineOverCandidateP50, null);
  assert.equal(compareTrialTimings(trial(8), trial(0)).gpuRenderPassMs.baselineOverCandidateP50, null);
  assert.equal(compareTrialTimings(trial(0), trial(2)).gpuRenderPassMs.baselineOverCandidateP50, null);
  assert.equal(comparison.queryReadbackWallMs.baselineOverCandidateP50, null);
});

test('ordered batches cover each primitive exactly once without changing the instance stream', () => {
  const config = { width: 800, height: 500, instances: 512, seed: 104729, batchLimit: 262144 };
  for (const invalid of [{ width: Infinity }, { height: NaN }, { dpr: Infinity }, { dpr: 0 }, { seed: -1 }])
    assert.throws(() => buildWorkload('mixed', { ...config, ...invalid }));
  for (const name of ['shapes', 'glyphs', 'images', 'mixed']) {
    const workload = buildWorkload(name, config);
    const split = buildWorkload(name, { ...config, batchLimit: 7 });
    assert.equal(workload.primitives.byteLength, workload.count * primitiveStrideBytes);
    assert.deepEqual(workload.primitives, buildWorkload(name, config).primitives);
    assert.notDeepEqual(workload.primitives, buildWorkload(name, { ...config, seed: 104730 }).primitives);
    assert.deepEqual(workload.primitives, split.primitives, 'Batching must preserve painter order and geometry.');
    for (const candidate of [workload, split]) {
      let covered = 0;
      for (const batch of candidate.batches) {
        assert.equal(batch.firstInstance, covered);
        for (let index = 0; index < batch.count; index++) assert.equal(candidate.textures[covered + index], batch.texture);
        covered += batch.count;
      }
      assert.equal(covered, candidate.count);
    }
    assert.ok(split.batches.every(batch => batch.count <= 7));
    assert.ok(workload.primitives.every(Number.isFinite));
    for (let index = 0; index < workload.count; index++) {
      const p = workload.primitives.subarray(index * 32, (index + 1) * 32);
      assert.ok(p[24] >= p[8] - .001 && p[25] >= p[9] - .001);
      assert.ok(p[24] + p[26] <= p[10] + .001 && p[25] + p[27] <= p[11] + .001);
      assert.ok(p[26] > 0 && p[27] > 0, 'Each emitted primitive has visible clipped geometry.');
      assert.ok(p[4] <= p[7] && p[5] <= p[7] && p[6] <= p[7], 'Colors must be premultiplied.');
      if (p[12] === 5) assert.equal(p[13], -1, 'Production analytic pies are filled; strokes use coverage masks.');
      if (p[12] === 2 && p[13] >= 0) {
        assert.equal(p[16], 1); assert.equal(p[17], 1);
        assert.ok(p[18] >= 10 && p[18] <= 24, 'Ellipse strokes must use the production bounded solve budget.');
      }
    }
  }
  const mixed = buildWorkload('mixed', config);
  assert.ok(mixed.kindCounts.every(count => count > 0), 'Mixed fixture must exercise all seven shader kinds.');
  assert.ok(mixed.glyphCount > 0 && mixed.imageCount > 0);
  assert.ok(mixed.batches.some(batch => batch.firstInstance > 0));
  const shapes = buildWorkload('shapes', config).primitives;
  assert.ok(Array.from({ length: config.instances }, (_, index) => index * 32).some(offset => shapes[offset + 12] === 2 && shapes[offset + 13] > 0),
    'The workload must exercise ellipse strokes, not only cheaper filled ellipses.');
});

test('resident texture fixtures have correct mip dimensions and premultiplied pixels', () => {
  for (let texture = 0; texture < 5; texture++) {
    const levels = textureFixture(texture);
    assert.equal(levels.length, texture < 3 ? 1 : 9);
    for (const [index, level] of levels.entries()) {
      assert.equal(level.width, Math.max(1, levels[0].width >> index));
      assert.equal(level.height, Math.max(1, levels[0].height >> index));
      assert.equal(level.bytes.length, level.width * level.height * 4);
      for (let offset = 0; offset < level.bytes.length; offset += 4)
        assert.ok(level.bytes[offset] <= level.bytes[offset + 3] && level.bytes[offset + 1] <= level.bytes[offset + 3] && level.bytes[offset + 2] <= level.bytes[offset + 3]);
    }
  }
});

test('reported percentiles retain raw zeros and never substitute wall time for missing GPU timestamps', () => {
  assert.deepEqual(distribution([4, 1, 3, 2]), { count: 4, min: 1, max: 4, mean: 2.5, p50: 2, p95: 4 });
  assert.throws(() => distribution([NaN])); assert.throws(() => distribution([-1]));
  const base = { cpuPrepareMs: 1, cpuSubmitCallMs: 2, queueCompletionWallMs: 20, postSubmitWaitMs: 17,
    queryReadbackWallMs: 0, gpuRenderPassMs: null, timestampRejection: null };
  assert.equal(summarizeFrames([base]).gpuRenderPassMs, null);
  const measured = summarizeFrames([base, { ...base, gpuRenderPassMs: 0 }, { ...base, gpuRenderPassMs: 1 },
    { ...base, timestampRejection: 'counter reset' }]);
  assert.deepEqual(measured.timestampSamples, { valid: 2, quantizedToZero: 1, rejected: 1 });
  assert.equal(measured.gpuRenderPassMs.p50, 0);
  assert.equal(measured.gpuRenderPassMs.p95, 1);
  assert.equal(measured.queueCompletionWallMs.count, 4);
  assert.deepEqual(decodeTimestampPair(0n, 0n), { gpuRenderPassMs: 0, timestampDeltaNs: '0', timestampRejection: null });
  assert.equal(decodeTimestampPair(1000000000000000000n, 1000000000001234567n).gpuRenderPassMs, 1.234567);
  assert.equal(decodeTimestampPair(100n, 99n).gpuRenderPassMs, null);
  assert.equal(decodeTimestampPair(100n, 99n).timestampDeltaNs, '-1');
  assert.ok(decodeTimestampPair(0n, 2n ** 64n - 1n).timestampRejection);
});
