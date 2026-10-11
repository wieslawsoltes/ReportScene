using ReportScene.Engine;
using ReportScene.Model;

namespace ReportScene.Validation;

public static class SubreportPhysicalPageTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action)
        { try { action(); return new("subreport physical pages: " + name, true); } catch (Exception e) { return new("subreport physical pages: " + name, false, e.ToString()); } }
        ReportDocument Columns(int left = 10, int right = 7)
        {
            var d = SubreportTests.Parallel(left, right); d.Pages[0] = d.Pages[0] with { Width = 1000, Columns = 2, ColumnGap = 20 }; return d;
        }
        Band TextBand(string name, bool pageBreak) => new()
        {
            Name = name, Kind = BandKind.ReportTitle, Height = 26, StartNewPage = pageBreak,
            Items = [new TextItem { Name = name, Text = name + ":[PageN]:[ColumnNumber]", Bounds = new(0, 0, 180, 20), Style = new() { FontSize = 9, Padding = 1 } }]
        };
        ReportDocument Break()
        {
            var d = Columns(1, 0); d.Pages[1].Bands.Clear(); d.Pages[1].Bands.Add(TextBand("First", false)); d.Pages[1].Bands.Add(TextBand("Second", true)); return d;
        }
        yield return Test("page budget counts physical pages instead of child column fragments", () =>
        {
            var p = new ReportPreparer().Prepare(Columns(), options: new(MaximumPages: 1)); Conformance.Equal(1, p.Pages.Count);
            Conformance.Equal(10, p.Pages[0].Items.Count(i => i.Source.Name == "AlphaValue"));
        });
        yield return Test("explicit child break skips the remaining parent column", () =>
        {
            var p = new ReportPreparer().Prepare(Break()); Conformance.Equal(2, p.Pages.Count);
            Conformance.Equal("First:1:1", p.Pages[0].Items.Single(i => i.Source.Name == "First").Text);
            Conformance.Equal("Second:2:1", p.Pages[1].Items.Single(i => i.Source.Name == "Second").Text);
        });
        yield return Test("explicit break still enforces the physical page budget", () =>
            Conformance.Throws<ReportPreparationException>(() => new ReportPreparer().Prepare(Break(), options: new(MaximumPages: 1))));
        yield return Test("independent sibling can use columns skipped by the other lane", () =>
        {
            var d = Break(); var source = SubreportTests.Parallel(0, 7); d.Data[1].Rows.AddRange(source.Data[1].Rows);
            var p = new ReportPreparer().Prepare(d);
            Conformance.Equal(7, p.Pages.SelectMany(page => page.Items).Count(i => i.Source.Name == "BetaValue"));
            Conformance.Equal(true, p.Pages[0].Items.Any(i => i.Source.Name == "BetaValue" && i.Text.EndsWith(" / 2")));
            Conformance.Equal(true, p.Pages[1].Items.Any(i => i.Source.Name == "Second"));
        });
        yield return Test("horizontal matrix tiles in a child start on physical pages", () =>
        {
            var d = Columns(0, 0); var matrixFixture = MatrixReportingTests.Fixture(2, 3); d.Data.Add(matrixFixture.Data[0]);
            var matrix = (MatrixItem)matrixFixture.Pages[0].Bands[0].Items[0];
            d.Pages[1].Bands.Clear(); d.Pages[1].Bands.Add(new() { Kind = BandKind.ReportTitle, Height = 0,
                Items = [matrix with { Bounds = new(0, 0, 210, 80), RowHeaderWidth = 80, CellWidth = 80 }] });
            var p = new ReportPreparer().Prepare(d); Conformance.Equal(3, p.Pages.Count);
            Conformance.Equal(6, p.Pages.SelectMany(page => page.Items).Count(i => i.Source is TextItem && i.Source.Name == "Grid.Value"));
        });
        yield return Test("nested child breaks retain the outer physical page schedule", () =>
        {
            var d = Break(); var nested = d.Pages[1] with { Name = "Deep" };
            d.Pages[1] = new() { Name = "LeftPage", Bands = [new() { Kind = BandKind.ReportTitle, Height = 24,
                Items = [new SubreportItem { PageName = "Deep", Bounds = new(0, 0, 210, 24) }] }] }; d.Pages.Add(nested);
            var p = new ReportPreparer().Prepare(d); Conformance.Equal(2, p.Pages.Count);
            Conformance.Equal("Second:2:1", p.Pages[1].Items.Single(i => i.Source.Name == "Second").Text);
        });
    }
}
