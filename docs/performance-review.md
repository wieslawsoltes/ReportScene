# Performance and correctness review

This review follows the nested-subreport implementation. It addresses concrete hot paths and lifecycle defects in model/text layout, the preparation engine, expression scope, rendering metrics, prepared preview/search, headless authoring activity and the Uno host. It is not an exhaustive audit or certification of every reporting feature, external driver, operating system or original FastReport behavior.

## Text layout and font cache

The previous wrapping algorithm repeatedly shaped the entire remaining paragraph and rebuilt its grapheme boundaries on every line. A narrow paragraph therefore accumulated quadratic suffix work and large transient strings. `TextLayout.Wrap` now segments a paragraph once and locates each line end with exponential line-prefix probes followed by binary search. The result retains the original line/word-break behavior for the differential corpus. Oversized graphemes still make progress without splitting surrogate pairs, emoji sequences or combining marks. A standalone space can be trimmed; a space carrying combining marks cannot be torn apart.

Both implementations assume monotonic prefix advances for their binary searches; this work is not a full Unicode line-breaking or complex-script typography implementation. The four-argument API remains available. A cancellation-aware overload is added, and `ReportPreparer` guards all width/line-height callbacks for cancellable preparations, including table, matrix and nested-subreport text. Cancellation is observed between native shaping calls, not by interrupting an individual synchronous HarfBuzz call.

`SkiaTextMeasurer` now bounds its width cache by both entry count (4,096) and retained text/family characters (262,144). Strings over 1,024 characters are not retained. FIFO eviction removes old entries instead of flushing the whole cache at capacity. Font-face recycling also invalidates widths. Disposed metrics reject cached and empty-string paths rather than returning stale values. Cache counters are exposed for inspection; instances remain owned by one rendering/preparation thread and must be disposed.

## Prepared-page and pagination indexes

`PreparedPageIndex` copies its prepared item list and builds a balanced bounding-volume tree. Queries clip against object/page visibility, conservatively include stroke extents and restore original painter order. The Uno preview retains only the current page index, reuses it while zooming, and releases it when a report changes. Culling uses the clip actually supplied by Skia; a host that supplies a full-page clip still requires full-page drawing. Building the index has a cost and is measured separately from repeated queries.

The preparation engine has a distinct need: it must preserve raw band geometry before final clipping. `VerticalIntervalIndex` supplies ordered candidates for band slices and text-line cut decisions without applying visual clipping rules prematurely. Large layouts are indexed once per layout/pass and held through weak per-run keys. Bands below 64 prepared objects retain the direct path. Atomic table/matrix row-end lookup uses binary search rather than rescanning from row zero at every page break. Existing fixed-point cut rules, epsilon comparisons, lane composition and painter order remain in force.

The indexes are immutable snapshots, not live views of mutable input lists. Rebuild after changing prepared content. They trade bounded per-page/per-layout storage and construction time for fewer candidates on repeated narrow queries. Highly overlapping items and full-page viewports may still require examining most of the content.

## Search, preview and authoring lifecycle

`PreparedSearchSession` provides reusable cached search, previous/next navigation with wraparound, case-sensitive matching and Unicode-category whole-word boundaries. The UI exposes both options and Shift+Enter for previous. One result corresponds to one prepared text item, even when it contains multiple occurrences; highlighting remains at the item's visible geometry, not individual glyph ranges. It is not a locale-specific word segmenter.

Search and outline geometry now exclude fully clipped/off-paper objects. A new preparation resets search results even when the query string is unchanged. Search navigation updates the visible page-number editor, and prepared page metadata is resolved by page number rather than assuming page number minus one is always the array index. Page/report replacement clears stale highlights; an empty report clears the old paper surface.

`DesignerSession.Revision` distinguishes committed content from selection, navigation and marking a document saved. `DesignerActivityTracker` prevents these view-only actions from invalidating a valid prepared report or scheduling another recovery serialization. During a drag, transient notifications no longer build the full report-tree structural key or save intermediate frames. A committed edit invalidates the old prepared report/search, releases preview data, updates structure and requests recovery. Undo, redo and document load are committed changes. Deleted page-tab controls are removed from the command registry.

`RecoverySaveCoordinator` serializes writes, coalesces requests and drains a newer committed request received during an awaited save. A failed write remains pending for an explicit retry or a later edit; it does not loop on persistent storage errors. Recovery generation counters are separate from undo history positions. The coordinator does not make a host's filesystem/browser storage transaction atomic or durable beyond the host's own guarantees.

Explicit `Parameters.Name` references no longer fall through to a row field or system variable when the binding is absent. Missing bindings fail with an expression diagnostic; explicit null remains null. Unqualified field/parameter precedence and explicit parent access remain available.

## Reproduce and interpret the measurements

```sh
dotnet run --project tests/ReportScene.Tests -c Release
dotnet run --project tests/ReportScene.Benchmarks -c Release
```

The benchmark writes `artifacts/performance.json` and prints the same evidence. CI retains it in `core-validation`. It records runtime, OS, architecture, processor count, input sizes, measured characters/calls, median elapsed time, allocations and correctness checksums. Each timing is the median of three in-process repetitions after one warmup. No speed ratio is a pass/fail threshold on shared runners.

The wrapping baseline is a frozen copy of the pre-optimization algorithm. Font-backed comparison uses the same current Skia metrics implementation and its bounded cache for both algorithms, so it measures wrapping behavior, not an independently measured old-cache implementation. A counting measurer separately exposes algorithmic character work. Spatial query output is compared with brute force, and index construction is timed separately. Complete current-engine table preparation is measured without inventing an unmeasured old-engine time. The search API comparison is repeated lazy scans versus the new reusable session, not a claim that the old designer rescanned on every Find-next; it already cached results but invalidated them incorrectly across preparations.

For an early measured revision (`0814e9812025c0779fb3a5cffd56dab5c6389d30`, before indexed engine slicing), [CI run 38141000062](https://github.com/wieslawsoltes/ReportScene/actions/runs/38141000062) recorded 32,768-character wrapping at 416.2411 ms versus 1.3058 ms, with identical 820-line output. Measured-character work changed from 27,014,211 to 267,757; allocation changed from 108,133,760 to 927,376 bytes. These are that runner's synthetic fixture measurements, not a universal report speedup. Consult the current run's JSON for subsequent results.

Conformance uses deterministic work/candidate bounds, randomized differential queries, a frozen wrapping reference, font-backed layout comparison, pixel equality for indexed versus full Skia drawing, 2,000-row/223-page table preservation, large multiline pagination, cancellation, cache budgets, parameter scope and lifecycle tests. Browser tests use actual native Uno controls and pointer/keyboard input to check advanced search, same-query result refresh, save/zoom cache retention and recovery completion. Diagnostic counters are read-only.

## Remaining review and feature work

The source review also identified areas needing separate work: relation lookups still scan child tables for each parent; complex aggregate work and layout lookahead need broader profiling; the designer still rebuilds structural keys on committed edits and diagnostic snapshots enumerate the document; report preparation still materializes its final pages; cache policies need diverse workload measurements; asynchronous recovery must be qualified with each real host's persistence semantics.

FRX remains the existing diagnosed subset. Its parameter-default, relation, grouping and subreport translation require further compatibility work; an attempted FRX patch was not applied in this increment. Do not use FRX as a lossless round-trip format. Native JSON remains the supported interchange for current matrices and subreports.

Rich-text/HTML objects, scripts/events, report inheritance, dialog pages, native FPX, the full external-provider and office-export catalog, complete designer parity, full accessibility/mobile qualification and interactive desktop testing across supported platforms remain outstanding. CI software WebGL and CPU measurements do not certify hardware GPU throughput, frame rate, power use or end-to-end input latency.

## References

- [StringInfo and text elements](https://learn.microsoft.com/en-us/dotnet/api/system.globalization.stringinfo?view=net-10.0)
- [Rune Unicode categories](https://learn.microsoft.com/en-us/dotnet/api/system.text.rune.getunicodecategory?view=net-10.0)
- [Per-thread allocation measurements](https://learn.microsoft.com/en-us/dotnet/api/system.gc.getallocatedbytesforcurrentthread?view=net-10.0)
- [Source and validation](https://github.com/wieslawsoltes/ReportScene)
