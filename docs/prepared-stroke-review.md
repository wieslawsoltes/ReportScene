# Prepared stroke visibility

Issue #15 identified a mismatch: the prepared index expanded explicit borders and minimum line strokes, but a checkbox paints its own outline of max(1, side/14) points even when BorderWidth is zero. A 140-point checkbox at (30,30) paints five points outside each edge. Viewports wholly outside its nominal rectangle can therefore still contain real stroke pixels. The linear DrawPage(viewport) path had the same nominal-bounds problem.

## Shared paint bounds

PreparedPaintBounds computes built-in stroke extents using prepared dimensions rather than the source template size. It includes explicit borders, minimum line/checkbox widths and the binary32 stroke width submitted by the renderer. The resulting rectangle is intersected with the page and item clip. Finite nonpositive item clips suppress painting, matching DrawItem; invalid object geometry and non-finite, negative or unrepresentable border widths are rejected rather than silently hiding content.

PreparedPageIndex computes these bounds at construction. Query order, ownership, cancellation and retained current-page indexing remain unchanged. DesignerPaintCulling shares the stroke calculation and adds its own device-independent selection tolerance. The linear DrawPage viewport remains a culling hint, not a new canvas clip; it now uses the same painted extents. Full-page rendering without a viewport is unchanged and supplies the differential reference.

Nominal PreparedVisibility search/bookmark geometry is deliberately unchanged. This is conservative built-in geometric stroke support, not a general effect/filter/custom-object bounds API or a device-scale antialiasing guarantee for every extreme coordinate. No report data format, FRX, pagination or export-format capability is added.

## Evidence

Twelve shared prepared-stroke cases include full-vs-indexed raster equality at all four checkbox edges, magnified minimum widths, prepared/template-size differences, binary32 stroke rounding, explicit borders, clipping, off-paper content, painter order and invalid input. Four more raster cases compare the full renderer with the linear viewport path. Reference fixtures must contain nonwhite ink, so two blank images cannot pass. Existing spatial-index and authoring suites remain intact. The isolated Uno consumer compiles the shared API from generated packages.

A new actual browser scenario imports a report, zooms to 400%, scrolls on both axes using native wheel events, places the nominal checkbox completely left of the viewport and requires green stroke pixels inside the presented viewport. It asserts unchanged report bytes/history/revision and no page-index rebuild from scrolling. The aggregate now requires 52 scenarios across all eight existing shards, with zero retries/skips/flaky results. The earlier authoring scenario and all deadlines remain unchanged.

Main workflow 38278313684 failed a diagnostic-publication wait in the authoring multi-scale scenario and skipped deployment. A requested temporary retained-trace inspection workflow was platform-blocked and was not uploaded or retried through another route. The available log alone does not establish that stall's complete cause, and this stroke fix is not claimed to fix it. Current-head complete browser acceptance is required; previous passing builds are not substituted.

Local filesystem and execution tools were unavailable during this change. Actual test counts, source-tree identities and deployment outcomes must be read from completed CI, not inferred from source review. The full renderer/index comparison is a correctness check, not a measured GPU/FPS/input-latency improvement.

Reference: [Skia paint/stroke API](https://api.skia.org/classSkPaint.html). These are project-owned renderer semantics, not a claim of complete FastReport designer or viewer parity.
