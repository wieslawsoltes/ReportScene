# Preview navigation and fitting

The actual Uno preview provides First/Previous/Next/Last, validated page entry, keyboard navigation, whole-page fitting and viewport-indexed drawing. The public FastReport API documents corresponding navigation concepts; this is an independent implementation, not a drop-in API or a complete visual-parity claim.

## Reusable pieces

`ReportScene.Model.ReportPagePosition` is an immutable ordinal cursor over a displayed page count. It handles empty/single-page reports, clamps moves at both boundaries and rejects negative counts or unknown commands. Parsing accepts trimmed positive decimal ordinals in range and preserves the exact current position on malformed/overflowing input. The ordinal is not physical `PreparedPage.Number`; search still resolves physical metadata into the displayed page-list index.

`ReportPreviewFit.Calculate` converts page points to preview DIPs with a 40-DIP total gutter. WholePage uses the limiting width/height ratio; Width uses the horizontal ratio. An unmeasured or exhausted viewport returns no zoom request. Geometry must be finite and valid; zoom retains the established 0.1–4 range. Very large paper may not fit at minimum zoom. Fit is an explicit action, not a persistent automatic resize mode.

`ReportScene.Controls.Uno.ReportPageNavigator` supplies native buttons, editable ordinal, page count and inline validation. The host supplies current position and handles `PageRequested`. Disabled boundaries, invalid entries, Escape restoration and same-page requests do not navigate. Unrelated same-position notifications preserve a draft entry; hosts can explicitly reset it when binding another report. The control does not prepare or cache report pages.

`ReportPreviewView.Navigate` uses the ordinal cursor. Clicking the paper focuses its native scroll surface; the preview handles PageUp/PageDown and Ctrl+Home/End before ScrollViewer class handling. Sibling entry/search TextBoxes retain editing keys. The designer synchronizes the navigator after button, keyboard and search-driven page changes. Its single view subscription is removed on disposal rather than accumulating handlers on old ribbon instances.

`ReportPreviewViewport.Calculate` provides a pure, validated conversion from native paper/viewport DIP bounds to the visible rectangle in report points. The host supplies actual transformed bounds. Drawing must not derive that rectangle from a cached recording canvas's LocalClipBounds; the observed right/lower-edge clipping defect and its correction are documented in [the viewport review](preview-viewport-review.md).

## Correctness and ownership

The old page-entry callback silently clamped out-of-range input and directly subtracted one from a parsed integer. Entry now validates the user-facing ordinal before navigation. Zero, negative, fractional, out-of-range and overflowing input leaves the current page/index unchanged and displays recoverable feedback.

Navigation callbacks check both the active control and bound report identity. The identity uses a `WeakReference<PreparedReport>`, not a strong closure capture: removed toolbar callbacks must not retain invalidated report pages. TryGetTarget obtains a temporary reference for the synchronous check. No stale event can navigate a replacement report. This removes a concrete retention path, not a measured peak-memory or exhaustive garbage-collection certification.

Only the current page's spatial index is retained. Fit, zoom, no-op boundary commands and invalid entries do not rebuild it or reprepare reports. Scrolling/size events invalidate the retained viewport drawing; no idle timer, global page cache or report-wide navigation index is added. Navigation and view choices do not dirty report content.

## Validation contracts

Nineteen shared cases cover ordinal/fit/viewport arithmetic, culture, overflow, geometry and boundaries, including two large-cull recording-versus-direct pixel comparisons. The independent core package consumer includes 43 checks, and the Uno consumer compiles the reusable navigator and new preview APIs.

Four native-input scenarios retain the preceding 42 and make 46 required browser scenarios. They cover actual disabled-button state, malformed entry/Escape, search synchronization, keyboard behavior across ribbon rebuilds, two-size whole-page fitting with white paper and colored footer pixels, and real mouse-wheel/400-percent-zoom culling with unchanged prepared index and serialized source. The complete suite runs once across eight isolated one-worker machines; the aggregator requires every shard and rejects retries, skips, flaky results and report errors.

Read current-head CI results for actual acceptance; these contracts are not a claim that an untested revision has passed. Model tests cannot replace the native Uno browser/desktop/package gates. Candidate counters and retained-cache checks do not establish hardware GPU, frame rate or input-latency improvements.

## Remaining scope

Continuous multi-page view, thumbnails, clickable preview outline, vendor-equivalent print/export dialogs and complete designer/viewer parity remain outstanding. Scripts/events, inheritance, rich-text/HTML report objects, dialog pages, FPX and the complete provider/export catalog are unchanged. Preferences and stored zoom remain separate from the report model.

References: [FastReport PreviewControl API](https://www.fast-report.com/public_download/docs/FRNet/online/en/api/FastReport.Preview.PreviewControl.html), [typed weak references](https://learn.microsoft.com/en-us/dotnet/api/system.weakreference-1?view=net-10.0), [TryGetTarget](https://learn.microsoft.com/en-us/dotnet/api/system.weakreference-1.trygettarget?view=net-10.0).
