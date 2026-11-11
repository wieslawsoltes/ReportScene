# Validation and reproducibility

`Validate` gates Pages deployment on core conformance, differential benchmark evidence, isolated package consumption, Uno 7 desktop compilation, production WGSL/canvas validation and the actual Uno WebAssembly application with browser interaction tests. Failed or incomplete required jobs do not deploy. The [Uno 7 and WebGPU migration guide](uno7-webgpu-migration.md) documents the renderer, resource bounds and platform changes.

## Core and asynchronous contracts

`dotnet run --project tests/ReportScene.Tests -c Release` awaits `ConformanceSuite.RunAsync()` and emits sample JSON/PDF/SVG/PNG artifacts. The browser self-test awaits the same entry point. `Run()` includes only synchronous suites; its count must not be presented as the async suite's result.

Coverage includes expressions/aggregates, preparation limits, band/table/matrix/subreport pagination, row/parameter scope, serialization, transactional authoring, font-backed drawing, barcodes, exports, XML validation/typing/encoding and ADO.NET adapter/resource lifecycle. Matrix and subreport samples include every output page as PNG.

Subreport tests cover independent lanes, mismatched heights, empty/finished lanes, physical page/column numbering, headers/footers, nested references, master/detail scope, scoped parameters, inline growth/shrink/clipping, following objects, caller immutability, cycle/target/layout rejection, work limits and cancellation. Source-less target bands retain exactly one implicit row per invocation across root/bound callers and nested/independent/PrintOnParent modes, without losing explicit same-table group inheritance.

Performance regressions cover differential grapheme wrapping, work bounds, font-cache budgets, index query/painter-order equivalence, snapshot ownership, pixel equality for full/indexed drawing, large table/text pagination, cancellation, clipped search, Unicode whole-word navigation, activity revisions and recovery coordination. Relation tests preserve null/missing distinctions, source order, coercion/error behavior, RowNumber filters/sort, edited keys across preparations and complete scan-versus-index output equivalence.

Authoring tests cover resize directions, bounds, atomic batch failures, one-pass replacement work, snapped no-op frames, locking/history, unique paste names, hierarchical outlines/cancellation, page-property validation/undo, compact-paper geometry and bounded wrapping reuse. Tree system-variable insertions are evaluated by the real expression engine. Navigation tests cover loaded-document identity, skipped metadata work, indexed projection ownership/lookup and all selected objects' handles with overlap/locking/painter-order checks.

Twenty-four delayed-operation tests use TaskCompletionSource-controlled host callbacks for captured save revisions, dirty state/undo, replacement, queued-write serialization, stale/cancelled queues, errors, disposal, clipboard targets/fallback, late opens/images, transient drags and Save-before-replacement. Browser conformance runs the actual .NET async tests, not an inferred native result.

Preference tests cover strict generated JSON/defaults/version/limits, preferred-versus-visible layout, missing/invalid storage, equal updates, coalescing, delayed-load protection, concurrent saves, failed/cancelled pending writes, readiness before every snapshot and disposal. Native-only tests use real bounded UTF-8 file IO, replacement, invalid/cancelled-write preservation, cleanup and decoding failures. WebAssembly tests explicitly reject the filesystem-store constructor instead. Native and WebAssembly totals differ due both database and filesystem capabilities; the logs provide each actual count.

## Isolated package consumption

`scripts/pack-core.sh` builds eleven core packages and restores a separate package-only consumer with a fresh cache and local ReportScene feed. Fifty-four executable checks cover data/matrix/engine/serialization/history/index/search/async APIs, preferences, viewport layout, optional coalesced storage, report independence and real native preference files.

The desktop job downloads those core packages, compiles the application, and runs `scripts/pack-uno.sh`. The WebGPU renderer, controls and designer libraries opt into packing; the script requires fourteen nupkgs, checks their assemblies and compiles an independent consumer without source ProjectReferences. It exercises public property-grid, multi-context outline, splitter, options/matrix/subreport, data-preview, optional preference-store designer and report-surface APIs. NuGet source mapping pins all `ReportScene.*` packages to this build; framework dependencies come from external feeds. This compile check is not interactive package-only desktop execution on every OS.

To run locally after `scripts/pack-core.sh`, copy `artifacts/packages` to `artifacts/core-input/packages`, then run `bash scripts/pack-uno.sh`.

## Reproducible performance evidence

`dotnet run --project tests/ReportScene.Benchmarks -c Release` writes `artifacts/performance.json` and prints the same data. Inputs, correctness checksums, runtime/OS/architecture, work/candidate counts, median elapsed time and per-thread allocations are recorded. Wall-clock ratios are observations, not thresholds that can waive correctness failures.

Wrapping uses a frozen reference and the new algorithm with the same current metrics/cache. Spatial index construction is separate. Search API comparisons are repeated lazy scans versus session caching, not the old designer's already-cached Find-next. Relation benchmarks separately measure key-index construction/lookups and complete 300-parent/9,000-child preparations with current indexes disabled/enabled, including construction and page-count passes. Designer replacement compares lookup work with the prior loop and measures the current complete headless command separately. Indexed outline membership uses identical metadata/probes, with separate construction and API probe counts distinguished from internal hash comparisons.

Preferences add pure viewport calculations, unchanged-update notifications and coalesced acknowledged-write counts. Their counting store does not measure physical/browser persistence latency. See [performance](performance-review.md), [host/relations](host-operations-and-relations.md), [designer](designer-fidelity.md), [page/cache](designer-page-and-cache-review.md), [navigation](designer-navigation-review.md) and [preferences](designer-preferences.md) for scope and ownership.

## Actual browser execution

The workflow publishes WebAssembly once and uploads `browser-app`. Eight isolated runners download the same artifact, serve it under `/ReportScene/`, and run Playwright shards `1/8` through `8/8`. Each uses one Chromium/software-Vulkan worker, independent browser contexts and zero retries. The aggregate requires all eight reports, rejects failures/retries/skips/flaky outcomes and missing named scenarios, and requires at least 77 scenarios. `fail-fast: false` permits evidence collection from every shard; it never turns failure into success.

Native-input scenarios preserve the reporting, editing, export/search, matrix, XML, startup, subreport and delayed-host coverage. Thirteen designer scenarios test compact panes, nested outlines and node reuse, selection/mixed formatting, property validation, units, eight handles and locking, layout reuse, page properties and visible paper pixels, group-click behavior, authoring text cache, explorer contexts/keyboard navigation, jitter and metadata lifecycle.

Six preference scenarios additionally test native draft/Apply/Cancel/defaults/validation, persisted reload, viewport-only constraints and cancelled clamped drags, invalid stored data and explicit repair, quota failure with editing still available and explicit retry, and direct View/pane/sort notifications. Settings tests compare report serialization/history, actual native control state, visible pane geometry and real storage. Fault injection changes only the host storage API, not diagnostic application commands or report content.

All report changes arrive through actual pointer/keyboard/file input. Read-only diagnostics expose native bounds/state and persistence/work counters; no diagnostic method dispatches commands. Startup checks require a native WebGPU report submission, loading handoff, loader removal, actual nonblank screenshots and progressing state. Uncaptured native GPU errors and device loss fail the active document's tests. ComboBox selection uses fully visible popup rows and real scrolling. Wheel input requires a newer native `ViewChangeVersion` before a final scroll state can acknowledge it; stable diagnostic snapshots alone are insufficient. Viewport positioning calibrates wheel-to-DIP gain from acknowledged, unclamped movement, then retains the existing strict geometry and pixel assertions. Pane gestures also wait for committed native layout.

GPU-specific scenarios inspect viewport-sized targets at high zoom, layout reuse while scrolling, demand-driven idle behavior, a visible unsupported-WebGPU state, a transparent 9001 × 257 image across filtered tile boundaries, and clipped 200pt text at 400% zoom. The text scenario requires visible black glyphs, no rendering error and bounded atlas allocation even with many distinct characters outside the text box. Resource counters establish bounded visible-content residency; they do not measure GPU execution time.

## Production shader and canvas validation

An independent job compiles `Report2D.wgsl` and renders its 128-byte instance contract with explicit bind layouts, clipping and premultiplied blending. Thirty-nine pixel assertions cover geometric edges, rectangle joins, radial pie antialiasing, glyph alpha, images, procedural grids and degenerate lines. Two ordered draws exercise nonzero `firstInstance`.

The same commands render into a real `GPUCanvasContext`. Every canvas readback byte must match the offscreen texture, and all 39 presented screenshot samples must match composition over white. Device loss or a blank presentation fails this gate. CI selects a software Vulkan configuration that supports canvas/shared-image interop; successful offscreen rendering alone is insufficient.

## Artifacts and limitations

`core-validation` contains conformance/benchmark logs and JSON, packages, a tracked-source archive/revision/checksum and sample exports. `uno-packages` contains all fourteen packages and its consumer log. `browser-evidence-1` through `browser-evidence-8` retain raw states/screenshots/HTTP logs, and the eight `blob-report` artifacts retain mergeable results. `browser-validation` contains the merged report/JSON. `webgpu-shader-validation` contains raw/presented PNGs, readback samples, adapter metadata and console evidence. Only the shard running WebAssembly conformance reports its actual count; other shards cannot infer it.

Record final counts, commit/tree identities and merge/deployment outcomes only after the corresponding jobs finish. Hardware GPU throughput, peak/native memory, browser input latency, interactive desktop qualification on all OSes, every driver, mobile/accessibility parity and exhaustive FastReport comparison remain unverified. Settings and report-host tests do not establish physical-disk/browser durability, and abrupt close can lose unacknowledged debounced settings. A controlled host can await the explicit preferences flush.

References: [Playwright sharding](https://playwright.dev/docs/test-sharding), [NuGet source mapping](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping#package-pattern-precedence).
