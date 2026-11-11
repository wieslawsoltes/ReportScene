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
};
@vertex fn vertex_main(@builtin(vertex_index) vertex: u32, @builtin(instance_index) instance: u32) -> VertexOutput {
    let corners = array<vec2f, 6>(
        vec2f(0, 0), vec2f(1, 0), vec2f(0, 1),
        vec2f(0, 1), vec2f(1, 0), vec2f(1, 1));
    let p = primitives[instance];
    let point = p.quad.xy + corners[vertex] * p.quad.zw;
    var output: VertexOutput;
    output.position = vec4f(point.x / frame.size.x * 2 - 1, 1 - point.y / frame.size.y * 2, 0, 1);
    output.point = point;
    output.index = instance;
    return output;
}
fn box_distance(p: vec2f, half_size: vec2f) -> f32 {
    let q = abs(p) - half_size;
    return length(max(q, vec2f(0))) + min(max(q.x, q.y), 0);
}
fn ellipse_distance(point: vec2f, radius: vec2f) -> f32 {
    let r = max(radius, vec2f(0.0001));
    let n = point / r;
    return (dot(n, n) - 1) / max(length(2 * n / r), 0.0001);
}
fn coverage(distance: f32, aa: f32) -> f32 {
    if (aa < 0.5) { return select(0.0, 1.0, distance <= 0); }
    let width = max(fwidth(distance), 0.0001);
    return clamp(0.5 - distance / width, 0, 1);
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
    let p = primitives[input.index];
    let point = input.point;
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
        distance = ellipse_distance(local, half_size);
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
        distance = ellipse_distance(local, half_size);
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
    } else if (kind != 3u && kind != 4u && p.shape.y >= 0) {
        distance = abs(distance) - p.shape.y;
    }
    return color * coverage(distance, p.shape.z);
}
