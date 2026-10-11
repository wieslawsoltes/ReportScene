# Validation and reproducibility

The `Validate` workflow gates Pages deployment on three jobs: core conformance/package consumption, Uno desktop compilation/package consumption, and the actual Uno WebAssembly application with browser interaction tests. It does not deploy a failed build.

## Core and package checks

`dotnet run --project tests/ReportScene.Tests -c Release` runs the common conformance entry point and emits real sample JSON, PDF, SVG and PNG artifacts, including all matrix sample pages. Coverage includes typed expressions, compound aggregates, preparation limits, band/table/matrix pagination, source scope, source-generated serialization, authoring history, font-backed drawing, barcodes, exports, XML safety/typing/encoding and ADO.NET scalar/adapter/resource-lifecycle behavior.

Database query contract tests use instrumented DbConnection/DbCommand instances and real DataTableReader objects. Browser tests run the supported adapter tests and assert that direct database queries fail before connection creation. Consequently console and WebAssembly test counts differ; neither platform's count is inferred from the other.

`scripts/pack-core.sh` builds all eleven core nupkgs, including Matrix and Data.AdoNet. A separate package-only consumer restores from the local artifact feed with a fresh package cache and runs six integration checks.

The desktop job downloads the core packages, compiles the desktop application, and runs `scripts/pack-uno.sh`. Both Uno library projects explicitly opt into packing. The script requires exactly thirteen generated nupkgs, inspects both Uno packages for their actual library assemblies, and compiles a separate Uno consumer without any source ProjectReference. NuGet source mapping pins every `ReportScene.*` dependency to the generated local feed; the external feed supplies only framework dependencies. A fresh consumer package cache prevents a previously installed ReportScene package from hiding a missing local dependency. Missing files fail the job rather than producing a successful no-op pack step.

The Uno consumer compile exercises the designer, design and preview surfaces, property sheet, matrix editor and data preview APIs. It is a compilation check, not a claim that a package-only desktop UI was interactively executed. The actual browser application supplies the separate interactive validation.

To run the Uno package check locally after `scripts/pack-core.sh`, copy `artifacts/packages` to `artifacts/core-input/packages`, then run `bash scripts/pack-uno.sh`. The core package-only test uses no public package source. The Uno package-only test uses the generated source-mapping configuration because it also needs Uno/Skia dependencies.

## Browser checks

The Playwright suite serves the published .NET WebAssembly output under the actual `/ReportScene/` base path. Tests do not invoke designer mutation methods through JavaScript. The diagnostic bridge exposes only state, geometry, native TextBox values and realized ComboBox popup item bounds. Drivers send real pointer/keyboard/file-chooser events and wait for observed native state instead of guessing fixed popup timing.

Coverage includes startup and shared .NET conformance; insert/edit/drag/undo/redo/save/PDF; opening a local report and prepared-page search; matrix editor validation and cancel, transaction history, source bindings, sample selection, page navigation and export; typed XML import/preview/undo/redo/search; rejected and cancelled data imports retaining the current document.

Screenshot review found that a retained bootstrap splash could cover a functionally active Skia designer. The browser host now completes Uno's normal loading-screen handoff after the actual designer has rendered, in both diagnostic and ordinary startup modes. Presentation tests assert absence of the loader and inspect colors in the actual screenshot pixels; state-only assertions cannot pass a blank/covered window. Screenshot decoding uses a detached image/canvas and never changes application state or DOM.

The suite has no automatic retries and does not suppress failing assertions. Its bounded waits allow cold WebAssembly/interpreter and software Skia rendering on shared CI workers. Hardware GPU throughput, desktop interaction on every OS, live external database drivers, and exhaustive FastReport visual parity are not certified by these checks.

## Evidence

Each successful workflow run publishes `core-validation` (eleven core packages, tests and sample exports), `uno-packages` (all thirteen packages and the Uno consumer log), `browser-validation`, and `browser-app`. Browser artifacts contain .NET conformance JSON, saved report JSON, screenshots, exported PDFs and failure traces. The run's commit SHA identifies the tested source. The PR records concrete passing run IDs and the eventual merge/deployment status; this document describes the tests rather than asserting a future run has passed.

NuGet source-mapping precedence reference: https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping#package-pattern-precedence
