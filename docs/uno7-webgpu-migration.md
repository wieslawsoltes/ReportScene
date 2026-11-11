# Uno Platform 7 and native WebGPU migration

## Migration status

The browser and desktop heads and all Uno-dependent libraries now target Uno Platform 7. Interactive design, preview and original vector icons draw through `WebGpuCanvasElement` using a new native WebGPU renderer. Report preparation, document editing and export remain shared across the application.

This migration is tracked in [PR #26](https://github.com/wieslawsoltes/ReportScene/pull/26). The baseline is `425c01db1b44f19418a8af408da01ab0dff3010d`. The PR checks and their retained artifacts identify the tested revision for every validation result.

## Versions and project changes

| Component | Before | Migrated |
| --- | --- | --- |
| Uno SDK | 6.7.30 | 7.0.5 |
| Uno runtime | SDK-selected 6.x | 7.0.32, selected by Uno SDK 7.0.5 |
| .NET | .NET 10 | .NET 10; existing SDK roll-forward retained |
| SkiaSharp / SkiaSharp.HarfBuzz | 3.119.2 | 4.151.1 |
| HarfBuzz native assets | Previously independently pinned | 14.4.0, aligned with the Uno SDK |
| Application renderer features | SkiaRenderer;Skia | Skia;WebGpu |
| Custom interactive surfaces | SKCanvasElement | ReportCanvasElement / WebGpuCanvasElement |
| Reusable packages | 13 | 14, including ReportScene.Rendering.WebGpu |

Every Uno library is rebuilt against the same SDK. Desktop and browser builders register `WebGpuBackend.CreateGraphicsProvider()` first, followed by Skia. The second provider keeps native application UI and an explanatory error usable if WebGPU is unavailable; custom report surfaces explicitly require WebGPU and do not silently substitute another display renderer.

Skia remains a deliberate CPU dependency for HarfBuzz-compatible font measurements, glyph/complex-path coverage, image decoding and offline PDF/SVG/PNG encoding. The application does not display an `SKCanvasElement` for report content or icons. This is not a claim that every preprocessing operation executes on the GPU.

The browser loading handoff uses the Uno 7 `Uno.UI.Runtime.WebAssemblyWindowWrapper` namespace. Readiness requires a successfully submitted report GPU frame; an explicit GPU failure also releases the splash so its error can be read.

## Shared report drawing contract

`ReportCanvas`, in the existing rendering package, represents painter-ordered drawing commands and scoped transforms/clips. `ReportRenderer`, indexed page rendering, matrix authoring previews and viewport clipping accept this contract. Existing public `SKCanvas` overloads remain available through the non-owning `SkiaReportCanvas` adapter, preserving exporter and package-consumer compatibility.

The same code still interprets text, pictures, shapes, checkboxes, all existing barcode symbologies, charts, authoring tables/matrices and subreports. Pagination, wrapping, item clipping and source-object diagnostics are unchanged. Text and chart helper scopes restore transforms and clips even when drawing fails.

`DrawDashedRect` makes guide dashes explicit. Its GPU-compatible path emits visible line segments with a continuous perimeter phase; the Skia adapter retains its native dash effect. GPU code does not attempt to inspect opaque native `SKPathEffect` internals.

## Native GPU pipeline

`ReportScene.Rendering.WebGpu` is an independently packable Uno library.

1. `WebGpuReportCanvas` consumes commands, applies the report-to-viewport transform, rejects offscreen primitives and builds an ordered stream.
2. A 128-byte instance stores geometry, premultiplied color, rectangular clip, shape parameters, texture coordinates and the bounded quad.
3. `Report2D.wgsl` generates six vertices per instance and shades rectangles, rounded rectangles, ellipses, lines, pie interiors, texture coverage and grids.
4. Adjacent primitives using the same texture form a draw batch. No global texture sorting changes report painter order or transparency.
5. One render pass writes directly to the target provided by Uno. The application does not create a second GPU device, copy a complete page through a CPU bitmap, or read pixels back during normal rendering.

The backend uses Uno's `WebGpuCanvasContext.Api`, device and queue. Its target format is used verbatim when creating the pipeline. Blend factors are `One` and `OneMinusSrcAlpha` for premultiplied output. Device-scoped pipelines and texture caches are shared by icons, design and preview; each viewport owns its submission buffers. Recording and submission serialize shared cache access.

Native render-pass attachments explicitly set `DepthSlice = uint.MaxValue`, the undefined sentinel required for a 2D texture. A zero-initialized C# descriptor otherwise submits a defined depth slice and WebGPU rejects the pass. The application browser tests also reject native uncaptured GPU errors and device loss in the current document, independently of successful queue-submit counters.

### Quality

- Analytic signed-distance coverage provides fractional-pixel antialiasing for geometric edges, avoiding fixed-resolution page rasterization at zoom.
- Pie sectors antialias their radial edges as well as their ellipse boundary. Rectangle outlines preserve miter, bevel and round joins; large outlines use disjoint edge strips to avoid shading transparent interiors or blending corners twice.
- Physical pixel density and zoom determine glyph raster resolution. Raster scales round upward in eighth-unit steps to limit cache churn without undersampling.
- HarfBuzz glyph IDs and positions come from the same metrics owner used by report preparation. Individual characters are not independently reshaped.
- Glyph atlas allocations include two transparent guard pixels, preventing neighboring glyphs from bleeding into filtered samples.
- Glyph ink bounds are tested against the visible clip before rasterization or atlas lookup, including overhang and the filtering guard. Long clipped text cannot pin invisible glyphs and exhaust the visible frame's atlas budget.
- Ordinary glyph coverage remains reusable across text colors. Color-capable OpenType faces retain premultiplied RGBA coverage, including intrinsic palette/bitmap colors and foreground-index layers; only those faces include foreground color in the cache key.
- Images decode into sRGB premultiplied RGBA pixels. Small images use full mip chains with linear mip interpolation; large images select a global mip resolution and upload only visible tiles.
- Mip generation averages premultiplied channels and includes odd-sized source edges.
- Image tiles include four neighboring source pixels on each side, with a second aligned mip. The global tiled chain uses exact 2:1 reductions with edge padding and fractional final UV spans, so selecting the next level cannot shift odd-sized images. Internal quads are disjoint and only the whole-image boundary receives geometric antialiasing, preventing alpha seams.
- Complex contours, joins, caps and path effects retain exact coverage through bounded CPU-generated masks sampled by the GPU. Visible clipping bounds mask allocation. Simple opaque round polylines are direct analytic segments.
- Barcode modules preserve the existing non-antialiased geometric mode.

The renderer preserves the application's color convention; this migration does not introduce an HDR or wide-gamut output contract.

Color glyph opacity is applied once to the completed premultiplied raster. A deterministic, original COLR/CPAL fixture verifies both palette and foreground-index layers. SkiaSharp 4.151.1 applies partially transparent paint twice to COLR foreground-index layers when drawing a text blob directly. The interactive renderer corrects this by rasterizing with opaque foreground RGB and then multiplying RGBA by the requested opacity. Direct Skia exports retain their native behavior for that specific color-font case. The fixture and its generator are included under the repository license; no third-party font is redistributed.

### Procedural grid

A visible band's dot grid is one shader primitive, rather than one command per point or a CPU-generated mesh. The CPU computes the same half-open lattice bounds and validates its point budget. The shader evaluates nearest lattice centers in the clipped quad. Diagnostic `Points` remains the logical number of dots; `Submissions` reflects the single primitive.

## Viewport sizing and lifecycle

The outer `ReportCanvasElement` keeps the logical page extent and pointer coordinate system. Its translated GPU child covers only the visible DIP rectangle. Scrolling changes the child position and initial drawing translation; it does not allocate a full-document render target. This matters particularly for long reports and 400% zoom.

Content, scrolling and native layout changes coalesce through the dispatcher. Empty or unavailable layout defers rendering. There is no idle render loop. Existing prepared-page indexes and authoring layouts remain reusable through viewport changes.

Initialization failures release partially created resources. Unload/reload and disposal release owned buffers, bindings, textures and pipelines; Uno's borrowed device/queue/API are never disposed by this renderer. GPU references are released rather than forcibly destroyed while previous submitted work may still hold them.

Errors are visible in the native UI, including the unsupported-WebGPU path. Browser automation must distinguish an actual GPU submission from a render callback or a loading-screen handoff.

## Resource bounds

| Resource | Bound / behavior |
| --- | --- |
| Glyph atlas | Four 2048 × 2048 RGBA pages; 64 MiB |
| Glyph key count | 65,536 entries |
| Images and complex-path masks | Shared 256 MiB budget, excluding CPU decode/staging buffers |
| Image cache | 1,024 entries |
| Image tile interior | At most 2048 × 2048 pixels, reduced when the device limit requires it |
| Image decode/staging | 32 megapixel source limit; one retained global mip (up to 128 MB), with a smaller next mip during downsampling |
| Complex-path cache | 2,048 entries |
| Shape-run cache | 2,048 runs and 131,072 retained text/family characters per metrics owner |
| Primitive stream | At most 262,144 instances, 32 MiB before batching metadata |
| Procedural grid | Existing 1,048,576 visible-point default |
| Texture dimensions | Validated against the actual device limit |
| GPU target | Visible viewport in physical pixels |

Resources referenced by the current recording are pinned until submission. Cold resources can be evicted between recordings; the queue retains prior submitted references. Oversized content produces an object or renderer diagnostic rather than unbounded allocation. These are safety bounds, not expected steady-state memory consumption.

Large image dimensions can exceed the device's texture limit because each visible tile stays within it. The shared GPU image budget covers both complete images and tiles. CPU decode/staging is accounted separately from GPU residency: the largest permitted source plus next mip can approach 192 MB for a one-pixel-wide image, with about 21 MB of tile staging. The encoded-image/native decoder cache is also separate. Very long, narrow sources use widened arithmetic when computing mip partitions, even though their total area is bounded.

A path requiring an inverse fill or image/mask filter needs an explicit effect pass. Current ReportScene objects do not request those features.

## Diagnostics

The design and preview views expose `GpuSnapshot` and `RenderingError`. Designer snapshots include `DesignGpu` and `PreviewGpu`. Evidence includes:

- native initialization state and `Waiting`, `WebGPU`, `Unavailable`, `Failed` or `Disposed` state;
- submitted frame count;
- target pixel dimensions and visible DIP bounds;
- CPU record/upload/submission duration;
- primitive count, draw-call count, texture uploads, uploaded bytes and atlas-page count;
- device texture dimension limit, tile size, retained image/mask bytes and CPU image staging bytes.

These counters describe CPU-side submission work. They are not GPU execution timings, compositor presentation acknowledgements, hardware acceleration certification, or frames-per-second benchmarks. Texture upload counters exclude shared resource initialization outside that frame.

## Validation

The initial drawing-contract commit `31eeda45c3201a080f80bc8c7bcfe14c5016e401` passed the GitHub core conformance job, differential benchmarks, all 54 isolated core package-consumer checks, the desktop build and the Uno package-consumer build. This proves the compatibility refactor against the baseline dependencies; it is not a substitute for the full migration checks.

The full Uno 7 / Skia 4 commit `5f35f5fe41490fa9bfd42e9a11dc262ea683485a` passed core conformance, benchmarks, all 54 core package checks, the desktop build, all 14 reusable packages, browser publication and all 37 original production-shader pixel assertions in [run 43109136759](https://github.com/wieslawsoltes/ReportScene/actions/runs/43109136759). Skia 4 typeface identity and immutable path-builder changes and Uno 7 layout signatures are integrated. Subsequent actual-application evidence identified the Chromium software-presentation configuration and native 2D depth-slice sentinel issues described above; both have explicit fixes and stronger validation gates.

The expanded core suite includes 850 checks, with eight image staging, color and mip-transition checks and two deterministic glyph-coverage checks. Browser conformance executes the platform-applicable subset; its recorded count comes from the actual WebAssembly application. The complete application gate requires all 77 scenarios across eight shards. Existing browser pixel assertions, required scenario names and the zero-retry policy remain mandatory. See [validation and reproducibility](validation.md) for commands, package checks and artifact contents, and the [PR checks](https://github.com/wieslawsoltes/ReportScene/pull/26/checks) for current revision-specific results.

The migration runs the same gates against Uno 7 / Skia 4, plus:

- an independent WGSL compile/pipeline/readback job with 39 pixel assertions over 15 primitives and two ordered draw calls, including nonzero `firstInstance`, a fractional pie radial edge and a mitered rectangle corner;
- the same production shader rendered into an actual `GPUCanvasContext`, requiring exact offscreen/canvas byte equality and matching browser screenshot samples;
- native browser evidence for successful WebGPU submission, viewport-sized targets at high zoom, scroll/layout reuse and idle stability;
- a native browser scenario using a transparent 9001 × 257 image at minified and original pixel sizes, scrolling across tile boundaries and inspecting screenshot pixels and cache bounds;
- a native browser scenario rendering 200pt text at 400% zoom, requiring visible glyph pixels and bounded atlas pages while many distinct glyphs lie outside the text box;
- core raster checks for odd-size mip edges, long/narrow coordinate overflow, premultiplied transparent pixels, adjacent tile gutters, full-patch equivalence across mip transitions, sRGB conversion and invalid input;
- exact premultiplied color-font palette/foreground opacity checks and reusable monochrome coverage checks;
- a browser with WebGPU deliberately unavailable, requiring a visible error, splash release and zero GPU report submissions;
- all existing report editing, preparation, navigation, rendering, import/export and package tests.

The standalone shader test writes `pixels.png`, `presented.png`, `results.json`, `console.json`, and failure diagnostics to its CI artifact. It tests the production shader directly and complements the actual Uno application scenarios. The real-canvas extension passed at commit `fee204018a73d092df6b3b747100a4c45bae47d0` in [run 43111580394](https://github.com/wieslawsoltes/ReportScene/actions/runs/43111580394).

The initial browser failure matched [Chromium issue 575926500](https://issues.chromium.org/issues/575926500): selecting the software WebGPU adapter with incompatible canvas/shared-image flags allowed offscreen checks but lost the device during presentation. CI now selects software Vulkan with `--use-vulkan=swiftshader`, retains software ANGLE, and validates both canvas readback and actual presentation. No application ownership workaround or waived pixel assertion is involved.

### Commands

```sh
dotnet run --project tests/ReportScene.Tests -c Release
dotnet run --project tests/ReportScene.Benchmarks -c Release
bash scripts/pack-core.sh
dotnet build apps/ReportScene.App -c Release -f net10.0-desktop \
  -p:ReportSceneTargetFramework=net10.0-desktop

dotnet workload install wasm-tools
dotnet publish apps/ReportScene.App -c Release -f net10.0-browserwasm \
  -p:ReportSceneTargetFramework=net10.0-browserwasm \
  -p:WasmShellWebAppBasePath=/ReportScene/ -o artifacts/wasm
python3 scripts/stage-browser.py artifacts/wasm artifacts/site artifacts/testsite/ReportScene

npm install --prefix tests/browser
(cd tests/browser && npx playwright install chromium)
node --test tests/browser/webgpu-shader.test.mjs
python3 -m http.server 8000 --directory artifacts/testsite
# In another terminal:
(cd tests/browser && npx playwright test)
```

CI uses SwiftShader software WebGPU for reproducible behavioral/pixel validation. Representative hardware measurements remain necessary before publishing GPU speedups or a broad visual-quality acceptance claim. A hardware acceptance pass should cover browser/device pixel ratios, macOS/Windows/Linux desktop adapters, large photo reports, zoomed text, dense barcodes, long pages, device loss and repeated load/unload.

The original local execution environment disconnected during dependency restore. Subsequent edits were preserved through GitHub and validated by Actions; local build claims are not inferred from those remote results.

## Upstream references

- [Uno Platform 7 migration guide](https://platform.uno/docs/articles/migrating-to-uno-7.html)
- [Uno 7 package and feature migration](https://platform.uno/docs/articles/migrating-to-uno-7/packages.html)
- [WebGPU rendering backend](https://platform.uno/docs/articles/features/using-webgpu-rendering.html)
- [WebGpuCanvasElement](https://platform.uno/docs/articles/controls/WebGpuCanvasElement.html)
- [WebGpuCanvasContext ownership contract](https://github.com/unoplatform/uno/blob/master/src/AddIns/Uno.WinUI.Graphics3DWebGpu/WebGpuCanvasContext.cs)
