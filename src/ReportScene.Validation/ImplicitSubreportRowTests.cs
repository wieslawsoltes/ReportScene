using ReportScene.Engine;
using ReportScene.Model;
using ReportScene.Serialization;

namespace ReportScene.Validation;

public static class ImplicitSubreportRowTests
{
    public static ReportDocument Fixture(bool inline, bool nested = false, bool boundCaller = false, bool explicitTarget = false)
    {
        SubreportItem Sub(string target) => new() { PageName = target, PrintOnParent = inline, Bounds = new(0, 0, 240, 24) };
        var rows = new DataTable { Name = "Rows", Columns = ["Id"], Rows = [new() { ["Id"] = 1 }, new() { ["Id"] = 2 }, new() { ["Id"] = 3 }] };
        var target = new ReportPage { Name = "Detail", Bands = [new()
        {
            Kind = BandKind.Data, Height = 24, DataSource = explicitTarget ? "Rows" : null,
            Items = [new TextItem { Name = "Implicit", Text = boundCaller ? "[Rows.Id]:[RowNumber]" : "Print once", Bounds = new(0, 0, 230, 20) }]
        }] };
        var main = new ReportPage { Name = "Main", Bands = [new()
        {
            Kind = boundCaller ? BandKind.Data : BandKind.ReportTitle, DataSource = boundCaller ? "Rows" : null,
            Height = 24, Items = [Sub(nested ? "Middle" : "Detail")]
        }] };
        var document = new ReportDocument { Name = "Implicit row regression", Data = [rows], Pages = [main, target] };
        if (nested) document.Pages.Add(new() { Name = "Middle", Bands = [new() { Kind = BandKind.ReportTitle, Height = 24, Items = [Sub("Detail")] }] });
        return document;
    }
    private static string Output(ReportDocument document) => string.Join(',', new ReportPreparer().Prepare(document).Pages
        .SelectMany(p => p.Items).Where(i => i.Source is TextItem && i.Source.Name == "Implicit").Select(i => i.Text));
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("implicit subreport rows: " + name, true); } catch (Exception e) { return new("implicit subreport rows: " + name, false, e.ToString()); } }
        foreach (bool inline in new[] { false, true }) foreach (bool nested in new[] { false, true })
        {
            yield return Test($"root caller, inline={inline}, nested={nested}", () =>
                Conformance.Equal("Print once", Output(Fixture(inline, nested))));
            yield return Test($"one implicit row per bound invocation, inline={inline}, nested={nested}", () =>
                Conformance.Equal("1:1,2:1,3:1", Output(Fixture(inline, nested, true))));
        }
        yield return Test("explicit same-table source still inherits all caller rows", () =>
            Conformance.Equal("1:1,2:2,3:3,1:1,2:2,3:3,1:1,2:2,3:3", Output(Fixture(false, false, true, true))));
        yield return Test("implicit row still obeys its filter", () =>
        {
            var d = Fixture(false); d.Pages[1].Bands[0] = d.Pages[1].Bands[0] with { Filter = "false" };
            Conformance.Equal("", Output(d));
        });
        yield return Test("native roundtrip and preparation retain caller data", () =>
        {
            var d = Fixture(true, true, true); string before = ReportJson.Serialize(d);
            Conformance.Equal("1:1,2:1,3:1", Output(ReportJson.Clone(d))); Conformance.Equal(before, ReportJson.Serialize(d));
        });
    }
}
