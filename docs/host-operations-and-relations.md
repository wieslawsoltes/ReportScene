# Awaited workspace operations and relation indexes

This increment follows the [performance review](performance-review.md) and fixes review issues #4 and #5. It is a targeted cross-component review, not exhaustive FastReport parity or production certification.

## Stable asynchronous workspace operations

`DesignerSession.CaptureRevision()` issues an opaque owner/document-epoch/revision/sequence token. A new load expires every previous token even when the exact same document instance is loaded again. `CaptureTarget()` additionally captures the active page, band and selected IDs. Reads and mutations are owned by one UI synchronization context; direct external mutation of model lists and concurrent cross-thread session edits are not supported by these guards.

`DesignerOperations` supplies reusable Save, Copy, Cut, Paste and generic ReadAndApply operations without depending on Uno. Native-report saves are serialized before entering the host. A queued request whose content has been superseded is rejected before serialization or host access. A successful write records the revision actually written. Newer edits remain dirty, and undoing to the written revision becomes clean. Cancellation/failure does not clear dirty state. Loading another document prevents an older callback from changing its save state. Disposal cancels waiting requests and prevents delayed completions from mutating the session; a host write already in progress is not retroactively undone.

Cut captures its object selection before clipboard access and deletes only after successful acknowledgement and revalidation. Changed content/page/band/selection rejects the delayed operation. Paste checks the captured target outside its permission-error fallback, so cancellation and stale-target errors cannot become permission to paste an internal clipboard into a different band. Copy preserves its captured bytes but reports stale completion rather than a misleading current-selection count.

The Uno designer uses these operations for normal saves, open/new/sample replacement, image loading, clipboard commands and recovery loading. Its Save/Discard dialog permits replacement after Save only when the acknowledged snapshot is still current and no newer edits remain dirty. Cancelled saves keep the current report. Generic property/editor dialogs, matrix/subreport editors and data imports revalidate their initiating target/content before Apply. Host cancellation is represented by `OperationCanceledException` for saves and null for cancelled file reads. Browser download acknowledgement means acceptance of a download, not proof that the user saved it to durable storage.

The public host API is unchanged; its success/cancellation contract is now documented. The operation service is disposable and UI-thread-owned, not a thread-safe document store. Export/print commands report stale completion without retroactively deleting already-produced output. Repeated transient drag notifications no longer reset an already-empty preview surface.

Twenty-four delayed-callback service tests use real TaskCompletionSource-controlled awaits and are included through `ConformanceSuite.RunAsync()` in native and actual WebAssembly hosts. `Run()` remains the synchronous-only suite. Six additional Playwright scenarios deliver real pointer/keyboard/file-chooser input while a test wrapper delays only a host acknowledgement. The wrapper does not mutate report objects or synthesize application state. The save-cancellation scenario substitutes a cancelled host response to exercise the same contract as the desktop picker's cancellation; it is not a claim that Chromium exposes a cancellable disk-save acknowledgement.

## Source-less subreport rows

An unbound data band has one implicit row. Only an explicitly bound same-table subreport band now inherits its caller's row group. Previously a source-less band could have its implicit row replaced with an empty root row set or repeated over all caller rows. Eleven regressions cover independent, PrintOnParent and nested modes; root and bound callers; filters; explicit same-table inheritance; and native roundtrip/caller immutability.

## Relation indexing and exact semantics

`DataKeyIndex` is a reusable immutable column-key snapshot in Model. It returns original zero-based source ordinals in their original order. Missing fields do not match null; explicit null does. Homogeneous same-kind values use cached equality buckets, while the standalone API retains `Value.Compare` scanning for coercing/mixed queries.

The preparation engine uses only lookup cases equivalent to its existing comparison without coercion. Otherwise it retains the original interleaved relation/filter scan, including exception order. Filters and sort expressions keep their original source RowNumber and Rows context; output group numbering remains unchanged. An all-missing column does not evaluate a parent key that the original scan would never have read.

Indexes are shared across child runs in a pagination pass through a weak association with the existing shared preparation budget. There is no strong global root retaining reports. At most sixteen source/column indexes covering 262,144 source rows are retained per pass. Bands with fewer than 64 source rows keep the direct path; further cache requests over budget also retain the direct path rather than causing flush/rebuild churn. New preparations rebuild against current keys. Mutating data during a preparation is not supported. Highly repeated keys can still return most source rows, and construction has a cost.

`PreparationOptions.EnableRelationIndexes = false` selects the reference scan path for diagnosis and differential measurement. Both ordinary master/detail and independent-subreport preparation are compared with identical paper geometry, clips, IDs, order and text. Benchmarks include index construction, standalone lookups, and complete 300-parent/9,000-child preparations with the option off/on. Times and managed allocations are observations, not performance gates or hardware GPU measurements.

## CI execution and evidence

The WebAssembly application is published once. Three isolated browser jobs download that exact artifact and run Playwright shards, each with one software-rendering worker and zero retries. A required `browser` aggregation job requires all three blob reports, merges results and rejects failures, retries, skips or missing WebAssembly/host-operation scenarios. Deployment still requires core, desktop and the complete browser aggregate. Sharding changes wall-clock scheduling, not test coverage or acceptance criteria.

Artifacts retain per-shard raw evidence, merged browser reports, core conformance/benchmark JSON, sample exports and all reusable packages. Record precise pass counts and timing results from the final commit's CI, not from superseded runs. The implementation does not add FRX translation, arbitrary scripts/events, report inheritance, dialog pages, native FPX, the full external-provider/office-export catalog or complete original-designer visual parity.

References: [Playwright sharding](https://playwright.dev/docs/test-sharding), [SemaphoreSlim.WaitAsync](https://learn.microsoft.com/en-us/dotnet/api/system.threading.semaphoreslim.waitasync?view=net-10.0), [ConditionalWeakTable](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.conditionalweaktable-2?view=net-10.0).
