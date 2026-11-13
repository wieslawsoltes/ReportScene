// Independent double-precision oracle for the production WGSL geometry tests.
// This uses the closest-point normal multiplier, not the shader's half-angle
// quartic. See David Eberly, Distance from a Point to an Ellipse, sections 2.5-2.8:
// https://www.geometrictools.com/Documentation/DistancePointEllipseEllipsoid.pdf
export function ellipseIterationBudget(projectedRadius) {
  return Math.min(24, Math.max(10, Math.ceil(Math.log2(Math.max(1, projectedRadius))) + 10));
}

export function ellipseBoundaryDistance(point, radius) {
  let [a, b] = radius;
  let [x, y] = point.map(Math.abs);
  if (a < b) [a, b, x, y] = [b, a, y, x];
  if (a === b) return Math.abs(Math.hypot(x, y) - a);
  if (x === 0) return Math.abs(y - b);
  const difference = a * a - b * b;
  if (y === 0) {
    if (x >= difference / a) return Math.abs(x - a);
    const qx = a * a * x / difference;
    const qy = b * Math.sqrt(Math.max(0, 1 - (qx / a) ** 2));
    return Math.hypot(x - qx, qy);
  }
  // s = multiplier + b^2 avoids cancellation near the major-axis medial set.
  // F(s) = (a*x/(s+a^2-b^2))^2 + (b*y/s)^2 - 1 is strictly decreasing.
  const ax = a * x, by = b * y;
  let lower = by, upper = Math.hypot(ax, by);
  for (let iteration = 0; iteration < 100; iteration++) {
    const s = (lower + upper) / 2;
    if (s === lower || s === upper) break;
    const value = (ax / (s + difference)) ** 2 + (by / s) ** 2 - 1;
    if (value > 0) lower = s; else upper = s;
  }
  const s = (lower + upper) / 2;
  return Math.hypot(x - a * ax / (s + difference), y - b * by / s);
}

export function ellipseStrokeDistance(point, radius, halfStroke) {
  const distance = ellipseBoundaryDistance(point, radius);
  const inside = (point[0] / radius[0]) ** 2 + (point[1] / radius[1]) ** 2 < 1;
  return (inside && halfStroke >= Math.min(...radius) ? -distance : distance) - halfStroke;
}

export function ellipseDistanceCases() {
  const initialSeed = 0x13072026;
  let seed = initialSeed;
  const random = () => {
    seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0;
    return seed / 0x1_0000_0000;
  };
  const cases = [];
  for (let index = 0; index < 4096; index++) {
    // Include axis/medial queries and both axis orderings. Major radii span
    // 0.01..8192 pixels; aspect ratios reach 100,000:1, independently of size.
    let a = 2 ** (-6.643856189774724 + random() * 19.643856189774724);
    let b = a * 10 ** (-5 * random());
    if (index % 16 === 0) b = a;
    let x = a * random() * 2, y = b * random() * 3;
    if (index % 11 === 0) x = 0;
    if (index % 13 === 0) y = 0;
    if (index % 17 === 0) { x = (a * a - b * b) / a; y = 0; }
    const halfStroke = Math.fround(index % 7 === 0 ? b : b * random() * 3);
    if (index % 2 === 0) [a, b, x, y] = [b, a, y, x];
    const radius = [a, b].map(Math.fround), point = [x, y].map(Math.fround);
    const iterations = ellipseIterationBudget(Math.max(...radius));
    const scale = Math.max(...radius, ...point.map(Math.abs));
    const ulp = 2 ** (Math.floor(Math.log2(scale)) - 23);
    cases.push({
      point, radius, halfStroke, iterations,
      expected: ellipseStrokeDistance(point, radius, halfStroke),
      // The geometric bracket plus an explicit float32 rounding allowance.
      // This is an absolute local-coordinate error, not a relative error near 0.
      tolerance: Math.max(...radius) / 2 ** iterations + 8 * ulp
    });
  }
  // A change of local coordinate units must not alter ordinary projected
  // geometry. Squaring these unnormalized values underflows/overflows f32.
  for (const unit of [1e-25, 1e25]) {
    for (const [x, y, b, half] of [[.3, .07, .1, .08], [.7, .22, .1, .2], [0, 0, 1, 2], [3, 0, 1, 2]]) {
      const point = [x * unit, y * unit].map(Math.fround);
      const radius = [unit, b * unit].map(Math.fround);
      const halfStroke = Math.fround(half * unit), iterations = ellipseIterationBudget(1);
      const scale = Math.max(...radius, ...point.map(Math.abs));
      const ulp = 2 ** (Math.floor(Math.log2(scale)) - 23);
      cases.push({ point, radius, halfStroke, iterations, localToPixel: 1 / unit,
        expected: ellipseStrokeDistance(point, radius, halfStroke),
        tolerance: Math.max(...radius) / 2 ** iterations + 8 * ulp });
    }
  }
  return { seed: initialSeed, cases };
}
