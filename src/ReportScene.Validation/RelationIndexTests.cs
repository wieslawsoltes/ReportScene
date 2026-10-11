using ReportScene.Engine;
using ReportScene.Model;
using ReportScene.Serialization;

namespace ReportScene.Validation;

public static class RelationIndexTests
{
    public static ReportDocument Fixture(int parents = 40, int children = 1200, bool subreport = false)
    {
        var detail = new Band { Name = "Details", Kind = BandKind.Data, DataSource = "Details", Height = 20,
            Items = [new TextItem { Name = "DetailText", Text = "[Parent.Id]:[Details.Label]:[RowNumber]", Bounds = new(0, 0, 300, 20) }] };
        var master = new Band { Name = "Masters", Kind = BandKind.Data, DataSource = "Masters", Height = 24 };
        if (subreport) master.Items.Add(new SubreportItem { PageName = "DetailPage", Bounds = new(0, 0, 320, 24) });
        else master.Children.Add(detail);
        var report = new ReportDocument { Name = "Relation index", Data = [
            new() { Name = "Masters", Columns = ["Id"], Rows = Enumerable.Range(0, parents).Select(n => new DataRow { ["Id"] = n }).ToList() },
            new() { Name = "Details", Columns = ["MasterId", "Label"], Rows = Enumerable.Range(0, children).Select(n => new DataRow { ["MasterId"] = n % parents, ["Label"] = "Value" + n }).ToList() }],
            Relations = [new("Masters", "Id", "Details", "MasterId")], Pages = [new() { Bands = [master] }] };
        if (subreport) report.Pages.Add(new() { Name = "DetailPage", Bands = [detail] });
        return report;
    }
    private static PreparedReport Prepare(ReportDocument document, bool indexed) => new ReportPreparer().Prepare(document,
        options: new(ReportDate: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)) { EnableRelationIndexes = indexed });
    private static string Output(PreparedReport report) => string.Join('|', report.Pages.SelectMany(p => p.Items.Select(i => $"{p.Number}:{i.Source.Id}:{i.Bounds}:{i.Clip}:{i.Text}")));
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("relation index: " + name, true); } catch (Exception e) { return new("relation index: " + name, false, e.ToString()); } }
        yield return Test("homogeneous numeric lookup retains duplicate order and decimal scale equality", () =>
        {
            DataRow[] rows = [new() { ["Id"] = 1m }, new() { ["id"] = 2 }, new() { ["Id"] = 1.00m }];
            var index = new DataKeyIndex(rows, "ID"); Conformance.Equal("0,2", string.Join(',', index.Find(1, out int examined))); Conformance.Equal(2, examined);
        });
        yield return Test("missing fields do not match explicit null", () =>
        {
            var index = new DataKeyIndex([new(), new() { ["Id"] = Value.Null }, new() { ["Id"] = "" }], "Id");
            Conformance.Equal("1", string.Join(',', index.Find(Value.Null, out _))); Conformance.Equal("2", string.Join(',', index.Find("", out _)));
        });
        yield return Test("mixed numeric string boolean values preserve coercing comparison", () =>
        {
            Value[] values = [1, "1", "01", true, false, 0, Value.Null];
            var rows = values.Select(v => new DataRow { ["Key"] = v }).ToArray(); var index = new DataKeyIndex(rows, "Key");
            foreach (var query in new Value[] { 1, "1", "01", true, Value.Null })
            {
                var expected = values.Select((v, n) => (v, n)).Where(e => Value.Compare(e.v, query) == 0).Select(e => e.n);
                Conformance.Equal(string.Join(',', expected), string.Join(',', index.Find(query, out _)));
            }
            Conformance.Equal(false, index.CanUseLookup(1));
        });
        yield return Test("incompatible numeric coercion is not silently converted to no match", () =>
        {
            var index = new DataKeyIndex([new() { ["Key"] = "not a number" }], "Key");
            Conformance.Throws<FormatException>(() => index.Find(1, out _));
        });
        yield return Test("keys are snapshots, not mutable live dictionary lookups", () =>
        {
            var row = new DataRow { ["Id"] = 1 }; var index = new DataKeyIndex([row], "Id"); row["Id"] = 2;
            Conformance.Equal(1, index.Find(1, out _).Count); Conformance.Equal(0, index.Find(2, out _).Count);
        });
        yield return Test("large repeated lookups examine only matching rows", () =>
        {
            var rows = Enumerable.Range(0, 100000).Select(n => new DataRow { ["Id"] = n % 1000 }).ToArray();
            var index = new DataKeyIndex(rows, "Id"); long candidates = 0;
            for (int n = 0; n < 1000; n++) { Conformance.Equal(100, index.Find(n, out int count).Count); candidates += count; }
            Conformance.Equal(100000L, candidates);
        });
        yield return Test("construction and lookup observe cancellation", () =>
        {
            Conformance.Throws<OperationCanceledException>(() => new DataKeyIndex([], "Id", new(true)));
            var index = new DataKeyIndex([], "Id"); Conformance.Throws<OperationCanceledException>(() => index.Find(0, out _, new(true)));
        });
        foreach (bool subreport in new[] { false, true })
            yield return Test($"complete preparation matches scan path, subreport={subreport}", () =>
            {
                var d = Fixture(subreport: subreport); string original = ReportJson.Serialize(d);
                Conformance.Equal(Output(Prepare(d, false)), Output(Prepare(d, true))); Conformance.Equal(original, ReportJson.Serialize(d));
            });
        yield return Test("relation filtering preserves original source RowNumber for filters and sort", () =>
        {
            var d = Fixture(); var detail = d.Pages[0].Bands[0].Children[0];
            d.Pages[0].Bands[0].Children[0] = detail with { Filter = "RowNumber > 60", Sort = [new("RowNumber", true)] };
            Conformance.Equal(Output(Prepare(d, false)), Output(Prepare(d, true)));
        });
        yield return Test("mixed keys keep filtering and error evaluation order", () =>
        {
            var d = Fixture(1, 100); d.Data[1].Rows[1]["MasterId"] = "bad";
            d.Pages[0].Bands[0].Children[0] = d.Pages[0].Bands[0].Children[0] with { Filter = "MissingField" };
            string Error(bool index) { try { Prepare(d, index); return "no error"; } catch (Exception e) { return e.GetType().Name + ":" + e.Message; } }
            Conformance.Equal(Error(false), Error(true)); Conformance.Equal(true, Error(true).Contains("MissingField"));
        });
        yield return Test("another preparation sees edited source keys", () =>
        {
            var d = Fixture(2, 100); var preparer = new ReportPreparer(); preparer.Prepare(d);
            d.Data[1].Rows[0]["MasterId"] = 1;
            Conformance.Equal(Output(Prepare(d, false)), Output(Prepare(d, true)));
        });
    }
}
