// Independent double-precision segment-distance reference and deterministic
// rasterization envelopes. These tests bound geometry support, not GPU timings.
const f = Math.fround;
const dot = (a, b) => a[0] * b[0] + a[1] * b[1];
const subtract = (a, b) => [a[0] - b[0], a[1] - b[1]];

function boxDistance(x, y, hx, hy) {
  const qx = Math.abs(x) - hx, qy = Math.abs(y) - hy;
  return Math.hypot(Math.max(qx, 0), Math.max(qy, 0)) + Math.min(Math.max(qx, qy), 0);
}

export function segmentDistance(point, start, end, halfWidth, cap) {
  const delta = subtract(end, start), length = Math.hypot(...delta);
  if (length < .0001) {
    if (cap === 0) return Infinity;
    const offset = subtract(point, start);
    return cap === 1 ? boxDistance(...offset, halfWidth, halfWidth) : Math.hypot(...offset) - halfWidth;
  }
  const tangent = delta.map(value => value / length), normal = [-tangent[1], tangent[0]];
  const local = subtract(point, [(start[0] + end[0]) * .5, (start[1] + end[1]) * .5]);
  const along = dot(local, tangent), across = dot(local, normal);
  if (cap === 2) return Math.hypot(Math.max(Math.abs(along) - length * .5, 0), across) - halfWidth;
  return boxDistance(along, across, length * .5 + (cap === 1 ? halfWidth : 0), halfWidth);
}

function command(start, end, halfWidth, cap, aa, clip, frame, color = [.3, .15, .45, .6]) {
  const rect = [Math.min(start[0], end[0]), Math.min(start[1], end[1]), Math.abs(end[0] - start[0]), Math.abs(end[1] - start[1])];
  const padding = halfWidth + (aa ? 1.5 / Math.min(...frame.pixelsPerDip) : 0);
  const left = Math.max(clip[0], rect[0] - padding), top = Math.max(clip[1], rect[1] - padding);
  const right = Math.min(clip[2], rect[0] + rect[2] + padding), bottom = Math.min(clip[3], rect[1] + rect[3] + padding);
  return Array.from(new Float32Array([
    ...rect, ...color, ...clip, 3, halfWidth, aa ? 1 : 0, cap, ...start, ...end,
    0, 0, 1, 1, left, top, Math.max(0, right - left), Math.max(0, bottom - top), 0, 0, 0, 0
  ]));
}

// Simulate vertex arithmetic at float32 precision for conservative support
// checks. The distance oracle above uses an independent basis/box formulation.
export function rasterEnvelope(primitive, frame) {
  const start = primitive.slice(16, 18), end = primitive.slice(18, 20), quad = primitive.slice(24, 28);
  const delta = end.map((value, axis) => f(value - start[axis]));
  const length = f(Math.sqrt(f(f(delta[0] * delta[0]) + f(delta[1] * delta[1]))));
  const limit = f(Math.max(...frame.size) * 4), halfWidth = Math.max(primitive[13], .0001);
  const original = [[quad[0], quad[1]], [f(quad[0] + quad[2]), quad[1]],
    [f(quad[0] + quad[2]), f(quad[1] + quad[3])], [quad[0], f(quad[1] + quad[3])]];
  const originalArea = quad[2] * quad[3];
  if (!(length >= .0001 && length < 3e38 && length <= limit && [...start, ...end].every(value => Math.abs(value) <= limit)))
    return { oriented: false, corners: original, area: originalArea, originalArea };
  const tangent = delta.map(value => f(value / length)), normal = [-tangent[1], tangent[0]];
  const fringe = f((primitive[14] >= .5 ? 1.5 : .5) / Math.min(...frame.pixelsPerDip));
  const extension = primitive[15] > .5 ? halfWidth : 0;
  const extents = [f(f(f(length * .5) + extension) + fringe), f(halfWidth + fringe)];
  const area = f(f(4 * extents[0]) * extents[1]);
  if (!(area < f(quad[2] * quad[3]))) return { oriented: false, corners: original, area: originalArea, originalArea };
  const center = start.map((value, axis) => f(f(value + end[axis]) * .5));
  const corners = [[-1, -1], [1, -1], [1, 1], [-1, 1]].map(signs => center.map((value, axis) =>
    f(f(value + f(tangent[axis] * f(signs[0] * extents[0]))) + f(normal[axis] * f(signs[1] * extents[1])))));
  return { oriented: true, corners, area, originalArea };
}

export function lineReferenceFrames() {
  return [[1, 1], [1.25, 1.5], [2.75, .75]].map(pixelsPerDip => {
    const width = 512, height = 512, size = [width / pixelsPerDip[0], height / pixelsPerDip[1]];
    const frame = { width, height, size, pixelsPerDip, firstInstance: 3, commands: Array.from({ length: 3 }, () => Array(32).fill(0)), names: [] };
    for (let index = 0; index < 16; index++) {
      const column = index % 4, row = Math.floor(index / 4), tw = size[0] / 4, th = size[1] / 4;
      const origin = [column * tw, row * th], map = p => [origin[0] + p[0] * tw, origin[1] + p[1] * th];
      let start = map([.15, .15]), end = map([.85, .85]), halfWidth = 1.25;
      let cap = index % 3, aa = index < 3 || index >= 6;
      if (index % 2) { start = map([.85, .15]); end = map([.15, .85]); }
      if (index === 6) halfWidth = .5 / Math.max(...pixelsPerDip); // hairline
      if (index === 7) { start = map([.5, .5]); end = [start[0] + .00005, start[1]]; cap = 2; }
      if (index >= 8 && index <= 10) { start = end = map([.5, .5]); cap = index - 8; halfWidth = 8; }
      if (index === 11) { start = map([-.5, -.25]); end = map([1.25, 1.5]); }
      if (index === 12) { start = [-1e6, origin[1] + th * .5]; end = [1e6, start[1]]; }
      if (index === 13) {
        const offset = origin[1] + th * .5 - origin[0] - tw * .5;
        start = [-1e6, -1e6 + offset]; end = [1e6, 1e6 + offset];
      }
      if (index === 14) { start = map([.45, .45]); end = map([.55, .55]); halfWidth = 20; cap = 1; }
      const clip = [...map([.05, .05]), ...map([.95, .95])];
      frame.commands.push(command(start, end, halfWidth, cap, aa, clip, frame));
      frame.names.push(`line-${index}, cap=${cap}, aa=${aa}, dpr=${pixelsPerDip}`);
      if (index === 15) {
        frame.commands.push(command(map([.15, .15]), map([.85, .85]), halfWidth, 1, aa, clip, frame, [.4, .2, .1, .5]));
        frame.names.push(`overlapping line, dpr=${pixelsPerDip}`);
      }
    }
    return frame;
  });
}

export function verifyLineEnvelopeSupport(frames = lineReferenceFrames()) {
  let sampled = 0, supported = 0, oriented = 0, fallback = 0, originalAreaPixels = 0, candidateAreaPixels = 0;
  for (const frame of frames) for (const primitive of frame.commands.slice(frame.firstInstance)) {
    const envelope = rasterEnvelope(primitive, frame), quad = primitive.slice(24, 28), clip = primitive.slice(8, 12);
    if (envelope.oriented) oriented++; else fallback++;
    originalAreaPixels += envelope.originalArea * frame.pixelsPerDip[0] * frame.pixelsPerDip[1];
    candidateAreaPixels += envelope.area * frame.pixelsPerDip[0] * frame.pixelsPerDip[1];
    const halfWidth = Math.max(primitive[13], .0001), aaSupport = primitive[14] >= .5 ? .5 * (1 / frame.pixelsPerDip[0] + 1 / frame.pixelsPerDip[1]) : 0;
    const [minX, minY] = [Math.max(0, Math.floor(quad[0] * frame.pixelsPerDip[0])), Math.max(0, Math.floor(quad[1] * frame.pixelsPerDip[1]))];
    const [maxX, maxY] = [Math.min(frame.width, Math.ceil((quad[0] + quad[2]) * frame.pixelsPerDip[0])), Math.min(frame.height, Math.ceil((quad[1] + quad[3]) * frame.pixelsPerDip[1]))];
    for (let y = minY; y < maxY; y++) for (let x = minX; x < maxX; x++) {
      const point = [(x + .5) / frame.pixelsPerDip[0], (y + .5) / frame.pixelsPerDip[1]];
      if (point[0] < clip[0] || point[1] < clip[1] || point[0] >= clip[2] || point[1] >= clip[3]
          || point[0] < quad[0] || point[1] < quad[1] || point[0] >= quad[0] + quad[2] || point[1] >= quad[1] + quad[3]) continue;
      sampled++;
      const distance = segmentDistance(point, primitive.slice(16, 18), primitive.slice(18, 20), halfWidth, primitive[15]);
      if (distance > aaSupport) continue;
      supported++;
      for (let edge = 0; edge < 4; edge++) {
        const a = envelope.corners[edge], b = envelope.corners[(edge + 1) % 4];
        const cross = (b[0] - a[0]) * (point[1] - a[1]) - (b[1] - a[1]) * (point[0] - a[0]);
        if (cross < -1e-5) throw new Error(`Line envelope omits possible coverage at (${x},${y}), dpr=${frame.pixelsPerDip}, command=${JSON.stringify(primitive)}`);
      }
    }
  }
  return { frames: frames.length, sampled, supported, oriented, fallback, originalAreaPixels, candidateAreaPixels };
}
