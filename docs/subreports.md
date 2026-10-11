# Nested subreports and composition

Subreport support is native to the reusable model, engine, serialization and designer packages. It does not instantiate FastReport assemblies, run a second JavaScript engine, or replace report output with a screenshot. Child bands are prepared into the same vector/text/object representation consumed by the existing preview and exporters.

## Page references and caller scope

A `SubreportItem.PageName` points to another `ReportPage` in the same document, matched case-insensitively. Referenced pages are templates and do not print again as independent top-level pages. Pages with no incoming subreport references print in document order. This includes a former target after its final reference is deleted: the page remains in the document and becomes a top-level page. Hidden references still identify their targets as templates. Missing targets, cycles (including unused cycles), duplicate names and reference paths over 32 levels are rejected before preparation.

```csharp
using ReportScene.Model;
using ReportScene.Engine;

var detail = new ReportPage
{
    Name = "OrderDetail",
    Bands = [new Band
    {
        Kind = BandKind.Data, DataSource = "Orders", Height = 26, CanSplit = false,
        Items = [new TextItem
        {
            Text = "[Orders.Amount * Parameters.Factor]",
            Bounds = new(0, 0, 220, 22)
        }]
    }]
};
var report = new ReportDocument
{
    Data = [new DataTable
    {
        Name = "Orders", Columns = ["Amount"],
        Rows = [new() { ["Amount"] = 12.50m }, new() { ["Amount"] = 7.25m }]
    }],
    Pages = [new ReportPage
    {
        Name = "Main",
        Bands = [new Band
        {
            Kind = BandKind.ReportTitle, Height = 24,
            Items = [new SubreportItem
            {
                Name = "OrderList", PageName = "OrderDetail",
                Bounds = new(0, 0, 220, 24),
                ParameterBindings = [new("Factor", "2")]
            }]
        }]
    }, detail]
};
var prepared = new ReportPreparer().Prepare(report);
```

All parameter binding expressions read the original caller's context; a later binding cannot accidentally read a value assigned by an earlier binding. A child receives its own parameter dictionary, so overrides do not leak to siblings or the caller. Reports share the caller-provided data tables without copying row data into each reference.

The target's data bands use ordinary expressions, filtering, sorting, grouping, aggregates and report relations. A matching master/detail relation filters child rows using the active caller row. `Parent.Name` inside a child data row resolves the caller's field, and a qualified caller-table field can be resolved through the parent context chain. A same-table subreport invoked from a group inherits that group's row scope. Use a data-band footer for filtered/grouped totals, just as in a non-embedded report.

## Independent band flow (default)

Place a reference at the lower edge of the containing band. Objects above it form the parent prefix; objects that must follow the subreport belong in a subsequent/Child band. The placeholder's height is an authoring handle, not an extra printed spacer. Target paper size and margins are not applied a second time: the reference width and the parent printable area determine its viewport.

Parallel references occupy non-overlapping horizontal lanes. Each lane independently paginates its own bands/rows against the same parent viewport schedule. One lane can use 26-point rows and the other 31-point rows without forcing mismatched row boundaries into a common cut. A lane that finishes does not repeat while another continues. The following parent band starts after the longest lane. Parent page/column headers and footers remain outside the lane; target page/data headers and footers remain inside that lane.

Continuation follows the parent page's columns, and child `PageN`, `TotalPages`, and `ColumnNumber` resolve to the actual parent output location. The existing bounded multipass preparation settles page-count-dependent layout. Nested independent subreports receive the same physical context and clip their output to their own lane.

Restrictions are explicit: target templates currently have one template column; the parent controls multi-column continuation. Lanes may not overlap horizontally. Regular objects below the first independent anchor are rejected instead of being silently overprinted. Align parallel anchors at the lower band edge; an anchor outside available printable space is diagnosed. Repeated parent headers/footers/overlays must use PrintOnParent rather than launch independent lanes.

## PrintOnParent

With `PrintOnParent = true`, child objects are composed into the containing band. The reference can grow or shrink according to `CanGrow` and `CanShrink`. `ShiftFollowing` moves objects anchored below its original bottom by the resulting displacement. Parallel size changes use the largest applicable displacement rather than adding every sibling's height. Fixed-size references clip child content to their bounds.

The parent band can split long composed content while retaining atomic unsplittable child bands and table/matrix rows. It evaluates the child at the parent band's page context; it does not re-evaluate embedded page-variable text for each later parent slice. Target page/column headers and footers appear once in this mode, rather than becoming parent-page decorations. Explicit target page breaks and horizontally tiled matrices are rejected in this mode; use independent flow for them.

Complex overlapping on-parent flows can have incompatible atomic boundaries. The engine diagnoses a non-progressing cut rather than looping or dropping rows. Independent parallel subreports are the appropriate mode when each lane needs its own break decisions. This implementation is not a claim of exhaustive FastReport PrintOnParent layout equivalence.

## Bounded preparation

`PreparationOptions.MaximumSubreportDepth` defaults to 16 (supported range 1–32), and `MaximumSubreportInvocations` defaults to 10,000. Invocation and band-work counters are shared by child runs, including layout lookahead/rebuild work, not reset for each sibling. Existing page/item limits also apply to composed output. Cancellation is checked during graph-safe preparation, source iteration, row expansion and composition. Imported references cannot load external files, execute scripts or create network/database connections.

## Designer and serialization

`DesignerSession.AddSubreport` inserts a reference and new target page as a single structural transaction. `OpenSubreportPage` navigates without adding history. Page renaming propagates to references in the same undoable operation. Nested band settings can be edited through the headless session API. Retargeting or pasting references cannot introduce a cycle or a missing target silently; rejected operations leave history and content unchanged.

`SubreportDefinitionEditor` is an independently reusable Uno control. It edits the target, mode, growing/shrinking flags and caller parameter expressions without mutating the input. The host applies its result through the session after validation. The application provides creation, editing and target-page navigation tools and two executable samples.

Native report JSON and same-document clipboard operations preserve definitions and parameter bindings. Clipboard paste regenerates object IDs but does not copy target pages across documents; the target must already exist. FRX subreport translation and FPX remain unimplemented and are not implied by native support.

## References

Public behavior references: [FastReport printing modes](https://www.fast-report.com/public_download/docs/FRNet/online/en/UserManual/en-US/DataBand/Subreports/SubreportPrintModes.html) and [SubreportObject API](https://www.fast-report.com/public_download/docs/FRNet/online/en/api/FastReport.SubreportObject.html). ReportScene's own API, serialization and documented limits are distinct from binary/API compatibility with FastReport.
