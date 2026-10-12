# Preview navigation and fitting

This increment adds ordinary report-viewing controls to the actual Uno preview: First/Previous/Next/Last, validated page entry, keyboard navigation and whole-page fitting. The public FastReport API documents the corresponding first/last/next/prior and one-based page-position concepts; this is an independent implementation, not a drop-in API or visual-parity claim.

## Reusable pieces

`ReportScene.Model.ReportPagePosition` is an immutable ordinal cursor over a displayed page count. It handles empty/single-page reports, clamps moves at both boundaries and rejects negative counts or unknown commands. Parsing accepts trimmed positive decimal ordinals in range, rejects malformed/overflowing values and preserves the exact current position on failure. The displayed ordinal is not a physical `PreparedPage.Number`; the existing search code still resolves physical metadata into the displayed page-list index.

`ReportPreviewFit.Calculate` converts page points to preview DIPs and uses a 40-DIP total gutter. WholePage uses the limiting width/height ratio; Width uses only the horizontal ratio. An unmeasured or exhausted viewport returns no zoom request. Input must be finite and valid; zoom retains the established 0.1–4 range. Very large paper may not fit completely at the minimum zoom. Fit is an explicit action, not a persistent automatic resize mode.

`ReportScene.Controls.Uno.ReportPageNavigator` supplies native buttons, editable page ordinal, page count and inline validation. The host supplies current position and handles `PageRequested`. Disabled boundary buttons, invalid entries, Escape restoration and same-page requests do not ask the host to change page. Unrelated same-position view updates preserve a draft entry; the host can explicitly reset it when binding a replacement report. The control does not own, prepare or cache report pages.

`ReportPreviewView.Navigate` uses the ordinal cursor. PageUp/PageDown navigate when the preview surface has focus; Ctrl+Home/Ctrl+End go to first/last. The page-entry TextBox retains its normal text-editing keys. `FitPage` and `FitWidth` share the geometry calculation. The designer keeps its native navigator synchronized with button, keyboard and search-driven page changes. Event subscription is retained once per designer and removed on disposal, avoiding subscriptions to obsolete ribbon controls after rebuilds.

## Correctness and retained performance

The previous page-entry callback silently clamped out-of-range input and subtracted one directly from a parsed integer. It now validates the user-facing ordinal before any navigation. Invalid, zero, negative, fractional or overflowing input leaves the current page and spatial index unchanged and displays a recoverable message. The current-page index is still built only when the page object actually changes; fitting and no-op boundary commands do not reprepare the report or rebuild that index. No new report-wide navigation index or page cache is added.

Navigation callbacks check both the active control and the bound report identity. The report identity is a `WeakReference<PreparedReport>`, not a strong closure capture: a removed toolbar callback must not keep an invalidated report's pages alive. A successful `TryGetTarget` obtains a temporary reference only for the synchronous identity check and navigation. No stale event is allowed to navigate a replacement report. This removes a concrete retention path found during source review; it is not a measured peak-memory or exhaustive garbage-collection certification.

Twelve shared native/WebAssembly conformance cases cover ordinal and fit arithmetic, culture, overflow, invalid geometry and boundaries. Five extra independent core package assertions bring that consumer to 40 checks; the Uno package consumer compiles the navigator and new preview APIs. Three native-input browser scenarios preserve the preceding 42 and raise the required aggregate to 45. They exercise all four buttons and their actual enabled state, invalid entry and Escape, search synchronization, keyboard navigation after repeated ribbon rebuilds, single-page boundaries, and fit at two viewport sizes. Fitting checks actual element bounds and presented paper pixels, not model state alone, and serialized source remains unchanged.

No browser scenario retry, skipped assertion or reduced acceptance threshold is introduced. Concrete results must be read from the final current-head run. Core arithmetic tests are not a substitute for the actual Uno browser/desktop/package gates. This increment retains existing cache behavior; it does not claim a measured GPU, frame-rate, whole-application or input-latency speedup.

## Remaining scope

Continuous multi-page view, thumbnail navigation, clickable preview outline, a full vendor-equivalent print/export dialog, and complete FastReport designer/viewer parity remain outstanding. Reporting scripts/events, inheritance, rich-text/HTML objects, dialog pages, FPX and the full provider/export catalog are unchanged. Pane preferences and stored zoom continue through the separate preference service; report navigation never marks report content dirty.

References: [FastReport PreviewControl API](https://www.fast-report.com/public_download/docs/FRNet/online/en/api/FastReport.Preview.PreviewControl.html), [typed weak references](https://learn.microsoft.com/en-us/dotnet/api/system.weakreference-1?view=net-10.0), [TryGetTarget](https://learn.microsoft.com/en-us/dotnet/api/system.weakreference-1.trygettarget?view=net-10.0).
