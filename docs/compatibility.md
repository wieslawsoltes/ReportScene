# Compatibility and implementation boundaries

ReportScene uses its own .NET API and native report JSON. Similar feature names are not a claim of binary/API compatibility with FastReport .NET. The public FastReport user manual and API documentation are reference material, not a licensed proprietary source dependency.

## Implemented and exercised

| Area | Implemented scope |
| --- | --- |
| Core reports | Band preparation, page/column headers and footers, grouping, sums/averages/count/min/max, stable sorting/filtering, master/detail, parameters, growing text and multi-pass total-page convergence |
| Objects | Text, shapes, embedded pictures, check boxes, six barcode kinds, basic bar/line/pie charts, bound tables and cross-tabs |
| Matrix aggregation | Up to eight levels per axis; typed keys; ascending/descending order; subtotals/grand totals; Sum, Count, CountRows, Average, Min, Max, DistinctCount, First and Last; percentage-of-row/column/grand-total displays |
| Matrix report integration | Expression and parameter bindings, group-scoped rows, master/detail filtering, source-backed totals, dynamic row heights, atomic rows, repeated headers, horizontal page tiles and existing exports |
| Matrix editing | Native Uno dimension/measure editors, reordering/removing/adding entries, axis swap, layout/total settings, validation, cancel, one-step apply/undo/redo and source-generated JSON/clipboard round trips |
| Data | CSV and scalar JSON; bounded repeated-row XML including attributes, xsi:nil and supported xsi:type scalars; explicitly configured ADO.NET adapters and query providers |
| Designer | Report/data trees, properties, direct selection/drag/resize, grid snapping, alignment, z-order, clipboard, undo/redo, preview/search, file import/export and recovery |
| Export | PDF, SVG, PNG, HTML, text, CSV and prepared JSON |

## Important narrower boundaries

Matrix subtotal averages and distinct counts aggregate source records rather than adding already-aggregated cells. Empty intersections remain null. Matrix output is bounded by row, axis-node, dense output, sparse-state, update and distinct-value budgets; it is not an unlimited OLAP engine. Custom cell objects, custom aggregate scripts, matrix events and complete commercial matrix presentation options are not implemented. Independently paginated tables/matrices cannot overlap vertically in one band; the engine diagnoses this instead of claiming side-by-side flow support.

XML import accepts a document root containing repeated scalar row elements. It does not infer hierarchical XML relations, import arbitrary XSD schemas, process DTDs, resolve external resources or load a .NET DataSet XML document with multiple row kinds. Attributes use the `Attribute_` prefix. Namespaces that collide in local column names are rejected. Use scalar-compatible expression field names; the expression language is not general C# or VB.NET.

ADO.NET adapters preserve report-compatible scalar values and single-column DataSet relations. Composite relations and unsupported CLR values are diagnosed instead of being partially converted or stringified. Direct queries are desktop/server-only, opt-in through application code, and have provider-dependent cancellation/async behavior. Tests use real .NET DataTableReader objects and instrumented connection/command fixtures; they do not certify every SQL Server, PostgreSQL, Oracle or other external driver/server combination.

Native JSON retains the implemented matrix definition. FRX import/export is a separate limited translator and does not gain matrix, scripts, events or other unsupported FRX semantics merely because the native model supports an analogous object. FPX compatibility remains absent.

Skia/HarfBuzz drawing is shared by designer, preview and exporters. CI software WebGL validates rendering correctness, not hardware throughput, WebGPU support, full typography conformance or identical glyph output on every platform. The application is Uno, not an HTML/CSS imitation of the reporting engine.

## Outstanding parity

General subreport objects and independent side-by-side flows; rich-text and HTML report objects; C#/VB.NET scripting and report events; report inheritance; dialog pages; all commercial objects/barcodes/charts; native FPX; the complete data-provider and export catalog, including office formats; advanced interactive report behavior; complete desktop platform and printer integration; full accessibility/mobile qualification; exhaustive original-designer visual and functional comparison.

Unsupported operations should report actionable diagnostics and retain the user's current report rather than silently dropping content.

## Public references

- FastReport .NET user manual: https://www.fast-report.com/public_download/docs/FRNet/online/en/UserManual/en-US/index.html
- FastReport subreport printing modes (remaining scope): https://www.fast-report.com/public_download/docs/FRNet/online/en/UserManual/en-US/DataBand/Subreports/SubreportPrintModes.html
- Microsoft XmlReader DTD behavior: https://learn.microsoft.com/en-us/dotnet/api/system.xml.xmlreadersettings.dtdprocessing?view=net-10.0
- Microsoft DbDataReader async behavior: https://learn.microsoft.com/en-us/dotnet/api/system.data.common.dbdatareader.readasync?view=net-10.0
