// Deterministic GPU input fixtures, not report layout, text shaping or image decoding.
// Keep the eight vec4 fields synchronized with the production WebGpuPrimitive ABI.
import { ellipseIterationBudget } from './ellipse-reference.mjs';

export const primitiveStrideBytes = 128;
export const workloadDefaults = Object.freeze({ shapes: 4096, glyphs: 16384, images: 512, mixed: 8192 });
// Targeted fixtures are opt-in; the established four default workloads and
// their instance bytes remain unchanged for historical comparisons.
export const comparisonWorkloadDefaults = Object.freeze({ 'diagonal-lines': 96, 'ellipse-strokes': 64 });
export const supportedWorkloadDefaults = Object.freeze({ ...workloadDefaults, ...comparisonWorkloadDefaults });
export const fixtureVersion = 1;

export function shaderVariantOrder(repeat, comparison) {
  if (!Number.isInteger(repeat) || repeat < 0) throw new Error('Invalid repeat index.');
  return comparison ? repeat % 2 === 0 ? ['baseline', 'candidate'] : ['candidate', 'baseline'] : ['candidate'];
}

export function comparePixelBytes(baseline, candidate) {
  if (!(baseline instanceof Uint8Array) || !(candidate instanceof Uint8Array)
      || baseline.length !== candidate.length || baseline.length % 4 !== 0)
    throw new Error('Pixel comparisons require equally sized RGBA8 byte arrays.');
  let differingBytes = 0, differingPixels = 0, maximumChannelDifference = 0;
  const examples = [];
  for (let pixel = 0; pixel < baseline.length / 4; pixel++) {
    let differs = false;
    for (let channel = 0; channel < 4; channel++) {
      const offset = pixel * 4 + channel;
      const difference = Math.abs(baseline[offset] - candidate[offset]);
      if (difference) { differingBytes++; differs = true; maximumChannelDifference = Math.max(maximumChannelDifference, difference); }
    }
    if (differs) {
      differingPixels++;
      if (examples.length < 8) examples.push({ pixel, baseline: [...baseline.subarray(pixel * 4, pixel * 4 + 4)],
        candidate: [...candidate.subarray(pixel * 4, pixel * 4 + 4)] });
    }
  }
  return { bytesCompared: baseline.length, differingBytes, differingPixels, maximumChannelDifference, examples };
}

export function compareTrialTimings(baseline, candidate) {
  const fields = ['cpuPrepareMs', 'cpuSubmitCallMs', 'queueCompletionWallMs', 'postSubmitWaitMs', 'queryReadbackWallMs', 'gpuRenderPassMs'];
  return Object.fromEntries(fields.map(field => {
    const before = baseline.statistics[field], after = candidate.statistics[field];
    const ratio = percentile => before?.[percentile] > 0 && after?.[percentile] > 0
      ? before[percentile] / after[percentile] : null;
    return [field, { baseline: before, candidate: after,
      baselineOverCandidateP50: ratio('p50'), baselineOverCandidateP95: ratio('p95') }];
  }));
}

export function distribution(values) {
  if (values.some(value => !Number.isFinite(value) || value < 0))
    throw new Error('Timing samples must be finite, nonnegative milliseconds.');
  if (!values.length) return null;
  const ordered = [...values].sort((a, b) => a - b);
  const percentile = fraction => ordered[Math.max(0, Math.ceil(ordered.length * fraction) - 1)];
  return {
    count: ordered.length, min: ordered[0], max: ordered.at(-1),
    mean: ordered.reduce((sum, value) => sum + value, 0) / ordered.length,
    p50: percentile(.5), p95: percentile(.95)
  };
}

export function summarizeFrames(samples) {
  const fields = ['cpuPrepareMs', 'cpuSubmitCallMs', 'queueCompletionWallMs', 'postSubmitWaitMs', 'queryReadbackWallMs'];
  const result = Object.fromEntries(fields.map(field => [field, distribution(samples.map(sample => sample[field]))]));
  result.gpuRenderPassMs = distribution(samples.map(sample => sample.gpuRenderPassMs).filter(value => value !== null));
  result.timestampSamples = {
    valid: samples.filter(sample => sample.gpuRenderPassMs !== null).length,
    quantizedToZero: samples.filter(sample => sample.gpuRenderPassMs === 0).length,
    rejected: samples.filter(sample => sample.timestampRejection !== null).length
  };
  return result;
}

export function decodeTimestampPair(begin, end) {
  const delta = end - begin;
  const rejected = delta < 0n || delta > BigInt(Number.MAX_SAFE_INTEGER);
  return {
    gpuRenderPassMs: rejected ? null : Number(delta) / 1e6,
    timestampDeltaNs: delta.toString(),
    timestampRejection: rejected ? 'counter reset or unrepresentable timestamp delta' : null
  };
}

export function buildWorkload(name, { width, height, dpr = 1, instances, seed, batchLimit }) {
  if (!Object.hasOwn(supportedWorkloadDefaults, name)) throw new Error('Unknown workload: ' + name);
  const count = instances ?? supportedWorkloadDefaults[name];
  if (!Number.isInteger(count) || count < 1 || count > 262144) throw new Error('Invalid primitive count.');
  if (![width, height, dpr].every(value => Number.isFinite(value) && value > 0) ||
      !Number.isInteger(batchLimit) || batchLimit < 1 || batchLimit > 262144)
    throw new Error('Invalid viewport or batch limit.');
  if (!Number.isInteger(seed) || seed < 0 || seed > 4294967295) throw new Error('Invalid fixture seed.');
  const pixelsX = Math.round(width * dpr) / width, pixelsY = Math.round(height * dpr) / height;
  let randomState = seed >>> 0;
  const random = () => ((randomState = (Math.imul(randomState, 1664525) + 1013904223) >>> 0) / 4294967296);
  if (Object.hasOwn(comparisonWorkloadDefaults, name))
    return buildTargetedWorkload(name, { width, height, pixelsX, pixelsY, count, batchLimit }, random);
  const primitives = new Float32Array(count * primitiveStrideBytes / 4);
  const textures = new Uint8Array(count);
  const kindCounts = Array(7).fill(0);
  const batches = [];
  let glyphCount = 0, imageCount = 0;
  for (let index = 0; index < count; index++) {
    // Mixed report runs retain painter order: shapes, a glyph run, then images.
    const runPosition = index % 64;
    const category = name === 'mixed' ? runPosition < 8 ? 'shapes' : runPosition < 56 ? 'glyphs' : 'images' : name;
    const kind = category === 'shapes' ? [0, 1, 2, 3, 5, 6][index % 6] : 4;
    const scale = .5 + random();
    let w = category === 'glyphs' ? 8 * scale : category === 'images' ? 72 * scale : 28 * scale;
    let h = category === 'glyphs' ? 16 * scale : category === 'images' ? 54 * scale : 22 * scale;
    const x = random() * Math.max(1, width - w), y = random() * Math.max(1, height - h);
    const alpha = .65 + random() * .35;
    const color = category === 'images' ? [alpha, alpha, alpha, alpha] :
      [.1 + random() * .6, .1 + random() * .6, .1 + random() * .6].map(channel => channel * alpha).concat(alpha);
    const clip = index % 7 === 0 ? [x + w * .15, y + h * .1, x + w * .9, y + h * .85] : [0, 0, width, height];
    let stroke = index % 4 === 0 ? 1.25 : -1;
    const data = [0, 0, 0, 0], extra = [0, 0, 0, 0];
    let uv = [0, 0, 1, 1], texture = 0;
    if (kind === 1) data[0] = 4;
    if (kind === 2 && stroke >= 0) {
      // Match the production ellipse-stroke contract at identity layout scale.
      // The nearest-point solve is bounded from the projected physical radius.
      const budget = ellipseIterationBudget(Math.max(w * .5 * pixelsX, h * .5 * pixelsY));
      data.splice(0, 4, 1, 1, budget, 0);
    }
    if (kind === 3) {
      stroke = 1.25;
      data.splice(0, 4, x, y, x + w, y + h);
    }
    if (kind === 5) {
      // Production emits analytic filled pies; curved strokes use coverage masks.
      stroke = -1; data.splice(0, 4, -.3, Math.PI * 1.4, 0, 0);
    }
    if (kind === 6) {
      stroke = -1;
      data.splice(0, 4, x + .5, y + .5, 6, 6);
      extra.splice(0, 4, 1.5, 1.5, 0, 0);
    }
    if (category === 'glyphs') {
      texture = 1 + (Math.floor(index / 48) % 2);
      const cell = index % 256, cellX = cell % 16, cellY = Math.floor(cell / 16);
      // One transparent pixel of padding prevents neighboring fixture masks bleeding.
      uv = [(cellX * 16 + 1) / 256, (cellY * 16 + 1) / 256,
        (cellX * 16 + 15) / 256, (cellY * 16 + 15) / 256];
      extra[3] = 1; stroke = -1; glyphCount++;
    } else if (category === 'images') {
      texture = 3 + (Math.floor(index / 8) % 2);
      stroke = -1; imageCount++;
    }
    const padding = 1.5 + Math.max(0, stroke);
    const left = Math.max(clip[0], x - padding), top = Math.max(clip[1], y - padding);
    const right = Math.min(clip[2], x + w + padding), bottom = Math.min(clip[3], y + h + padding);
    primitives.set([
      x, y, w, h, ...color, ...clip, kind, stroke, 1, index % 3,
      ...data, ...uv, left, top, Math.max(0, right - left), Math.max(0, bottom - top), ...extra
    ], index * 32);
    textures[index] = texture; kindCounts[kind]++;
    const previous = batches.at(-1);
    if (previous && previous.texture === texture && previous.count < batchLimit) previous.count++;
    else batches.push({ texture, firstInstance: index, count: 1 });
  }
  return { primitives, textures, batches, count, kindCounts, glyphCount, imageCount };
}

function buildTargetedWorkload(name, { width, height, pixelsX, pixelsY, count, batchLimit }, random) {
  const primitives = new Float32Array(count * primitiveStrideBytes / 4);
  const textures = new Uint8Array(count), batches = [], kindCounts = Array(7).fill(0);
  const kind = name === 'diagonal-lines' ? 3 : 2;
  for (let index = 0; index < count; index++) {
    let x, y, w, h, data;
    const halfStroke = .5 + random(), antialias = index % 11 === 0 ? 0 : 1;
    if (kind === 3) {
      let ax = width * (.04 + random() * .12), bx = width * (.84 + random() * .12);
      let ay = height * (.04 + random() * .12), by = height * (.84 + random() * .12);
      if (index % 2) [ay, by] = [by, ay];
      if (index % 4 >= 2) { [ax, bx] = [bx, ax]; [ay, by] = [by, ay]; }
      x = Math.min(ax, bx); y = Math.min(ay, by); w = Math.abs(bx - ax); h = Math.abs(by - ay);
      data = [ax, ay, bx, by];
    } else {
      w = width * (.7 + random() * .2); h = height * (.24 + random() * .36);
      x = (width - w) * .5 + width * (random() - .5) * .04;
      y = (height - h) * .5 + height * (random() - .5) * .06;
      data = [1, 1, ellipseIterationBudget(Math.max(w * .5 * pixelsX, h * .5 * pixelsY)), 0];
    }
    const alpha = .25 + random() * .65;
    const color = [.1 + random() * .7, .1 + random() * .7, .1 + random() * .7].map(channel => channel * alpha).concat(alpha);
    const clip = index % 7 === 0 ? [width * .2, height * .15, width * .8, height * .9] : [0, 0, width, height];
    // Rotated square caps extend by h*(|tx|+|ty|) along an axis. Give both
    // revisions the complete conservative input quad, including binary edges.
    const capExpansion = kind === 3 && index % 3 === 1 ? (w + h) / Math.hypot(w, h) : 1;
    const padding = halfStroke * capExpansion + (antialias ? 1.5 / Math.min(pixelsX, pixelsY) : .5 / Math.min(pixelsX, pixelsY));
    const left = Math.max(clip[0], x - padding), top = Math.max(clip[1], y - padding);
    const right = Math.min(clip[2], x + w + padding), bottom = Math.min(clip[3], y + h + padding);
    primitives.set([x, y, w, h, ...color, ...clip, kind, halfStroke, antialias, index % 3,
      ...data, 0, 0, 1, 1, left, top, right - left, bottom - top, 0, 0, 0, 0], index * 32);
    kindCounts[kind]++;
    const previous = batches.at(-1);
    if (previous && previous.count < batchLimit) previous.count++;
    else batches.push({ texture: 0, firstInstance: index, count: 1 });
  }
  return { primitives, textures, batches, count, kindCounts, glyphCount: 0, imageCount: 0 };
}

export function textureFixture(index) {
  if (!Number.isInteger(index) || index < 0 || index > 4) throw new Error('Unknown texture fixture.');
  const size = index === 0 ? 1 : 256;
  let width = size, height = size;
  let bytes = new Uint8Array(width * height * 4);
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
    const offset = (y * width + x) * 4;
    let alpha = 255, color = [255, 255, 255];
    if (index === 1 || index === 2) {
      // Synthetic antialiased coverage masks, deliberately not font rasterization.
      const cx = x % 16, cy = y % 16;
      const bar = Math.min(Math.abs(cx - 4), Math.abs(cx - 11), Math.abs(cy - (index === 1 ? 7 : 10)));
      alpha = cx < 2 || cx > 13 || cy < 2 || cy > 13 ? 0 : Math.round(255 * Math.max(0, Math.min(1, 2.2 - bar)));
    } else if (index > 2) {
      alpha = 160 + ((x + y) % 96);
      color = [x, y, ((x >> 4) ^ (y >> 4)) & 1 ? 220 : 40];
      if (index === 4) color.reverse();
    }
    bytes.set(color.map(channel => Math.round(channel * alpha / 255)).concat(alpha), offset);
  }
  const levels = [{ width, height, bytes }];
  // Match cached mask pages (one level) and resident premultiplied image mip chains.
  if (index < 3) return levels;
  while (width > 1 || height > 1) {
    const nextWidth = Math.max(1, width >> 1), nextHeight = Math.max(1, height >> 1);
    const next = new Uint8Array(nextWidth * nextHeight * 4);
    for (let y = 0; y < nextHeight; y++) for (let x = 0; x < nextWidth; x++) for (let channel = 0; channel < 4; channel++) {
      let sum = 0;
      for (let dy = 0; dy < 2; dy++) for (let dx = 0; dx < 2; dx++)
        sum += bytes[(Math.min(height - 1, y * 2 + dy) * width + Math.min(width - 1, x * 2 + dx)) * 4 + channel];
      next[(y * nextWidth + x) * 4 + channel] = Math.round(sum / 4);
    }
    width = nextWidth; height = nextHeight; bytes = next;
    levels.push({ width, height, bytes });
  }
  return levels;
}
