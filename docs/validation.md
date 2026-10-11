# Validation and reproducibility

The `Validate` workflow gates Pages deployment on core conformance/package consumption, Uno desktop compilation/package consumption, and the actual Uno WebAssembly application with browser interaction tests. It does not deploy a failed build.

## Core and package checks

`dotnet run --project tests/ReportScene.Tests -c Release` runs the shared conformance entry point and emits actual sample JSON, PDF, SVG and PNG artifacts. Matrix and subreport samples include PNGs of every output page. Coverage includes typed expressions, aggregate resolution, preparation limits, band/table/matrix/subreport pagination, source/parameter scope, serialization, transactional authoring, font-backed drawing, barcodes, exports, XML validation/typing/encoding and ADO.NET adapter/resource-lifecycle behavior.

Subreport regressions cover independent lane row preservation and page counts, mismatched row heights, empty/finished lanes, page and column numbering, target headers/footers, nested references, master/detail scope, parameter isolation, on-parent growth/shrink/clipping, following objects, caller immutability, rejected cycles/targets/layouts, bounded work and cancellation. Structural tests exercise atomic page+reference creation, page renaming, nested-band settings, history interoperation, navigation and safe clipboard handling.

Database query contract tests use instrumented connections/commands and real DataTableReader objects. Browser tests run the supported adapter checks and assert rejection of direct database queries before connection creation. Therefore native and WebAssembly test counts differ; neither platform's result is inferred from the other.

`scripts/pack-core.sh` builds eleven core nupkgs, including Matrix and Data.AdoNet. A separate package-only consumer restores from the generated local feed with a fresh package cache. It executes eight checks, including scoped subreport preparation and reference-safe page renaming.

The desktop job downloads those core packages, compiles the application and runs `scripts/pack-uno.sh`. Both Uno libraries explicitly opt into packing. The script requires thirteen nupkgs, checks both Uno library assemblies and compiles a separate consumer without source ProjectReferences. NuGet source mapping pins all `ReportScene.*` packages to the local build while external sources supply framework dependencies only. The consumer exercises public designer/surface/property/matrix/subreport/data-preview APIs. This compile check is not interactive package-only desktop execution.

To run the Uno package check locally after `scripts/pack-core.sh`, copy `artifacts/packages` to `artifacts/core-input/packages`, then run `bash scripts/pack-uno.sh`.

## Browser checks

Playwright serves the published .NET WebAssembly output under `/ReportScene/`. It uses actual pointer/keyboard/file-chooser input; no diagnostic command mutates the application. Read-only diagnostics provide state, geometry, native TextBox values and realized ComboBox popup bounds. The driver waits for observed state, not guessed popup delays.

Scenarios cover the original editing/dragging/history/save/export/search flow, matrix editing and samples, XML preview/import/rejection/cancel, ordinary/diagnostic startup presentation, subreport-and-page insertion and navigation, binding validation/apply/undo/cancel, searching bound child output, independent parallel samples, PDF exports, and preserving the current document when cyclic subreport JSON is rejected.

Presentation tests assert that the bootstrap loader is absent and inspect colors in actual screenshot pixels. A loaded-but-covered window cannot pass based only on internal state. Screenshot decoding uses a detached image/canvas and never changes the application DOM. Screenshots remain evidence artifacts for review; these checks do not certify pixel-identical FastReport UI parity.

The suite has zero automatic retries and does not waive failing assertions. Bounded waits account for cold WebAssembly and software Skia on shared runners. `summarize-evidence.mjs` reads the actual saved WebAssembly conformance JSON after tests and prints the precise pass/fail count; it neither generates replacement results nor overrides test statuses. Filtered local runs without a WebAssembly self-test have no inferred WebAssembly result.

## Evidence and limits

Successful runs publish `core-validation` (core packages, logs, sample exports), `uno-packages` (all thirteen packages and consumer log), `browser-validation` (test evidence and summaries), and `browser-app`. Each run's commit identifies the source. Concrete run IDs, counts and merge/deployment outcomes are recorded in the PR only after the corresponding job completes.

Hardware GPU throughput, interactive desktop qualification on every OS, all external database drivers, mobile/accessibility parity, and exhaustive FastReport functional/visual comparison are not certified by these checks.

References: [NuGet source mapping](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping#package-pattern-precedence), [Playwright global teardown](https://playwright.dev/docs/test-global-setup-teardown).
