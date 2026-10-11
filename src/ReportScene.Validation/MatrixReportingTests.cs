using System.Globalization;
using System.Text;
using ReportScene.Engine;
using ReportScene.Export;
using ReportScene.Model;
using ReportScene.Serialization;

namespace ReportScene.Validation;

public static class MatrixReportingTests
{
    public static ReportDocument Fixture(int rowCount = 13, int columnCount = 5)
    {
        var data = new DataTable { Name = "Sales", Columns = ["Row", "Column", "Value"] };
        for (int r = 0; r < rowCount; r++) for (int c = 0; c < columnCount; c++)
            data.Rows.Add(new() { ["Row"] = "R" + r.ToString("D2"), ["Column"] = "C" + c.ToString("D2"), ["Value"] = r * 100 + c + 1 });
        var matrix = new MatrixItem
        {
            Name = "Grid", DataSource = "Sales", Bounds = new(0, 0, 300, 80), RowHeaderWidth = 100, CellWidth = 80,
            RowDimensions = [new() { Expression = "Sales.Row", Title = "Row" }], ColumnDimensions = [new() { Expression = "Sales.Column" }],
            Measures = [new() { Expression = "Sales.Value", Title = "Amount" }], ShowRowGrandTotal = false, ShowColumnGrandTotal = false,
            Style = new() { FontSize = 9, Padding = 2 }
        };
        return new()
        {
            Name = "Matrix pagination", Data = [data], Pages = [new()
            {
                Width = 340, Height = 240, Margins = new(20, 20, 20, 20), Bands =
                [new() { Kind = BandKind.ReportTitle, Name = "MatrixBand", Height = 0, Items = [matrix] },
                 new() { Kind = BandKind.PageFooter, Name = "Footer", Height = 20, Items = [new TextItem { Name = "Page", Text = "[PageN]/[TotalPages]", Bounds = new(0, 0, 300, 20), Style = new() { FontSize = 8 } }] }]
            }]
        };
    }
    private static MatrixItem Matrix(ReportDocument document) => (MatrixItem)document.Pages[0].Bands[0].Items[0];
    private static void Change(ReportDocument document, Func<MatrixItem, MatrixItem> change) => document.Pages[0].Bands[0].Items[0] = change(Matrix(document));
    private static List<PreparedItem> Values(PreparedReport report) => report.Pages.SelectMany(p => p.Items).Where(i => i.Source is TextItem && i.Source.Name == "Grid.Value").ToList();

    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action)
        { try { action(); return new("matrix reporting: " + name, true); } catch (Exception e) { return new("matrix reporting: " + name, false, e.ToString()); } }
        void Equal<T>(T expected, T actual) => Conformance.Equal(expected, actual);
        var preparer = new ReportPreparer();
        yield return Test("native polymorphic round-trip", () => { var d = ReportJson.Clone(Fixture()); Equal(13 * 5, d.Data[0].Rows.Count); Equal(80d, Matrix(d).CellWidth); Equal("Sales.Row", Matrix(d).RowDimensions[0].Expression); Equal(false, Matrix(d).ShowRowGrandTotal); });
        yield return Test("clipboard preserves definitions", () => { var m = Matrix(Fixture()); var copy = (MatrixItem)ReportJson.DeserializeItems(ReportJson.SerializeItems([m])).Single(); Equal(m.Id, copy.Id); Equal("Amount", copy.Measures[0].Title); });
        yield return Test("omitted matrix and nested defaults restored", () => { var m = (MatrixItem)ReportJson.DeserializeItems("[{\"$type\":\"matrix\",\"rowDimensions\":[{\"expression\":\"Sales.Row\"}],\"measures\":[{\"expression\":\"Sales.Value\"}]}]").Single(); Equal(90d, m.CellWidth); Equal(24d, m.RowHeight); Equal(true, m.RepeatHeaders); Equal(MatrixAggregate.Sum, m.Measures[0].Aggregate); Equal("", m.RowDimensions[0].Title); });
        yield return Test("explicit null dimension rejected", () => Conformance.Throws<FormatException>(() => ReportJson.DeserializeItems("[{\"$type\":\"matrix\",\"rowDimensions\":[null]}]")));
        yield return Test("unknown matrix property rejected", () => Conformance.Throws<FormatException>(() => ReportJson.DeserializeItems("[{\"$type\":\"matrix\",\"inventedOption\":true}]")));
        yield return Test("horizontal and vertical tiling page count", () => Equal(9, preparer.Prepare(Fixture()).Pages.Count));
        yield return Test("every data cell occurs exactly once", () => { var d = Fixture(); var values = Values(preparer.Prepare(d)); Equal(65, values.Count); Equal(65, values.Select(i => i.Source.Id).Distinct().Count()); Equal(string.Join(",", d.Data[0].Rows.Select(r => r["Value"].AsNumber()).Order()), string.Join(",", values.Select(v => decimal.Parse(v.Text, CultureInfo.InvariantCulture)).Order())); });
        yield return Test("column headers repeat on each continuation page", () => { var p = preparer.Prepare(Fixture()); Equal(true, p.Pages.All(page => page.Items.Any(i => i.Source is TextItem && i.Source.Name == "Grid.ColumnHeader"))); });
        yield return Test("row headers repeat on every horizontal tile", () => { var p = preparer.Prepare(Fixture()); Equal(39, p.Pages.SelectMany(page => page.Items).Count(i => i.Source is TextItem && i.Source.Name == "Grid.RowHeader")); });
        yield return Test("page numbers resolve after matrix pagination", () => { var p = preparer.Prepare(Fixture()); Equal(true, p.Pages.All(page => page.Items.Any(i => i.Source.Name == "Page" && i.Text == page.Number + "/9"))); });
        yield return Test("no zero-area prepared clips", () => Equal(false, preparer.Prepare(Fixture()).Pages.SelectMany(p => p.Items).Any(i => i.Clip is { } clip && (clip.Width <= 0 || clip.Height <= 0))));
        yield return Test("matrix cell text remains inside printable body", () => { var p = preparer.Prepare(Fixture()); Equal(true, Values(p).All(i => i.Bounds.Y >= 20 && i.Bounds.Bottom <= 200.001 && i.Bounds.X >= 20 && i.Bounds.Right <= 320.001)); });
        yield return Test("repeated headers can be disabled without losing cells", () => { var d = Fixture(); Change(d, m => m with { RepeatHeaders = false }); var p = preparer.Prepare(d); Equal(65, Values(p).Count); Equal(3, p.Pages.Count(page => page.Items.Any(i => i.Source.Name == "Grid.ColumnHeader"))); });
        yield return Test("oversized atomic row fails rather than looping", () => { var d = Fixture(1, 1); Change(d, m => m with { RowHeight = 500 }); Conformance.Throws<ReportPreparationException>(() => preparer.Prepare(d)); });
        yield return Test("too-narrow matrix fails explicitly", () => { var d = Fixture(1, 1); Change(d, m => m with { Bounds = m.Bounds with { Width = 150 } }); Conformance.Throws<ReportPreparationException>(() => preparer.Prepare(d)); });
        yield return Test("page limit includes horizontal tiles", () => Conformance.Throws<ReportPreparationException>(() => preparer.Prepare(Fixture(), options: new(MaximumPages: 2))));
        yield return Test("item limit checked during matrix expansion", () => Conformance.Throws<ReportPreparationException>(() => preparer.Prepare(Fixture(), options: new(MaximumItems: 20))));
        yield return Test("empty source produces a valid empty report", () => { var p = preparer.Prepare(Fixture(0, 1)); Equal(1, p.Pages.Count); Equal(0, Values(p).Count); Equal(true, p.Pages[0].Items.Any(i => i.Text == "No data")); });
        yield return Test("source report remains unchanged", () => { var d = Fixture(); string before = ReportJson.Serialize(d); preparer.Prepare(d); Equal(before, ReportJson.Serialize(d)); });
        yield return Test("matrix filter and parameter expressions", () => { var d = Fixture(3, 2); d.Parameters.Add(new("Factor", 2)); Change(d, m => m with { Filter = "Sales.Row == 'R01'", Measures = [new() { Expression = "Sales.Value * Parameters.Factor" }] }); Equal("202,204", string.Join(",", Values(preparer.Prepare(d)).Select(i => decimal.Parse(i.Text, CultureInfo.InvariantCulture)).Order())); });
        yield return Test("multiple measures survive tiling", () => { var d = Fixture(2, 3); Change(d, m => m with { Measures = [new() { Expression = "Sales.Value", Title = "Sum" }, new() { Expression = "Sales.Value", Title = "Count", Aggregate = MatrixAggregate.Count }] }); var values = Values(preparer.Prepare(d)); Equal(12, values.Count); Equal(6, values.Count(v => v.Text == "1") - 1); });
        yield return Test("percentage culture and formatting", () => { var d = Fixture(1, 2) with { Culture = "pl-PL" }; Change(d, m => m with { Measures = [new() { Expression = "Sales.Value", Display = MatrixDisplay.PercentOfRow }] }); var expected = (1m / 3).ToString("P2", CultureInfo.GetCultureInfo("pl-PL")); Equal(true, Values(preparer.Prepare(d)).Any(v => v.Text == expected)); });
        yield return Test("shared PDF exporter accepts expanded matrix", () => { var f = new ReportExporter().Export(preparer.Prepare(Fixture(2, 2)), ExportFormat.Pdf); Equal("%PDF", Encoding.ASCII.GetString(f.Content, 0, 4)); });
        yield return Test("shared SVG exporter accepts expanded matrix", () => { var f = new ReportExporter().Export(preparer.Prepare(Fixture(2, 2)), ExportFormat.Svg); Equal(true, Encoding.UTF8.GetString(f.Content).Contains("<svg")); });
        yield return Test("matrix in group header sees the group scope", () =>
        {
            var d = Fixture(2, 1); var m = Matrix(d) with { RowDimensions = [], ColumnDimensions = [] };
            d.Pages[0].Bands.Clear();
            d.Pages[0].Bands.Add(new() { Kind = BandKind.Data, DataSource = "Sales", Height = 0, Groups = [new() { Expression = "Sales.Row", Header = new() { Kind = BandKind.GroupHeader, Height = 0, Items = [m] }, Footer = new() { Kind = BandKind.GroupFooter, Height = 0 } }] });
            Equal("1,101", string.Join(",", Values(preparer.Prepare(d)).Select(v => v.Text)));
        });
        yield return Test("master detail matrix applies report relation", () =>
        {
            var d = Fixture(2, 1); var m = Matrix(d) with { RowDimensions = [], ColumnDimensions = [] };
            d.Data.Add(new() { Name = "Parents", Columns = ["Key"], Rows = [new() { ["Key"] = "R00" }, new() { ["Key"] = "R01" }] });
            d.Relations.Add(new("Parents", "Key", "Sales", "Row"));
            d.Pages[0].Bands.Clear(); d.Pages[0].Bands.Add(new() { Kind = BandKind.Data, DataSource = "Parents", Height = 0, Children = [new() { Kind = BandKind.Child, Height = 0, Items = [m] }] });
            Equal("1,101", string.Join(",", Values(preparer.Prepare(d)).Select(v => v.Text)));
        });
    }
}
