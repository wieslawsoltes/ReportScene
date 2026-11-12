// Independent double-precision segment-distance reference and deterministic
// rejection planes. These bound coverage and derivative support, not GPU timings.
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

// Simulate only the conservative rejection plane at float32 precision. The
// independent distance oracle above verifies visible support and every 2x2
// derivative neighbor; the GPU gate compares all bytes to the unmodified path.
export function lineRejection(primitive, frame) {
  const start = primitive.slice(16, 18), end = primitive.slice(18, 20), quad = primitive.slice(24, 28);
  const delta = end.map((value, axis) => f(value - start[axis]));
  const length = f(Math.sqrt(f(f(delta[0] * delta[0]) + f(delta[1] * delta[1]))));
  const coordinateLimit = f(Math.max(...frame.size) * 4);
  if (!(length >= .0001 && length < 3e38 && length <= coordinateLimit && [...start, ...end].every(value => Math.abs(value) <= coordinateLimit)))
    return [0, 0, 0, 0];
  const tangent = delta.map(value => f(value / length)), normal = [-tangent[1], tangent[0]];
  const guard = f(Math.max(f(1 / Math.min(...frame.pixelsPerDip)), f(.0001)) + f(coordinateLimit * 2 ** -16));
  const limit = f(Math.max(primitive[13], f(.0001)) + f(4 * guard));
  const projectedLength = f(f(quad[2] * Math.abs(tangent[0])) + f(quad[3] * Math.abs(tangent[1])));
  if (!(f(f(2 * limit) * projectedLength) < f(quad[2] * quad[3]))) return [0, 0, 0, 0];
  return [...normal, -f(f(start[0] * normal[0]) + f(start[1] * normal[1])), limit];
}

const planeDistance = (point, plane) => Math.abs(f(f(f(f(point[0]) * plane[0]) + f(f(point[1]) * plane[1])) + plane[2]));
const rejected = (point, plane) => plane[3] > 0 && planeDistance(point, plane) > plane[3];

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

// Additional CPU bounds cases isolate the derivative floor and large finite
// coordinate arithmetic without changing the established GPU fixture images.
export function lineGuardReferenceFrames() {
  return [[100_000, 100_000], [1e-7, 2e-7]].map(pixelsPerDip => {
    const width = 256, height = 256, size = [width / pixelsPerDip[0], height / pixelsPerDip[1]];
    const frame = { width, height, size, pixelsPerDip, firstInstance: 0, commands: [], names: [] };
    const start = size.map(value => value * .1), end = size.map(value => value * .9);
    const halfWidth = Math.max(.0001, .5 / Math.min(...pixelsPerDip));
    frame.commands.push(command(start, end, halfWidth, 2, true, [0, 0, ...size], frame));
    frame.names.push(`derivative floor / coordinate guard, dpr=${pixelsPerDip}`);
    return frame;
  });
}

export function verifyLineRejectionSupport(frames = lineReferenceFrames()) {
  let sampled = 0, supported = 0, helperChecks = 0, enabled = 0, fallback = 0, rejectedBeforeDistance = 0;
  let unmasked = 0, guardBandMasked = 0;
  for (const frame of frames) for (const primitive of frame.commands.slice(frame.firstInstance)) {
    const plane = lineRejection(primitive, frame), quad = primitive.slice(24, 28), clip = primitive.slice(8, 12);
    if (plane[3] > 0) enabled++; else fallback++;
    const halfWidth = Math.max(primitive[13], f(.0001));
    const innerLimit = f(halfWidth + f(f(plane[3] - halfWidth) * .5));
    const aaSupport = primitive[14] >= .5 ? Math.max(.5 * (1 / frame.pixelsPerDip[0] + 1 / frame.pixelsPerDip[1]), .00005) : 0;
    const start = primitive.slice(16, 18), end = primitive.slice(18, 20);
    const [minX, minY] = [Math.max(0, Math.floor(quad[0] * frame.pixelsPerDip[0])), Math.max(0, Math.floor(quad[1] * frame.pixelsPerDip[1]))];
    const [maxX, maxY] = [Math.min(frame.width, Math.ceil((quad[0] + quad[2]) * frame.pixelsPerDip[0])), Math.min(frame.height, Math.ceil((quad[1] + quad[3]) * frame.pixelsPerDip[1]))];
    for (let y = minY; y < maxY; y++) for (let x = minX; x < maxX; x++) {
      const point = [(x + .5) / frame.pixelsPerDip[0], (y + .5) / frame.pixelsPerDip[1]];
      if (point[0] < clip[0] || point[1] < clip[1] || point[0] >= clip[2] || point[1] >= clip[3]
          || point[0] < quad[0] || point[1] < quad[1] || point[0] >= quad[0] + quad[2] || point[1] >= quad[1] + quad[3]) continue;
      sampled++;
      const exits = rejected(point, plane);
      if (exits) rejectedBeforeDistance++;
      const masked = plane[3] > 0 && planeDistance(point, plane) > innerLimit;
      if (masked && !exits) guardBandMasked++;
      const distance = segmentDistance(point, start, end, halfWidth, primitive[15]);
      if (distance <= aaSupport) {
        supported++;
        if (masked) throw new Error(`Line output mask omits possible coverage at (${x},${y}), dpr=${frame.pixelsPerDip}, command=${JSON.stringify(primitive)}`);
      }
      if (masked) continue;
      unmasked++;
      // Check every neighboring pixel around EVERY unmasked sample, not only
      // potentially covered samples or one assumed alignment of derivative quads.
      // Outer-band derivative artifacts must never reach an unmasked output.
      for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) {
        helperChecks++;
        const helper = [(x + dx + .5) / frame.pixelsPerDip[0], (y + dy + .5) / frame.pixelsPerDip[1]];
        if (rejected(helper, plane)) throw new Error(`Line rejection omits coverage or derivative helper at (${x},${y}), dpr=${frame.pixelsPerDip}, command=${JSON.stringify(primitive)}`);
      }
    }
  }
  return { frames: frames.length, sampled, supported, unmasked, guardBandMasked, helperChecks, enabled, fallback,
    rejectedBeforeDistance, distanceCandidates: sampled - rejectedBeforeDistance };
}
