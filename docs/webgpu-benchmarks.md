# Reproducible WebGPU renderer measurements

`tests/browser/webgpu-benchmark.mjs` measures the production `Report2D.wgsl` pipeline with the same 128-byte primitive layout, bind groups, premultiplied blending and ordered instanced draws as the native renderer. It creates and owns a separate WebGPU device. It does not request features on, modify, or dispose Uno's device.

The runner measures a prepared GPU workload. Report traversal, .NET interop, HarfBuzz shaping, glyph rasterization, image decoding, atlas eviction and application input/presentation latency are outside its scope. The existing .NET differential benchmarks and actual Uno browser scenarios cover complementary behavior; their results should not be substituted for one another.

## Run on a representative adapter

From the repository root:

```sh
npm ci --prefix tests/browser
cd tests/browser
npx playwright install chromium

# Use a reported non-fallback adapter, without forcing a software renderer.
npm run benchmark:webgpu -- --require-hardware --headed \
  --output benchmark-evidence/hardware

# A separate instrumented run requests the optional timestamp feature.
npm run benchmark:webgpu -- --require-hardware --headed \
  --timestamps auto --output benchmark-evidence/hardware-timestamps
```

`--executable /path/to/chrome` or `REPORTSCENE_BROWSER_EXECUTABLE` selects an installed Chromium executable. The default uses Playwright's pinned browser. Normal mode adds no SwiftShader/Vulkan software selection flags. `--require-hardware` rejects reported fallback/software adapters and ambiguous adapter metadata; the JSON retains the raw adapter identification. This classification records what the browser reports and is not independent hardware certification.

The default logical viewport is 1600 × 1000 DIPs at DPR 1. Each workload receives 30 unrecorded warmup frames and 60 measured frames, repeated three times. The workload order rotates between repeats. Every frame waits for `queue.onSubmittedWorkDone()` before the next frame starts. The resulting values describe serialized completion latency with one frame in flight; they are not pipelined throughput or display FPS.

### Workloads and controls

| Workload | Default instances | Input |
| --- | ---: | --- |
| `shapes` | 4,096 | Analytic rectangles, rounded rectangles, filled/stroked ellipses, capped lines, filled pies and procedural grids |
| `glyphs` | 16,384 | Synthetic antialiased masks in two resident atlas textures; no font shaping or rasterization |
| `images` | 512 | Premultiplied image fixtures with complete mip chains in two resident textures |
| `mixed` | 8,192 | Painter-ordered runs of shapes, glyph masks and images |

`--instances` overrides the count for every selected workload. A fixed unsigned seed determines geometry and colors. Adjacent instances with the same texture form batches; textures are never globally sorted. `--batch-limit` splits those batches without changing the primitive stream or painter order. `--uploads instances` includes the 16-byte frame uniform and complete instance upload in each measured frame; `--uploads none` reuses the uploaded buffers. Texture creation and initial uploads are outside the measured frames in both modes.

`--width`, `--height` and `--dpr` control logical and physical dimensions. `--target offscreen` is the default. `--target canvas` acquires a real `GPUCanvasContext` target each frame; queue completion still does not acknowledge that the display presented it. Input validation bounds the viewport, instance count, total frames and wall-clock duration, and checks selected-device limits before allocating the main buffers.

The runner performs an untimed output readback after every trial and rejects an entirely clear viewport. It retains the changed-pixel count and output SHA-256. Detailed edge/color/presentation correctness belongs to `webgpu-shader.test.mjs`; nonblank output alone cannot establish rendering quality.

## Read the timings correctly

| Field | Measured interval |
| --- | --- |
| `cpuPrepareMs` | Optional uniform/instance writes, target acquisition, command encoding and `finish()` |
| `cpuSubmitCallMs` | CPU wall time inside `queue.submit()` |
| `queueCompletionWallMs` | Frame start through queue completion, including CPU work, scheduling, GPU work and callback delivery |
| `postSubmitWaitMs` | Submit return through queue completion callback |
| `gpuRenderPassMs` | Optional begin/end GPU timestamps for the single render pass |
| `queryReadbackWallMs` | Additional timestamp map/readback wall time after queue completion |

CPU and queue wall times are never relabeled as GPU execution times. GPU pass timestamps exclude buffer transfers before the pass, query resolution/copy/readback, CPU encoding and display presentation. Enabling timestamps changes the submitted workload by adding query writes and resolve/copy commands, so compare instrumented runs with other instrumented runs.

`--timestamps off` is the default. `auto` requests `timestamp-query` only if the selected adapter supports it; otherwise the GPU timing fields remain null with an explanation. `required` fails if the feature is unavailable. Timestamp values are nanoseconds with implementation-defined, privacy-coarsened precision. Zero deltas remain in the sample distribution; counter resets or unrepresentable deltas are retained as rejected observations and excluded from GPU percentiles. Missing or rejected samples are not replaced by wall times. These rules follow [WebGPU timestamp query semantics](https://www.w3.org/TR/webgpu/#timestamp-query).

## Evidence and comparisons

Each output directory contains:

- `results.json`: raw frames, per-repeat and pooled nearest-rank p50/p95, min/max/mean, work counts, initial/per-frame upload bytes, shader and harness hashes, instance hashes, output hashes, browser/adapter/features/limits, host information, options and timing definitions;
- `samples.csv`: every measured frame, including timestamp rejection flags;
- `console.json`: browser diagnostics;
- `failure.json` on failure, with the configuration and available progress/error evidence.

A rerun removes those runner-owned result files first, so a failed attempt cannot leave an older successful result under the same output name. Other files in the selected directory are retained.

For a controlled batching comparison, run the same fixture twice with `--batch-limit 1` and the default limit, using separate output directories. Verify equal primitive hashes and output hashes, and record the draw-call difference. Keep the adapter, driver, browser, viewport/DPR, seed/counts, upload and timestamp settings fixed. A shader comparison intentionally changes the shader hash; record that independent variable and hold the remaining inputs constant. Record machine power/thermal conditions and competing workloads alongside the generated evidence. Do not compare a short CI smoke with a hardware run as a performance improvement.

## CI smoke and local contract checks

```sh
npm run test:benchmark --prefix tests/browser
```

The `webgpu-shader` job runs two bounded software configurations after the production pixel suite: all four workloads on an offscreen target with instance uploads and automatic timestamps, then a canvas workload with resident buffers, one instance per draw and timestamps disabled. Both use three measured frames per workload. The `webgpu-benchmark-smoke` artifact retains both output directories. These checks validate execution, timing feature negotiation, output and evidence generation; there is no speed threshold on shared CI runners.

Use `npm run benchmark:webgpu -- --help` from `tests/browser` for the complete option ranges and the standalone software smoke command.
