import test from 'node:test';
import assert from 'node:assert/strict';
import { parseOptions } from './webgpu-benchmark.mjs';
import { buildWorkload, decodeTimestampPair, distribution, primitiveStrideBytes, summarizeFrames, textureFixture } from './webgpu-benchmark-workloads.mjs';

test('benchmark rejects unbounded or ambiguous configurations before opening a browser', () => {
  const defaults = parseOptions([]);
  assert.equal(defaults.software, false);
  assert.equal(defaults.timestamps, 'off');
  assert.equal(defaults.uploads, 'instances');
  for (const args of [
    ['--software', '--require-hardware'], ['--instances', '0'], ['--instances', '262145'],
    ['--width', '8192', '--height', '8192'], ['--dpr', 'NaN'], ['--samples', '2'],
    ['--warmup', '1000', '--samples', '1000', '--repeats', '10'],
    ['--workloads', 'shapes,shapes'], ['--workloads', 'constructor'], ['--workloads', 'shapes,'],
    ['--timestamps', 'yes'], ['--width', '800', '--width', '800'], ['--unknown']
  ]) assert.throws(() => parseOptions(args), undefined, args.join(' '));
  const smoke = parseOptions(['--software', '--instances', '128', '--width', '640', '--height', '480',
    '--warmup', '2', '--samples', '3', '--repeats', '1', '--timestamps', 'auto']);
  assert.equal(smoke.software, true); assert.equal(smoke.timestamps, 'auto');
  assert.equal(smoke.workloads.length, 4);
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
