diagnostic(off, derivative_uniformity);
// One ordered instanced quad stream. All coordinates are viewport-local DIPs.
// Colors and texture pixels are premultiplied, matching Uno's compositor target.
struct Frame {
    size: vec2f,
    pixels_per_dip: vec2f,
};
struct Primitive {
    rect: vec4f,
    color: vec4f,
    clip: vec4f,
    shape: vec4f, // kind, half stroke (-1 fill), antialias, cap
    data: vec4f,
    uv: vec4f,
    quad: vec4f,
    extra: vec4f,
};
@group(0) @binding(0) var<uniform> frame: Frame;
@group(0) @binding(1) var<storage, read> primitives: array<Primitive>;
@group(1) @binding(0) var image_sampler: sampler;
@group(1) @binding(1) var image_texture: texture_2d<f32>;
struct VertexOutput {
    @builtin(position) position: vec4f,
    @location(0) point: vec2f,
    @location(1) @interpolate(flat) index: u32,
    @location(2) @interpolate(flat) line_rejection: vec4f, // normal.xy, bias, conservative distance limit (0 disables)
};
@vertex fn vertex_main(@builtin(vertex_index) vertex: u32, @builtin(instance_index) instance: u32) -> VertexOutput {
    let corners = array<vec2f, 6>(
        vec2f(0, 0), vec2f(1, 0), vec2f(0, 1),
        vec2f(0, 1), vec2f(1, 0), vec2f(1, 1));
    let p = primitives[instance];
    let point = p.quad.xy + corners[vertex] * p.quad.zw;
    var line_rejection = vec4f(0);
    if (u32(p.shape.x + 0.5) == 3u) {
        let delta = p.data.zw - p.data.xy;
        let line_length = length(delta);
        // Distant endpoints and degenerate lines retain the complete original
        // distance path. This also bounds cancellation in the rejection plane.
        let coordinate_limit = max(frame.size.x, frame.size.y) * 4;
        if (line_length >= 0.0001 && line_length < 3.0e38 && line_length <= coordinate_limit
            && all(abs(p.data) <= vec4f(coordinate_limit))) {
            let tangent = delta / line_length;
            // The segment fields are 1-Lipschitz. Possible AA coverage is at
            // most .5*(1/ppd.x+1/ppd.y) from the stroke. Four physical pixels
            // additionally retain every neighboring 2x2 derivative invocation
            // and float32 roundoff. No potentially visible lane uses this exit.
            let limit = max(p.shape.y, 0.0001) + 4 / min(frame.pixels_per_dip.x, frame.pixels_per_dip.y);
            let projected_length = dot(p.quad.zw, abs(tangent));
            // Enable only when the conservative strip can avoid distance work;
            // short, thick, axial and mostly clipped lines need no extra test.
            if (2 * limit * projected_length < p.quad.z * p.quad.w) {
                let normal = vec2f(-tangent.y, tangent.x);
                line_rejection = vec4f(normal, -dot(p.data.xy, normal), limit);
            }
        }
    }
    var output: VertexOutput;
    output.position = vec4f(point.x / frame.size.x * 2 - 1, 1 - point.y / frame.size.y * 2, 0, 1);
    output.point = point;
    output.index = instance;
    output.line_rejection = line_rejection;
    return output;
}
fn box_distance(p: vec2f, half_size: vec2f) -> f32 {
    let q = abs(p) - half_size;
    return length(max(q, vec2f(0))) + min(max(q.x, q.y), 0);
}
fn ellipse_implicit(point: vec2f, radius: vec2f) -> f32 {
    let n = point / max(radius, vec2f(0.0001));
    return dot(n, n) - 1;
}
fn ellipse_boundary_approximation(point: vec2f, radius: vec2f) -> f32 {
    // Only used near a filled pie's zero contour, never to offset a stroke.
    let r = max(radius, vec2f(0.0001));
    let n = point / r;
    return (dot(n, n) - 1) / max(length(2 * n / r), 0.0001);
}
fn ellipse_stroke_distance(point: vec2f, radius: vec2f, half_stroke: f32, iterations: u32) -> f32 {
    let r = max(radius, vec2f(1.17549435e-38));
    // A collapsed inner contour is not a boundary. In particular h == r must
    // give a solid circle, with no half-covered pixel at its medial point.
    let has_hole = half_stroke < min(r.x, r.y);
    if (r.x == r.y) {
        // Normalize before squaring: a large uniform transform can make the
        // local radius tiny (or huge) while its projected size is ordinary.
        let magnitude = max(r.x, max(abs(point.x), abs(point.y)));
        let distance = magnitude * length(point / magnitude) - r.x;
        return select(distance, abs(distance), has_hole) - half_stroke;
    }

    // The nearest point lies in the query's quadrant (Eberly, Distance from a
    // Point to an Ellipse, sections 2.1/2.5). Sort a >= b and normalize by a to
    // keep coefficients finite even for very eccentric ellipses. Parameterize
    // the boundary by t = tan(theta/2), 0 <= t <= 1. The normal equation becomes
    // B*t^4 + 2*(D+A)*t^3 + 2*(A-D)*t - B = 0, where
    // A=x/a, B=b*y/a^2, D=1-b^2/a^2. Its second derivative is nonnegative;
    // starting at -B it has one positive crossing, including the major-axis
    // medial case where t=0 is a different, nonminimal stationary point.
    // Bisection has no trig, division, or unbounded convergence in its loop.
    // The CPU targets a 1/1024-pixel bracket with 10..24 iterations. At the cap,
    // the bound remains projected_radius / 2^24, plus float32 rounding.
    // https://www.geometrictools.com/Documentation/DistancePointEllipseEllipsoid.pdf
    let swap = r.x < r.y;
    let radii = select(r, r.yx, swap);
    let query = select(abs(point), abs(point.yx), swap);
    let ratio = radii.y / radii.x;
    let d = 1 - ratio * ratio;
    let a = query.x / radii.x;
    let b = ratio * (query.y / radii.x);
    var lower = 0.0;
    var upper = 1.0;
    for (var i = 0u; i < min(iterations, 24u); i++) {
        let t = (lower + upper) * 0.5;
        let value = ((b * t + 2 * (d + a)) * t * t + 2 * (a - d)) * t - b;
        if (value > 0) { upper = t; } else { lower = t; }
    }
    let t = (lower + upper) * 0.5;
    let tt = t * t;
    let closest = vec2f(1 - tt, ratio * 2 * t) / (1 + tt);
    let unsigned_distance = radii.x * length(query / radii.x - closest);
    let normalized = query / radii;
    let inside = dot(normalized, normalized) < 1;
    return select(unsigned_distance, -unsigned_distance, inside && !has_hole) - half_stroke;
}
fn coverage(distance: f32, aa: f32) -> f32 {
    if (aa < 0.5) { return select(0.0, 1.0, distance <= 0); }
    let width = max(fwidth(distance), 0.0001);
    return clamp(0.5 - distance / width, 0, 1);
}
fn ellipse_coverage(distance: f32, aa: f32) -> f32 {
    if (aa < 0.5) { return select(0.0, 1.0, distance <= 0); }
    // Ellipse fields are dimensionless; a DIP-sized derivative floor would
    // change coverage when only the original coordinate scale changes.
    return clamp(0.5 - distance / max(fwidth(distance), 1e-20), 0, 1);
}
fn segment_distance(point: vec2f, a: vec2f, b: vec2f, half_width: f32, cap: f32) -> f32 {
    let delta = b - a;
    let len = length(delta);
    if (len < 0.0001) {
        if (cap < 0.5) { return 100000.0; }
        return select(box_distance(point - a, vec2f(half_width)), length(point - a) - half_width, cap > 1.5);
    }
    let tangent = delta / len;
    let local = vec2f(dot(point - (a + b) * 0.5, tangent), dot(point - a, vec2f(-tangent.y, tangent.x)));
    if (cap > 1.5) { // round
        return length(point - (a + delta * clamp(dot(point - a, delta) / dot(delta, delta), 0, 1))) - half_width;
    }
    let extension = select(0.0, half_width, cap > 0.5); // square
    return box_distance(local, vec2f(len * 0.5 + extension, half_width));
}
@fragment fn fragment_main(input: VertexOutput) -> @location(0) vec4f {
    let point = input.point;
    // Keep the original quad and point interpolation bit-for-bit. Discard only
    // guaranteed transparent portions of large diagonal-line bounds, before
    // loading the primitive or evaluating distance/blending. Neighboring lanes of
    // any potentially covered fragment retain the unmodified distance path.
    if (input.line_rejection.w > 0
        && abs(dot(point, input.line_rejection.xy) + input.line_rejection.z) > input.line_rejection.w) {
        // Discard only demotes to a helper invocation; explicitly return to
        // avoid continuing through storage reads and the distance calculation.
        // https://www.w3.org/TR/WGSL/#discard-statement
        discard;
        return vec4f(0);
    }
    let p = primitives[input.index];
    // Exact per-primitive clips preserve painter order without scissor state churn.
    if (point.x < p.clip.x || point.y < p.clip.y || point.x >= p.clip.z || point.y >= p.clip.w) { discard; }
    let center = p.rect.xy + p.rect.zw * 0.5;
    let half_size = p.rect.zw * 0.5;
    let local = point - center;
    let kind = u32(p.shape.x + 0.5);
    var distance = box_distance(local, half_size);
    var color = p.color;
    if (kind == 1u) {
        let radius = min(p.data.x, min(half_size.x, half_size.y));
        distance = box_distance(local, max(half_size - vec2f(radius), vec2f(0))) - radius;
    } else if (kind == 2u) {
        if (p.shape.y < 0) {
            // fwidth makes implicit coverage scale-independent at the boundary.
            // Unlike F/|grad F|, F has no pole at a pixel-aligned ellipse center.
            distance = ellipse_implicit(local, half_size);
        } else {
            // Work in the original stroke metric, then let derivatives account
            // for its transform. Averaging two scales changes oval thickness.
            let local_per_dip = select(vec2f(1), p.data.xy, p.data.xy > vec2f(0));
            let iterations = u32(select(24.0, clamp(p.data.z, 10, 24), p.data.z > 0));
            let metric_radius = half_size * local_per_dip;
            let unit = max(max(metric_radius.x, metric_radius.y), p.shape.y);
            distance = ellipse_stroke_distance((local * local_per_dip) / unit,
                metric_radius / unit, p.shape.y / unit, iterations);
        }
        return color * ellipse_coverage(distance, p.shape.z);
    } else if (kind == 3u) {
        distance = segment_distance(point, p.data.xy, p.data.zw, max(p.shape.y, 0.0001), p.shape.w);
    } else if (kind == 4u) {
        if (p.extra.z > 0.5) {
            // Tiled image quads share one outer coverage boundary. Internal
            // edges remain fully covered and are emitted exactly once.
            distance = box_distance(point - p.data.xy - p.data.zw * 0.5, p.data.zw * 0.5);
        }
        let ratio = (point - p.rect.xy) / max(p.rect.zw, vec2f(0.0001));
        let uv = mix(p.uv.xy, p.uv.zw, ratio);
        // Explicit gradients keep derivative use uniform across shape branches.
        let uv_dx = vec2f((p.uv.z - p.uv.x) / max(p.rect.z, 0.0001) / frame.pixels_per_dip.x, 0);
        let uv_dy = vec2f(0, (p.uv.w - p.uv.y) / max(p.rect.w, 0.0001) / frame.pixels_per_dip.y);
        let sample = textureSampleGrad(image_texture, image_sampler, uv, uv_dx, uv_dy);
        color = select(sample * p.color, p.color * sample.a, p.extra.w > 0.5);
    } else if (kind == 5u) {
        distance = ellipse_boundary_approximation(local, half_size);
        let angle = atan2(local.y / max(half_size.y, 0.0001), local.x / max(half_size.x, 0.0001));
        let tau = 6.28318530718;
        let relative = angle - p.data.x;
        let turn = relative - floor(relative / tau) * tau;
        if (p.data.y < tau - 0.0001) {
            let start_ray = vec2f(cos(p.data.x), sin(p.data.x)) * half_size;
            let end_ray = vec2f(cos(p.data.x + p.data.y), sin(p.data.x + p.data.y)) * half_size;
            let start_point = start_ray * clamp(dot(local, start_ray) / max(dot(start_ray, start_ray), 0.0001), 0, 1);
            let end_point = end_ray * clamp(dot(local, end_ray) / max(dot(end_ray, end_ray), 0.0001), 0, 1);
            let edge_distance = min(length(local - start_point), length(local - end_point));
            let angular_distance = select(edge_distance, -edge_distance, turn <= p.data.y);
            distance = max(distance, angular_distance);
        }
    } else if (kind == 6u) {
        // An entire visible dot lattice is one primitive, independent of dot count.
        let cell = (point - p.data.xy) / p.data.zw;
        let nearest = p.data.xy + floor(cell + 0.5) * p.data.zw;
        distance = box_distance(point - nearest, max(p.extra.xy * 0.5, vec2f(0.0001)));
        return color * coverage(distance, p.shape.z);
    }
    if (kind == 0u && p.shape.y >= 0 && p.shape.w < 1.5) {
        let half_stroke = p.shape.y;
        var outer = box_distance(local, half_size + vec2f(half_stroke));
        if (p.shape.w > 0.5) {
            let corner = abs(local) - half_size;
            outer = max(outer, (corner.x + corner.y - half_stroke) * 0.70710678118);
        }
        distance = outer;
        if (all(half_size > vec2f(half_stroke))) {
            distance = max(outer, -box_distance(local, half_size - vec2f(half_stroke)));
        }
    } else if (kind != 2u && kind != 3u && kind != 4u && p.shape.y >= 0) {
        distance = abs(distance) - p.shape.y;
    }
    return color * coverage(distance, p.shape.z);
}
