# Page properties, text reuse and interaction follow-up

This supplements [Designer fidelity](designer-fidelity.md) with the review that completed page selection, compact band paper and authoring text reuse. The changes are independent implementations informed by the [FastReport designer manual](https://www.fast-report.com/public_download/docs/FRNet/online/en/UserManual/en-US/Fundamentals/TheReport/TheDesigner.html), not pixel-identical certification or copied vendor assets.

## Page properties and paper display

Selecting a page in the object selector or report tree now selects the page itself instead of implicitly selecting its default band. The property grid edits the page name, landscape orientation, paper dimensions, margins, column count and column gap. Geometry editors use the chosen display units; font and border metrics remain explicitly in points. Stored report geometry stays in points.

`DesignerSession.SelectPage()` changes selection only. `UpdatePageProperties()` rejects blank/duplicate page names and invalid paper/margin/column geometry before history or mutation. It retains the band collection; structural edits use their separate API. Page-property edits, undo and redo preserve explicit page selection, and target-page renaming still updates subreport references. Validation of a page-property edit is intentionally scoped to the proposed page settings rather than unrelated existing data errors.

The Uno surface defaults to compact band paper: unused physical paper below the last design band is gray workspace. **View → Rulers → Full paper** restores the physical paper extent. This is only a view preference; it never changes source paper dimensions or prepared pagination. Long bands still extend the authoring paper so their objects are not clipped. The original `DesignLayout.Create(page)` API keeps its full-paper behavior; the two-argument overload selects the view extent explicitly.

## Bounded wrapping results

`TextLayoutCache` is a reusable, single-thread-owned cache of immutable wrapped strings. The authoring surface uses it on redraw. Keys include the input text, point-based width and complete immutable style, so text/width/style edits invalidate correctly while object translation, selection and zoom can reuse the same wrapping. It does not cache whole render surfaces or change the shared renderer/export behavior.

Retention is bounded by 256 entries and 131,072 UTF-16 characters, counting input/output and every style string. Inputs longer than 4,096 characters are wrapped but not retained. FIFO eviction avoids a full-cache reset at capacity. The cache does not own its text measurer; call `Clear()` after changing the measurer's font environment. The Uno surface clears retained entries at disposal. This is not a thread-safe shared global font cache.

Deterministic tests check 1,000 repeated cache reads with no additional measurement calls, width/style/text invalidation, entry/character limits, oversized input, clearing and exceptions. Browser evidence checks actual repaint-driven hits and text-edit invalidation; these counters are not hardware GPU or end-to-end input-latency measurements.

## Interaction defects and test synchronization

An ordinary click on one object in a multi-selection now selects that object after pointer release, while a physical drag inside the selection still moves the complete editable selection. Delaying the collapse until release preserves group dragging. A movement that snaps back is still treated as a drag rather than a click.

The eight-handle test previously read expected preview geometry before the release/commit event had reached the .NET application. It now waits for the drag to finish and undo to become available before checking the transaction. The geometry and history assertions remain. Nonadditive selection helpers explicitly require one selected object.

Native ComboBox containers can be published before their opening geometry has settled. The driver requires the same realized option rectangle across three distinct diagnostic snapshots, then two browser presentation frames, before delivering its real pointer click. It does not invoke selection commands through diagnostics or retry a failed assertion. Sample-selection acceptance remains part of the full browser suite.

Startup diagnostic timeouts now include the application's diagnostic error/fatal/presentation state. This improves failure evidence; it is not a waiver of startup failures. Actual rendered screenshot checks remain mandatory. The system-variable tree also now inserts `ColumnNumber`, a supported expression; a cross-package test evaluates every inserted system-variable expression.

## Evidence boundaries

Nine designer browser scenarios complement the original reporting and delayed-host scenarios. The merged gate requires at least 32 passing scenarios including every new family, zero retries/skips/flaky results and actual WebAssembly conformance. Page-paper testing checks pixels from real browser screenshots as well as metadata, serialized output and undo behavior. Core package-only consumption includes 24 executable checks, and the independent Uno consumer compiles the new surface settings and controls.

Local native conformance is not substituted for CI browser/desktop/package validation. Final commit-specific counts and deployment status are recorded only after the relevant jobs complete. The temporary source-inspection workflow used during review is absent from the final source tree.

Remaining areas include rich-text/HTML reporting, scripts/events, inheritance, dialog pages, FPX, the full provider/office-export catalog, arbitrary docking/floating windows, persisted pane layouts, toolbox drag placement, fully mixed scalar editors, affine group scaling and complete original-designer matching. Metadata snapshots still traverse report structure; aggregates/lookahead and large native selector workloads still require broader profiling. FRX translation is unchanged.
