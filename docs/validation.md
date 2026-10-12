# Validation and reproducibility

`Validate` gates Pages deployment on core conformance, differential benchmark evidence, isolated package consumption, Uno desktop compilation and the actual Uno WebAssembly application with browser interaction tests. Failed or incomplete required jobs do not deploy.

## Core and asynchronous contracts

`dotnet run --project tests/ReportScene.Tests -c Release` awaits `ConformanceSuite.RunAsync()` and emits sample JSON/PDF/SVG/PNG artifacts. The browser self-test awaits the same entry point. `Run()` remains available but deliberately includes only the synchronous suites; its count must not be presented as the async suite's result.

Coverage includes expressions/aggregates, preparation limits, band/table/matrix/subreport pagination, row/parameter scope, serialization, transactional authoring, font-backed drawing, barcodes, exports, XML validation/typing/encoding and ADO.NET adapter/resource lifecycle. Matrix and subreport samples include every output page as PNG.

Subreport tests cover independent lanes, mismatched heights, empty/finished lanes, physical page/column numbering, headers/footers, nested references, master/detail scope, scoped parameters, inline growth/shrink/clipping, following objects, caller immutability, cycle/target/layout rejection, work limits and cancellation. Source-less target bands are tested for exactly one implicit row per invocation across root/bound callers and nested/independent/PrintOnParent modes, without losing explicit same-table group inheritance.

Performance regressions cover differential grapheme wrapping, work bounds, font-cache budgets, index query/painter-order equivalence, snapshot ownership, pixel equality for full/indexed drawing, large table/text pagination, cancellation, clipped search, Unicode whole-word navigation, activity revisions and recovery coordination. Relation-key tests preserve null/missing distinctions, source order, coercion/error behavior, source RowNumber filters/sort, edited keys across preparations and complete scan-versus-index output equivalence.

Authoring regressions cover eight resize directions, origin/minimum bounds, atomic batch failures and one-pass replacement work, snapped no-op frames, locking/history, unique paste names, hierarchical outline filtering and cancellation, page-property validation/undo, compact-paper geometry and bounded wrapping-result reuse. The tree's system-variable expressions are tested against the actual expression engine. Navigation tests add loaded-document identity, skipped save/navigation metadata work, indexed projection ownership and lookup, and all selected objects' handles with overlap/locking/painter-order checks.

Twenty-four delayed-operation service tests use awaited TaskCompletionSource-controlled host callbacks. They exercise captured save revisions, dirty state/undo, document replacement, queued-write serialization, stale and cancelled queues, errors, disposal, clipboard target changes/fallback, delayed opens/images, transient drags and Save-before-replacement. Browser runs execute these .NET async tests rather than inferring their result from native tests.

ADO query contract tests use instrumented connections and real DataTableReader objects. Browser tests check supported adapters and reject direct queries before connection creation. Native and WebAssembly totals therefore differ; actual logs provide each count.

## Isolated package consumption

`scripts/pack-core.sh` builds eleven core packages and restores a separate package-only consumer with a fresh cache and a local ReportScene feed. Twenty-nine executable checks cover data adapters, matrix/engine/serialization, history, scoped subreports, safe page rename, page/interval/key indexes, search, revisions, asynchronous operations, resize geometry, display units, outlines, batch transforms, locking, page properties, compact paper, wrapping-result caching, indexed outline projections, loaded identity and navigation activity.

The desktop job downloads those core packages, compiles the application, and runs `scripts/pack-uno.sh`. Both Uno libraries opt into packing; the script requires thirteen nupkgs, checks their assemblies and compiles an independent consumer without source ProjectReferences. It uses the public property-grid, multi-context outline, splitter, matrix/subreport, data-preview and report-surface APIs. NuGet source mapping pins all `ReportScene.*` packages to this build. Framework dependencies come from external feeds. This compile check is not interactive package-only desktop execution on every OS.

To run locally after `scripts/pack-core.sh`, copy `artifacts/packages` to `artifacts/core-input/packages`, then run `bash scripts/pack-uno.sh`.

## Reproducible performance evidence

`dotnet run --project tests/ReportScene.Benchmarks -c Release` writes `artifacts/performance.json` and prints the same data. Inputs, correctness checksums, runtime/OS/architecture, work/candidate counts, median elapsed time and per-thread allocations are recorded. Wall-clock ratios are observations, not thresholds that can waive correctness failures.

Wrapping compares a frozen reference with the new algorithm using the same current metrics/cache. Spatial index construction is measured separately. Search API measurements compare repeated lazy scans with session caching, not the old designer's already-cached Find-next. Relation benchmarks separately measure standalone key-index construction/lookups and complete 300-parent/9,000-child preparations with the current engine's relation indexes disabled/enabled, including construction and page-count passes. Designer replacement compares lookup work with the preceding per-item loop and measures the current complete headless command separately; outline construction and cached queries are also separate observations. Indexed outline selection compares identical metadata/probes against the previous membership scan, recording construction separately and distinguishing API probes from internal hash-table comparisons.

See [the performance review](performance-review.md), [host/relation increment](host-operations-and-relations.md), [designer review](designer-fidelity.md), [page/cache follow-up](designer-page-and-cache-review.md), and [navigation review](designer-navigation-review.md) for caveats.

## Actual browser execution

The workflow publishes WebAssembly once and uploads `browser-app`. Four isolated runners download that same artifact, serve it under `/ReportScene/`, and run `--shard=1/4`, `2/4`, `3/4`, or `4/4`. Each runner uses one Chromium/software-rendering worker, independent browser contexts and zero retries. A required aggregate job checks that all four blob reports exist, merges them, and rejects failures, retries, skips, flaky results or missing required scenarios. The gate requires at least 36 scenarios including actual WebAssembly, delayed-host and all thirteen designer families. `fail-fast: false` lets every shard finish collecting evidence; it never converts a failing shard into success.

Native input scenarios cover editing, pointer transforms, history, save/export/search, matrix definitions and tiles, typed XML preview/import/rejection, ordinary/diagnostic startup, subreport creation/editing/navigation/bindings/lanes, cycle rejection and advanced search/lifecycle behavior. Six delayed-host scenarios exercise actual save bytes and dirty-state undo, Cut selection changes, Paste band changes, late file-picker results and Save-before-new with newer edits or cancellation.

The thirteen designer scenarios cover compact/resizable/closable panes, nested-tree filtering and node reuse, object selection, mixed formatting, property validation and Escape, geometry display units, all eight resize handles, locking, selection/save/zoom layout reuse, page properties, visible compact/full paper pixels, click-versus-group-drag behavior, authoring text-cache reuse/invalidation, independent explorer contexts, native keyboard selection, first-selected-object handles/jitter and save/navigation/reload metadata lifecycle.

The delayed-host wrapper only delays host I/O acknowledgements after real I/O starts. All report changes still arrive through real pointer/keyboard/file input; diagnostic state and report objects are not synthesized. One scenario substitutes a cancelled host response to validate the documented cancellation contract. Read-only diagnostics provide native control bounds/state. Tests assert bootstrap-loader removal and actual screenshots containing rendered UI pixels. All reporting suites share the actual-presentation/fresh-snapshot handshake; read-only command polls transfer only the requested fields. ComboBox input waits for visible stable geometry and scrolls the real popup when necessary. Resize assertions wait for committed rather than transient preview state.

## Artifacts and limitations

`core-validation` contains conformance/benchmark logs and JSON, packages, a tracked-source archive/revision/checksum and sample exports. `uno-packages` contains all thirteen packages and its consumer log. `browser-evidence-1/2/3/4` retain raw per-shard state/screenshots and HTTP logs; `blob-report-1/2/3/4` retain mergeable results. `browser-validation` contains the merged report and JSON. The WebAssembly conformance summary is printed by the shard that ran that scenario; other shards must not infer a WASM result.

Record final counts, commit/tree identities and merge/deployment outcomes only after the corresponding jobs complete. Hardware GPU throughput, native/peak memory, browser input latency, interactive desktop qualification across OSes, every external database driver, mobile/accessibility parity and exhaustive FastReport comparison remain unverified. Host-operation tests do not establish browser/filesystem durability guarantees.

References: [Playwright sharding](https://playwright.dev/docs/test-sharding), [NuGet source mapping](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping#package-pattern-precedence).
