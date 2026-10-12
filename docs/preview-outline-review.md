# Prepared bookmarks and two-axis preview navigation

The preview now offers a searchable, resizable **Outline** panel with page-grouped bookmark occurrences and click/keyboard navigation. It builds on the native viewer's existing page navigator rather than preparing a second report or rendering a JavaScript mock. This is an independent implementation of ordinary report-viewing behavior; it is not the complete FastReport outline engine or pixel-identical viewer.

## Metadata and ownership

`PreparedOutlineSnapshot` makes one pass over prepared pages/items and retains only immutable bookmark metadata: ordinal page/item positions, physical page caption, source identifier, label and visible rectangle. Labels and source IDs are not identities. Repeated bookmarks, repeated source objects and duplicate physical page numbers retain distinct occurrences; dictionary keys use the ordinal occurrence. Bookmark rectangles intersect the physical page and the prepared item's clip. Whitespace, completely clipped and off-paper bookmarks are omitted. Invalid geometry, cancellation and retention limits fail explicitly rather than silently truncating the outline.

The snapshot is bounded by 100,000 bookmarks and 1,048,576 retained UTF-16 characters, conservatively counting labels, keys and source IDs. Construction scans prepared items even when they have no bookmark; it is not a constant-time operation. It retains no prepared-page references, images, source rows or report objects. Hosts must avoid concurrent mutation during snapshot construction, and replacing the bound report is the supported refresh boundary.

`ReportPreviewOutlineView` is independently reusable and uses native keyed TreeView controls. It groups occurrences under physical page captions; caption strings are not interpreted as hierarchical paths or re-evaluated as expressions. It provides native search, selection, expand/collapse, close and empty-state UI. The integrated preview materializes its metadata only when first opened, reuses it across page changes, filtering, zoom and close/reopen, and clears it when the report is replaced. New targets are bound to their current snapshot instance; a previous report's bookmark object is rejected even if its text/key matches.

The outline width is a bounded view setting, with native pointer/keyboard resizing and cancellation. Preferred width survives temporary viewport clamping, and the panel temporarily collapses when insufficient width remains. These outline-specific settings are in-memory, not additions to the versioned persisted designer preferences. Opening/resizing the panel does not mark report content dirty or build another current-page spatial index. The visible viewport is still recalculated from native layout, so indexed rendering can react to the smaller paper area.

## Revealing the actual target

Previously a search hit scrolled only vertically, so a match on the right side of a wide or highly zoomed page could remain invisible. `ReportPreviewReveal.Calculate` computes minimum horizontal and vertical offsets from actual viewport-relative target geometry. Already visible targets do not move; oversized targets align their leading edges; scroll extents and margins bound the result. Geometry must be finite.

Bookmarks and search now share this reveal path. Navigation first selects the correct displayed page and clips its target. Revealing is scheduled against actual native layout so changed paper dimensions are not confused with desired dimensions. A pending target is retried only in response to layout/loaded events, not an idle timer. New navigation/report replacement cancels old pending targets. Scrolling is nonanimated and retains the current-page index. Native keyboard paging is scoped to the paper ScrollViewer, leaving outline search/tree keys with their own controls.

## Validation

Fourteen added native/WebAssembly conformance cases cover duplicate occurrence identity, clipping, immutability, bounds, cancellation, 10,000 repeated bookmarks and both-axis reveal arithmetic. Four additional isolated core package checks bring the consumer to 47; the independent Uno consumer compiles the new control and navigation APIs.

Three new browser scenarios preserve the previous 46 and raise the aggregate gate to 49. They use actual native pointer, keyboard and file input to check duplicates and empty/replacement states; lazy snapshot and node reuse; pane sizing; and high-zoom offscreen bookmark/search targets. Tests inspect actual element transforms and both scroll offsets, and verify colored report content plus its highlight in real screenshots. Serialized source, dirty state and history must remain unchanged. All eight shards and zero retries/skips/flaky results remain mandatory. Compile/package checks alone are not interactive acceptance; final counts must come from the current-head run.

This increment adds bounded metadata work and avoids rescanning it on ordinary navigation; it does not claim hardware GPU, FPS, peak-memory or whole-application latency improvements. There are no new benchmark speed ratios. The full report still owns its prepared pages; outline metadata is an additional bounded allocation, not streaming preparation.

## Boundaries

Outline entries use existing prepared source bookmarks. Arbitrary script-driven hierarchy, continuous multi-page view, thumbnails, full print/export dialogs, and complete original viewer/designer parity remain outstanding. Rich-text/HTML objects, scripts/events, inheritance, dialog pages, FPX and the complete data-provider/export catalog are unchanged. FRX translation is unchanged. Clipping visible navigation rectangles is not secure redaction.

References: [FastReport PreviewControl outline APIs](https://www.fast-report.com/public_download/docs/FRNet/online/en/api/FastReport.Preview.PreviewControl.html), [native ScrollViewer.ChangeView](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.scrollviewer.changeview?view=windows-app-sdk-1.8).
