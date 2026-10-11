# Validation and reproducibility

The `Validate` workflow gates Pages deployment on three jobs: core conformance/package consumption, Uno desktop compilation, and the actual Uno WebAssembly application with browser interaction tests. It does not merge or deploy a failed build.

## Core and package checks

`dotnet run --project tests/ReportScene.Tests -c Release` runs the common conformance entry point and emits real sample JSON, PDF, SVG and PNG artifacts, including all matrix sample pages. Coverage includes typed expressions, compound aggregates, preparation limits, band/table/matrix pagination, source scope, source-generated serialization, authoring history, font-backed drawing, barcodes, exports, XML safety/typing/encoding and ADO.NET scalar/adaptor/resource-lifecycle behavior.

Database query contract tests use instrumented DbConnection/DbCommand instances and real DataTableReader objects. Browser tests run the supported adapter tests and assert that direct database queries fail before connection creation. Consequently console and WebAssembly test counts differ; neither platform's count is inferred from the other.

`scripts/pack-core.sh` builds all eleven core nupkgs, including Matrix and Data.AdoNet. A separate package-only consumer restores from the local artifact feed with a fresh package cache and runs six integration checks. The two Uno libraries are built/packed in the desktop job. Compilation and packing do not substitute for interactive desktop execution.

## Browser checks

The Playwright suite serves the published .NET WebAssembly output under the actual `/ReportScene/` base path. Tests do not invoke designer mutation methods through JavaScript. The diagnostic bridge exposes only state, geometry, native TextBox values and realized ComboBox popup item bounds. Drivers send real pointer/keyboard/file-chooser events and wait for observed native state instead of guessing fixed popup timing.

Coverage includes startup and shared .NET conformance; insert/edit/drag/undo/redo/save/PDF; opening a local report and prepared-page search; matrix editor validation and cancel, transaction history, source bindings, sample selection, page navigation and export; typed XML import/preview/undo/redo/search; rejected and cancelled data imports retaining the current document.

The suite has no automatic retries and does not suppress failing assertions. Its bounded waits allow cold WebAssembly/interpreter and software Skia rendering on shared CI workers. Hardware GPU throughput, desktop interaction on every OS, live external database drivers, and exhaustive FastReport visual parity are not certified by these checks.

## Evidence

Each workflow run publishes `core-validation`, `uno-packages`, `browser-validation`, and (on successful browser validation) `browser-app`. Browser artifacts contain .NET conformance JSON, saved report JSON, screenshots, exported PDFs and failure traces. The run's commit SHA identifies the tested source. The PR records concrete passing run IDs and the eventual merge/deployment status; this document describes the tests rather than asserting a future run has passed.
