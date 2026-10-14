# Authoring grid batching and readiness review

Read-only inspection of retained failure run 38352955973 (review run 38381339763) found a 43-second browser-side screenshot decode, a 27-second scalar evaluation and a later 49-second scalar evaluation. The diagnostic sequence advanced from 32 to 33 after the assertion deadline, without a diagnostic exception. The evidence establishes stalled browser responsiveness, not a stopped timer or a color-editor defect; it does not identify the complete CPU/GPU/scheduler cause. All four new color scenarios passed, but the run failed an existing authoring viewport scenario and was not accepted.

## Concrete production work reduction

The authoring grid previously submitted each visible dot with a separate managed-to-native `DrawPoint` call. The 2000-by-1800-point, four-point lattice can generate 225,000 such calls per paint. The actual designer now uses reusable `ReportGridPainter` to send the same points in row-major batches of at most 4096. That fixture takes 55 native submissions; it still contains all 225,000 points. Spacing, opacity, stroke width, float conversion, origin, clipping selection and ruler/object painter order are unchanged. Grid drawing is not disabled or coarsened at low zoom.

The reusable painter retains at most 8191 points (65,528 coordinate bytes excluding array headers): one 4096-point buffer and one exact-size final batch. Repeated equal-size tails are reused; changing tail size replaces rather than accumulates buffers. Clear releases them, and disposing the design control invokes Clear. No report/canvas/paint is retained. This is UI/render-thread-owned state, not a concurrent API.

The public API requires finite renderer-representable geometry and spacing of at least four report points, matching the existing designer minimum. A default per-call budget of 1,048,576 points and explicit cancellation prevent unbounded input from looping silently. Exceeding the budget is diagnosed, never silently thinned. Normal visible-grid bounds are still supplied by the native ScrollViewer geometry, not the recording clip.

## Validation scope

Nine new shared native/WebAssembly cases compare exact RGBA raster bytes against the previous individual-dot loop at 15%, 50%, 85%, 100% and 400% zoom, including fractional origin and semitransparent paint. Other cases check the 225,000-point / 55-submission fixture, bounded buffers, empty/disjoint queries, tail reuse, invalid inputs and cancellation. The independent package-only Uno consumer compiles the generic painter API. The existing 61 browser scenarios and eight required shards remain mandatory; the recurring authoring scenario retains its original pixel assertions and timeout.

Fewer native calls are a deterministic work reduction, not proof that every browser stall is fixed and not a hardware-GPU, FPS or input-latency measurement. Current-head full CI must complete before merge. The temporary retained-trace review workflow is absent from the final tree.

Primary reference: SkiaSharp SKCanvas.DrawPoints and DrawPoint implementation in binding/SkiaSharp/SKCanvas.cs. No external renderer or report export algorithm is replaced.
