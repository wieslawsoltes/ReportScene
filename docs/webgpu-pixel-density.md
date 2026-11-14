# Native pixel density for Uno WebGPU surfaces

## Problem

The first Uno 7 migration sized ReportScene's custom `WebGpuCanvasElement` in device-independent pixels (DIPs). Uno 7.0.32 records each visual in its own drawing session, then applies the root display scale when replaying that recording. Its custom WebGPU canvas allocates a target from the recorded size and session matrix without including `XamlRoot.RasterizationScale`.

Consequently, a 2× display could have a correctly sized Uno root canvas while a report or toolbar icon still had one texture pixel per DIP. Compositing enlarged that intermediate texture. Text, analytic shapes, barcode modules and vector icons all lost sharpness at the same boundary; changing their shaders or font hinting would not supply the missing samples.

This behavior is present in the pinned Uno release, not inferred from a future SDK. See [`WebGpuCanvasElement.Paint` in Uno 7.0.32](https://github.com/unoplatform/uno/blob/7.0.32/src/AddIns/Uno.WinUI.Graphics3DWebGpu/WebGpuCanvasElement.cs). The corresponding [`SKCanvasElement`](https://github.com/unoplatform/uno/blob/7.0.32/src/AddIns/Uno.WinUI.Graphics2DSK/SKCanvasElement.cs) already accounts explicitly for root rasterization scale in its software surface path.

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

## Rendering and performance contract

- Report preparation, editing, hit testing, scroll extents and report coordinates remain in their existing units.
- Design, preview and `ReportIcon` use the same corrected host.
- The GPU renderer retains one report render pass, painter order, the production WGSL and the existing premultiplied-alpha contract.
- Glyphs and path masks derive raster resolution from the corrected target dimensions. Existing bounded caches and eighth-step glyph scale quantization remain in effect.
- Target memory and fragment work now reflect the real display density. At 2×, a correctly sampled viewport contains approximately four times the pixels of the previous undersampled target. This is the cost of native resolution, not a measured frame-rate improvement or an extra supersampling pass.
- Viewport culling, shared device resources, warmed glyph reuse and idle rendering suppression remain required.

## Diagnostics and regression evidence

`ReportGpuSurfaceSnapshot` retains its existing positional constructor and adds the requested logical viewport, root rasterization scale and the GPU child's observed presentation bounds. Target dimensions come from the actual `WebGpuCanvasContext`; presentation geometry comes from the arranged visual transform. Tests compare both ends of the mapping rather than accepting intended target dimensions alone.

The focused browser scenarios run the actual Uno application at 1.25× and 2×. They exercise design and prepared preview, zoom, viewport resizing and native scrolling. A normal imported Code128 barcode with text disabled supplies a font-independent hard-edge oracle: its non-antialiased bars should not acquire multi-pixel filtered transitions through the compositor. The scenarios retain their screenshots and geometry with the other browser evidence.

The existing DPR 1 suite and shader pixel thresholds remain unchanged. The complete merged browser gate also requires the new density scenarios. See [validation](validation.md) for the build, browser, package and retained-artifact workflow.
