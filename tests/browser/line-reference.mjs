// Deterministic line commands for the exact AABB vertex/interpolation gate.
// Cap, coverage and distance behavior also have pixel and pinned-shader A/B tests.
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
