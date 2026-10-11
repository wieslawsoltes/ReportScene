# Compatibility and implementation boundaries

ReportScene uses its own .NET API and native report JSON. Similar feature names are not a claim of binary/API compatibility with FastReport .NET. Public FastReport manuals and API documentation are reference material, not a licensed proprietary source dependency.

## Implemented and exercised

| Area | Implemented scope |
| --- | --- |
| Core reports | Band preparation, page/column headers and footers, grouping, sums/averages/count/min/max, stable sorting/filtering, master/detail, parameters, growing text and multi-pass total-page convergence |
| Objects | Text, shapes, embedded pictures, check boxes, six barcode kinds, basic bar/line/pie charts, bound tables, cross-tabs and subreport references |
| Matrix aggregation | Up to eight levels per axis; typed keys; ascending/descending order; subtotals/grand totals; nine aggregates; percentage-of-row/column/grand-total displays |
| Matrix report integration | Expressions/parameters, group-scoped rows, master/detail filtering, source-backed totals, dynamic row heights, atomic rows, repeated headers, horizontal page tiles and existing exports |
| Subreports | Reusable target pages, caller-scoped bindings, nested and independent side-by-side band flows, parent-column continuation, physical page numbering, PrintOnParent composition, growth/shrink/shift and lane clipping |
| Structural editing | Subreport-and-page insertion as one transaction; target navigation; reference-safe page rename; nested band editing; cycle/missing-target rejection on reference editing and clipboard paste |
| Object editors | Reusable Uno matrix and subreport definition controls; validation/cancel/apply; transactional undo/redo; native JSON and clipboard round trips |
| Data | CSV and scalar JSON; bounded repeated-row XML including attributes, xsi:nil and supported xsi:type scalars; explicitly configured ADO.NET adapters and query providers |
| Designer | Report/data trees, properties, direct selection/drag/resize, grid snapping, alignment, z-order, clipboard, history, preview/search, file import/export and recovery |
| Export | PDF, SVG, PNG, HTML, text, CSV and prepared JSON |

## Important narrower boundaries

Subreport support is described in [Subreports](subreports.md). It is not exhaustive original-engine layout parity. Target templates currently use a single column while parent output may use multiple columns. Independent references must occupy non-overlapping horizontal lanes at the lower band edge, with following content in subsequent bands. PrintOnParent captures the parent band's evaluation context, does not re-evaluate page-variable text on later slices, and rejects explicit page breaks or horizontally tiled matrices. Complex overlapping on-parent atomic flows may require independent mode. External-file subreports, scripted subreport events, every original interaction option and FRX subreport translation remain outstanding.

Matrix subtotal averages and distinct counts aggregate source records rather than adding already-aggregated cells. Empty intersections remain null. Matrix output is bounded by row, axis-node, dense output, sparse-state, update and distinct-value budgets. Custom cell objects, custom aggregate scripts, matrix events and complete commercial presentation options are not implemented. Independently paginated tables/matrices cannot overlap vertically in a single ordinary band; put them in separate bands or independent subreport lanes.

XML import accepts one document root containing repeated scalar row elements. It does not infer hierarchical XML relations, import arbitrary XSD schemas, process DTDs, resolve external resources or load a .NET DataSet XML document with mixed row kinds. Attributes use the `Attribute_` prefix. Namespaces that collide in local column names are rejected. The expression language is not general C# or VB.NET.

ADO.NET adapters preserve report-compatible scalar values and single-column DataSet relations. Composite relations and unsupported CLR values are diagnosed instead of being partially converted or stringified. Direct queries are desktop/server-only, opt-in through application code, and have provider-dependent cancellation/async behavior. Tests use real .NET DataTableReader objects and instrumented connection/command fixtures; they do not certify every external database driver/server combination.

Native JSON retains implemented matrix and subreport definitions. FRX import/export is a separate limited translator: native support does not imply FRX support for analogous objects. Unsupported FRX objects are refused. FPX compatibility remains absent.

Skia/HarfBuzz drawing is shared by designer, preview and exporters. CI software WebGL validates rendering behavior, not hardware throughput, WebGPU support, full typography conformance or identical glyph output on every platform. The application is Uno, not an HTML/CSS imitation of the reporting engine.

## Outstanding parity

Rich-text and HTML report objects; C#/VB.NET scripting and report events; report inheritance; dialog pages; all commercial objects/barcodes/charts; native FPX; the complete data-provider and export catalog, including office formats; advanced subreport and report interactions; complete desktop platform and printer integration; full accessibility/mobile qualification; exhaustive original-designer visual and functional comparison.

Unsupported operations should report actionable diagnostics and retain the user's current report rather than silently dropping content.

## Public references

- [FastReport .NET user manual](https://www.fast-report.com/public_download/docs/FRNet/online/en/UserManual/en-US/index.html)
- [Subreport printing modes](https://www.fast-report.com/public_download/docs/FRNet/online/en/UserManual/en-US/DataBand/Subreports/SubreportPrintModes.html)
- [SubreportObject API](https://www.fast-report.com/public_download/docs/FRNet/online/en/api/FastReport.SubreportObject.html)
- [Microsoft XmlReader DTD behavior](https://learn.microsoft.com/en-us/dotnet/api/system.xml.xmlreadersettings.dtdprocessing?view=net-10.0)
- [Microsoft DbDataReader async behavior](https://learn.microsoft.com/en-us/dotnet/api/system.data.common.dbdatareader.readasync?view=net-10.0)
