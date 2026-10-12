# ReportScene

A modular .NET reporting engine and Uno Platform designer for desktop and WebAssembly. ReportScene is an independent implementation developed against publicly documented reporting concepts, not an official Fast Reports product or a drop-in FastReport replacement.

[Browser application](https://wieslawsoltes.github.io/ReportScene/) · [Compatibility](docs/compatibility.md) · [Designer options](docs/designer-preferences.md) · [Designer and authoring review](docs/designer-fidelity.md) · [Explorer and selection review](docs/designer-navigation-review.md) · [Preview outline](docs/preview-outline-review.md) · [Subreports](docs/subreports.md) · [Data providers](docs/data-providers.md) · [Performance review](docs/performance-review.md) · [Workspace operations and relations](docs/host-operations-and-relations.md) · [Validation](docs/validation.md)

## Reporting and authoring

Band pagination, grouping and aggregates, master/detail relations, parameters, multi-column pages, growing text, prepared-page search, charts, tables, pictures, shapes and six barcode symbologies share the same .NET model and preparation engine. Matrix reports add typed hierarchical axes, subtotals and grand totals, nine aggregation operations, multiple measures, percentages, horizontal page tiling and repeated column headers.

Subreports reference reusable pages inside the document. Independent mode supports nested and side-by-side report flows with different row heights, caller-scoped parameters, parent-column continuation and physical page numbering. PrintOnParent composes report objects into the containing band, with growing/shrinking content and following-object shifts. Source-less data bands retain their one implicit row per invocation; explicitly bound same-table bands can inherit caller groups. Cycles, excessive nesting and unsupported layouts are diagnosed explicitly; [the subreport guide](docs/subreports.md) records the boundaries.

The designer offers report/data trees, property editors, drag/resize/snap, alignment, clipboard operations, transactional undo/redo, local recovery, native matrix and subreport editors, target-page navigation, reference-safe page renaming, and CSV/JSON/XML import with a bounded typed preview. Creating a subreport and its target page is one undoable operation.

The compact workspace adds searchable hierarchical trees, an object selector, categorized/alphabetical property filtering, descriptions, resizable/closable tool panes, live formatting state and status-bar zoom. Eight resize handles and reversible locking use the shared authoring session. Display units can be changed without rewriting point-based report geometry. The [designer review](docs/designer-fidelity.md) documents implemented behavior, public references and remaining differences from the original designer.

**Designer Options** adds native Workspace, Grid and rulers, and Display tabs. Apply validates a draft; Cancel and draft-only Restore defaults do not change active settings. Preferred pane sizes survive viewport constraints and return when the window grows. Pane visibility, grid/snap/units, sorting and zoom persist separately from report recovery through optional injected storage. Invalid/quota-limited storage leaves editing available. [The preferences guide](docs/designer-preferences.md) describes bounded schema validation, coalescing, stale-load protection, browser/native stores and the limits of abrupt-close persistence.

Data and Report Tree tabs retain independent native nodes, searches and expansion state. Keyboard report-tree selection navigates pages, bands and objects; arrowing through data fields does not insert them. Loading another report resets these in-memory contexts. Selection handles on every editable selected object are actionable and painted above report content. Save acknowledgements and page navigation avoid rebuilding outline metadata, while an indexed immutable projection avoids scanning filtered entries for selection membership. [The navigation review](docs/designer-navigation-review.md) records lifecycle, performance and remaining limitations.

Preview search supports next/previous, Shift+Enter, match case and whole word through a reusable `PreparedSearchSession`. The native page navigator adds First/Previous/Next/Last, strict ordinal entry, keyboard paging and whole-page/width fitting. Prepared-page indexes are reused while zooming; native viewport geometry keeps lower/right page content and indexed culling consistent. [Navigation](docs/preview-navigation-review.md) and [viewport rendering](docs/preview-viewport-review.md) are documented separately.

The preview **Outline** command opens a searchable, resizable page-grouped bookmark panel. Bounded immutable bookmark metadata is created lazily and reused across navigation and close/reopen. Duplicate captions remain separate occurrences; targets intersect page/item clips. Bookmark and search navigation reveal the target on both horizontal and vertical axes, including wide pages at high zoom. Report replacement clears old targets without retaining source pages in the outline metadata. [The outline review](docs/preview-outline-review.md) documents limits and actual-browser acceptance.

Saving or changing a selection does not discard prepared content. Revision-aware activity tracking avoids report-tree reconstruction and recovery serialization on every drag frame, while recovery coordination retains edits committed during an awaited write.

`DesignerOperations` provides revision-safe asynchronous save, clipboard and read/apply contracts independently of Uno. The designer uses it for file operations and captured editor targets. A delayed save records the revision actually written without clearing newer changes; delayed Cut/Paste/Open operations cannot silently affect a different target. Cancellation preserves unsaved content. [The host-operation guide](docs/host-operations-and-relations.md) specifies ownership, disposal and persistence guarantees.

Text wrapping segments each paragraph once and probes line-sized prefixes. Font-width cache storage is bounded by entries and retained characters. Large band slices and prepared viewports use immutable ordered indexes, and table/matrix row-boundary lookup uses binary search. A reusable `DataKeyIndex` accelerates compatible master/detail lookups while preserving coercing/mixed-value scan semantics and source ordinals. Engine caches are bounded and shared across nested invocations within one pass; `PreparationOptions.EnableRelationIndexes` enables differential diagnosis against the scan path.

Authoring batches resolve edited objects in one document traversal rather than restarting a scan for every selected item. New geometry/identity validation precedes mutation, unchanged snapped drag frames do not send duplicate notifications, and selection/save/zoom reuse authoring layouts. Keyed native tree nodes survive nonstructural edits; metadata construction remains a documented traversal cost.

PDF, SVG, PNG, printable HTML, text, CSV and native prepared JSON use the common prepared-page representation. Native report JSON is the lossless interchange format for implemented objects. FRX is an explicitly diagnosed subset and does not yet translate subreport or matrix objects; FPX is not supported. Clipping corrects visible geometry and link annotations; it is **not secure redaction**.

Uno controls and report drawing use Skia; text metrics/shaping use HarfBuzz. Browser validation runs the actual .NET WebAssembly application rather than a separate JavaScript mock. CI uses software WebGL, not evidence of hardware GPU performance.

## Reusable packages

| Package | Responsibility |
| --- | --- |
| `ReportScene.Model` | Report objects, typed scalar data, validation, reference graph, key/interval/page indexes, prepared search/bookmarks and preview geometry |
| `ReportScene.Expressions` | Bounded expression parsing and aggregate evaluation |
| `ReportScene.Data` | CSV/JSON/XML readers, file dispatch and provider contract |
| `ReportScene.Data.AdoNet` | CLR/DataTable/DataSet/DbDataReader adapters and explicit parameterized query providers |
| `ReportScene.Matrix` | Independent typed cross-tab aggregation |
| `ReportScene.Engine` | Band, table, matrix and nested subreport preparation/pagination |
| `ReportScene.Serialization` | Source-generated native JSON and diagnosed FRX subset |
| `ReportScene.Designer` | Headless authoring/history, geometry, indexed outlines, preferences/layout/store contracts, asynchronous operations and recovery |
| `ReportScene.Rendering.Skia` | Common report drawing, indexed rendering and font-backed metrics |
| `ReportScene.Export` | Prepared-report exporters |
| `ReportScene.Controls.Uno` | Design/preview surfaces, page navigator, bookmark outline, property grid, multi-context tree, splitters, options/matrix/subreport editors and data preview |
| `ReportScene.Designer.Uno` | Embeddable designer with host-supplied file/clipboard/print and optional preference storage |
| `ReportScene.Samples` | Executable example reports |

`ReportScene.Validation` is a non-packable test library shared by console and browser hosts. Core packages do not depend on Uno. Database queries require an explicitly supplied desktop/server connection factory; imported report files cannot create connections or execute scripts.

## Build and validate

Install the .NET SDK specified by `global.json`. From the repository root:

```sh
dotnet run --project tests/ReportScene.Tests -c Release
dotnet run --project tests/ReportScene.Benchmarks -c Release
bash scripts/pack-core.sh
dotnet build apps/ReportScene.App -c Release -f net10.0-desktop -p:ReportSceneTargetFramework=net10.0-desktop

dotnet workload install wasm-tools
dotnet publish apps/ReportScene.App -c Release -f net10.0-browserwasm \
  -p:ReportSceneTargetFramework=net10.0-browserwasm \
  -p:WasmShellWebAppBasePath=/ReportScene/ -o artifacts/wasm
python3 scripts/stage-browser.py artifacts/wasm artifacts/site artifacts/testsite/ReportScene
python3 -m http.server 8000 --directory artifacts/testsite
# In another terminal:
cd tests/browser
npm install
npx playwright install chromium
npx playwright test
```

CI publishes one WebAssembly artifact, tests it across eight isolated shards, and requires every shard plus merged evidence before deployment. The current browser gate requires all 49 scenarios, including the three bookmark/scroll scenarios; the core package consumer has 47 checks. Local `playwright test` runs the complete suite with one worker. No automatic retries are enabled. The benchmark records differential checks, work counts, allocations and timings in `artifacts/performance.json`; fixture measurements are not universal or hardware-GPU speed claims.

Open **View → Samples → Matrix sales**, **Subreport dashboard**, or **Subreport master-detail**. Use **Report → Import data** to bind CSV, JSON or scalar XML data. Double-click a matrix or subreport to edit its definition. The left toolbar includes **Insert subreport and page**, **Edit selected subreport**, and **Open subreport page**. Report page tabs expose the target's normal band/object editor. **View → Panels → Reset layout** restores closed tool panes; each explorer tab's search and the property filter operate independently. Escape inside explorer search clears that tab's filter. Open **Designer Options** from the quick-access settings icon, File menu or View Settings group. In preview, **Outline** exposes prepared bookmarks and **Whole page** fits the current page.

## Scope

This is active development, not complete FastReport .NET parity or production certification. Rich-text/HTML objects, report inheritance, arbitrary C#/VB.NET scripts/events, dialog pages, native FPX, the full provider/export catalog, advanced subreport presentation options, and complete original-designer visual/behavioral matching remain outstanding. True docking/floating windows and persisted dock arrangements, report-aware tree reparenting, toolbox drag placement, aggregate/lookahead profiling, large native selection controls and fully incremental structural metadata also need work. Preview remains single-page, without thumbnails or arbitrary script-driven outline hierarchies. See the compatibility and review documents for narrower boundaries within implemented areas.

FastReport is a trademark of its respective owner. No proprietary FastReport source, artwork or binaries are included. See [LICENSE](LICENSE).
