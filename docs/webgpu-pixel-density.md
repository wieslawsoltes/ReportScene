# Native pixel density for Uno WebGPU surfaces

## Problem

The first Uno 7 migration sized ReportScene's custom `WebGpuCanvasElement` in device-independent pixels (DIPs). Uno 7.0.32 records each visual in its own drawing session, then applies the root display scale when replaying that recording. Its custom WebGPU canvas allocates a target from the recorded size and session matrix without including `XamlRoot.RasterizationScale`.

Consequently, a 2× display could have a correctly sized Uno root canvas while a report or toolbar icon still had one texture pixel per DIP. Compositing enlarged that intermediate texture. Text, analytic shapes, barcode modules and vector icons all lost sharpness at the same boundary; changing their shaders or font hinting would not supply the missing samples.

This behavior is present in the pinned Uno release, not inferred from a future SDK. See [`WebGpuCanvasElement.Paint` in Uno 7.0.32](https://github.com/unoplatform/uno/blob/7.0.32/src/AddIns/Uno.WinUI.Graphics3DWebGpu/WebGpuCanvasElement.cs). The corresponding [`SKCanvasElement`](https://github.com/unoplatform/uno/blob/7.0.32/src/AddIns/Uno.WinUI.Graphics2DSK/SKCanvasElement.cs) already accounts explicitly for root rasterization scale in its software surface path.

The complete source chain is pinned at Uno commit `78c746d28293975e64ba0f162af6f44f3c3daaab`: [`Visual.PaintStep`](https://github.com/unoplatform/uno/blob/78c746d28293975e64ba0f162af6f44f3c3daaab/src/Uno.UI.Composition/Composition/Visual.skia.cs) starts a recording, [`WebGpuCommandRecorder`](https://github.com/unoplatform/uno/blob/78c746d28293975e64ba0f162af6f44f3c3daaab/src/Uno.UI.Composition.WebGpu/WebGpuCommandRecorder.cs) starts with an identity matrix, and [`WebGpuPresentSession`](https://github.com/unoplatform/uno/blob/78c746d28293975e64ba0f162af6f44f3c3daaab/src/Uno.UI.Composition.WebGpu/WebGpuPresentSession.cs) applies root scale during replay.

## Host adaptation

`ReportCanvasElement` continues to own the logical document extent and pointer coordinates. Its private GPU child uses a physical-pixel-sized layout and an inverse display-scale render transform. An inner `Canvas` keeps that enlarged child from contributing a larger desired size to the surrounding document layout. This uses public Uno APIs and the renderer's existing device, queue and target. The mapping covers the application's translated host surfaces; arbitrary external XAML rotations or non-unit ancestor scaling require a separate transform-aware rasterization policy.

Sizing also accounts for the surface's origin in the root visual. The visible rectangle is expanded outward to physical-pixel boundaries before rendering. The renderer receives that same expanded rectangle, so its logical-to-texture mapping and the compositor's texture-to-screen mapping agree. Rounding width alone would leave fractional translation and a second filtering step.

For display scale `s`, requested local bounds `[x0, x1]`, and local origin `o` in root DIPs, the horizontal mapping is:

```text
leftPixel  = floor((o + x0) * s)
rightPixel = ceil((o + x1) * s)
pixelWidth = rightPixel - leftPixel
viewportX  = leftPixel / s - o
viewportWidth = pixelWidth / s
```

The vertical mapping uses the same calculation. The target covers the visible viewport plus less than one physical pixel per edge. It never expands to the full offscreen report. Invalid scales, non-finite bounds and dimensions that cannot be represented by the native visual are rejected before target allocation.

The host observes root rasterization-scale changes and layout-origin changes. Geometry changes and ordinary content invalidations use the existing coalesced dispatcher path. Unchanged layout does not invalidate the GPU canvas. Root subscriptions are released on unload and disposal.

This is an adaptation for Uno 7.0.32's recording behavior. When upgrading Uno, review its target allocation and visual-recording semantics together with the actual-app density tests. If upstream starts including root scale itself, remove the compensating child layout/transform as part of that upgrade; otherwise the target would be oversampled.

## Browser root canvas

Correct custom targets also need a correctly presented browser root canvas. Uno 7.0.32's [`BrowserRenderer.ts`](https://github.com/unoplatform/uno/blob/78c746d28293975e64ba0f162af6f44f3c3daaab/src/Uno.UI.Runtime.WebAssembly.Browser/ts/Runtime/BrowserRenderer.ts) assigns the document's CSS dimensions multiplied by DPR to integer canvas backing dimensions, while its [`uno.css`](https://github.com/unoplatform/uno/blob/78c746d28293975e64ba0f162af6f44f3c3daaab/src/Uno.UI.Runtime.WebAssembly.Browser/WasmCSS/uno.css) keeps the fixed-position canvas at 100% CSS width and height. At 1537 × 997 DIPs and DPR 1.25, that presents a 1921 × 1246 texture over 1921.25 × 1246.25 physical pixels. The browser filters the entire UI again.

The browser bridge retains Uno's backing allocation and WebGPU context. It gives the root canvas integer CSS dimensions equal to its backing dimensions and applies an inverse-DPR CSS transform about the top-left corner. This avoids fractional CSS layout-size quantization as well as the original stretch. Uno still measures the document element, and its pointer bridge uses client coordinates, so document layout and native input remain in the original DIP coordinate system.

A narrow attribute observer updates presentation after Uno changes backing width/height; resize events account for display-scale changes. Child-list observers on the Uno host and body handle initial attachment, canvas replacement and host replacement without observing the full accessibility tree. Style changes cannot trigger the backing-dimension observer, the fixed canvas cannot alter document measurement, and no polling loop or extra rendering pass is introduced. Actual DOM bounds and CSS transforms are retained in the pixel evidence.

## Rendering and performance contract

- Report preparation, editing, hit testing, scroll extents and report coordinates remain in their existing units.
- Design, preview and `ReportIcon` use the same corrected host.
- The GPU renderer retains one report render pass, painter order, the production WGSL and the existing premultiplied-alpha contract.
- Glyphs and path masks derive raster resolution from the corrected target dimensions. Existing bounded caches and eighth-step glyph scale quantization remain in effect.
- Target memory and fragment work now reflect the real display density. At 2×, a correctly sampled viewport contains approximately four times the pixels of the previous undersampled target. This is the cost of native resolution, not a measured frame-rate improvement or an extra supersampling pass.
- Viewport culling, shared device resources, warmed glyph reuse and idle rendering suppression remain required.

## Diagnostics and regression evidence

`ReportGpuSurfaceSnapshot` retains its existing positional constructor and adds the requested logical viewport, root rasterization scale and the GPU child's observed presentation bounds. Target dimensions come from the actual `WebGpuCanvasContext`; presentation geometry comes from the arranged visual transform. Tests compare both ends of the mapping rather than accepting intended target dimensions alone.

Native scrolling can finish before a coalesced GPU resize is submitted. Capture readiness therefore requires agreement between current requested bounds, the arranged child's size/origin and the last submitted frame, followed by stable diagnostic observations. It never uses screenshot colors to decide when to capture; the first coherent settled frame must satisfy the unchanged pixel assertions.

The focused browser scenarios run the actual Uno application at 1.25× and 2×. They exercise design and prepared preview, zoom, viewport resizing and native scrolling. A normal imported Code128 barcode with text disabled supplies a font-independent hard-edge oracle: its non-antialiased bars should not acquire multi-pixel filtered transitions through the compositor. The scenarios retain their screenshots and geometry with the other browser evidence.

The existing DPR 1 suite and shader pixel thresholds remain unchanged. The complete merged browser gate also requires the new density scenarios. See [validation](validation.md) for the build, browser, package and retained-artifact workflow.

### Reproducing the historical defect

The same density scenarios can run against a separately served historical application without changing their assertions. For the pre-fix `b8911d6a79f3b0f494f8d39401b4d8cfd1d2bc3f` publication, both cases must fail specifically on measured target density after retaining all six phases. The optional validator independently decodes the retained screenshots, reproduces every recorded barcode sample and requires actual filtered edge pixels. Startup failures, input timeouts, missing phases, skipped tests and retries do not count as defect reproduction.

From `tests/browser`, with that historical app served on port 8001:

```sh
mkdir -p ../../artifacts/dpr-baseline
REPORTSCENE_URL=http://127.0.0.1:8001/ReportScene/ \
  npx playwright test webgpu-pixel-density.spec.mjs --reporter=json \
  --output=../../artifacts/dpr-baseline/evidence \
  > ../../artifacts/dpr-baseline/report.json
# The historical invocation above must exit with code 1.
node verify-pixel-density-baseline.mjs ../../artifacts/dpr-baseline/evidence \
  ../../artifacts/dpr-baseline/report.json \
  > ../../artifacts/dpr-baseline/verified-summary.json
```

Historical qualification is separate from the candidate gate, where both density scenarios must pass. Ordinary CI does not depend on an expiring baseline artifact or an external deployed page.

### Recorded baseline

[Qualification run 43598006669](https://github.com/wieslawsoltes/ReportScene/actions/runs/43598006669) served the exact `b8911d6` publication from run 43403424896 and ran the density scenarios at test revision `71ef45d2aa39ab6873200467fb20bfa6cfa3a227`. Both cases completed all six phases and failed specifically on measured target width. The standalone validator independently decoded every retained preview PNG and reproduced its raw barcode samples.

| Historical observation | DPR 1.25 | DPR 2 |
| --- | ---: | ---: |
| Design viewport width | 776 DIP | 776 DIP |
| Actual target width | 776 px | 776 px |
| Required width before outward alignment | 970 px | 1552 px |
| Intermediate barcode samples over 12 scanlines | 2130 | 2496 |
| Black/white transitions on every scanline | 104 | 104 |

The sample totals span four preview phases, three scanlines per phase; they are not counts of unique pixels in one screenshot. Artifact `webgpu-pixel-density-baseline` has SHA-256 `85d506a0b86cf91fa4d43b073c33ac372bd3ddf1a09ec5d3a3e0efa4172b5ac7` and retains the application provenance, test revision, JSON report, screenshots and verified summary.

The first candidate run, [43597738653](https://github.com/wieslawsoltes/ReportScene/actions/runs/43597738653), passed all 79 pre-existing browser scenarios and exposed the separate browser-root stretch during the new odd-size DPR 1.25 resize. Its custom target density was already correct: initial preview had zero intermediate barcode samples, but resize introduced 104 per scanline. The same run exposed a capture taken before a completed DPR 2 resize submission; the following native frame matched the arranged target without further input. These findings led to the browser presentation correction and geometry-based capture readiness above, with no relaxation of the visual thresholds.
