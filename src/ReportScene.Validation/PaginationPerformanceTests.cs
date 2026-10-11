using ReportScene.Engine;
using ReportScene.Model;

namespace ReportScene.Validation;

public static class PaginationPerformanceTests
{
    public static ReportDocument TableReport(int rows = 2000) => new()
    {
        Data = [new() { Name = "Rows", Columns = ["Label"], Rows = Enumerable.Range(0, rows).Select(n => new DataRow { ["Label"] = "Value " + n }).ToList() }],
        Pages = [new() { Width = 200, Height = 240, Margins = new(10, 10, 10, 10), Bands = [new()
        {
            Kind = BandKind.ReportTitle, Height = 0, Items = [new TableItem
            { Id = "table", Name = "Table", DataSource = "Rows", Bounds = new(0, 0, 180, 22), RowHeight = 22, Columns = [new("Heading", "[Rows.Label]", 180)] }]
        }] }]
    };
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("pagination performance: " + name, true); } catch (Exception e) { return new("pagination performance: " + name, false, e.ToString()); } }
        yield return Test("interval queries preserve arbitrary input order", () =>
        {
            var index = new VerticalIntervalIndex([(50d, 80d), (0d, 100d), (20d, 30d)]);
            Conformance.Equal("0,1,2", string.Join(',', index.Query(0, 100, out _)));
        });
        yield return Test("interval queries equal a brute-force reference", () =>
        {
            var random = new Random(331); var ranges = Enumerable.Range(0, 1000).Select(_ => { double top = random.Next(-500, 500); return (Top: top, Bottom: top + random.Next(0, 500)); }).ToArray();
            var index = new VerticalIntervalIndex(ranges);
            for (int n = 0; n < 100; n++)
            {
                double top = random.Next(-600, 900), bottom = top + random.Next(1, 100);
                var expected = ranges.Select((r, i) => (r, i)).Where(x => x.r.Top < bottom && x.r.Bottom > top && x.r.Bottom > x.r.Top).Select(x => x.i);
                Conformance.Equal(string.Join(',', expected), string.Join(',', index.Query(top, bottom, out _)));
            }
        });
        yield return Test("interval boundaries and empty ranges are exact", () =>
        {
            var index = new VerticalIntervalIndex([(0d, 10d), (10d, 10d), (10d, 20d)]);
            Conformance.Equal("2", string.Join(',', index.Query(10, 20, out _)));
            Conformance.Equal(0, index.Query(10, 10, out _).Count);
        });
        yield return Test("long spanning intervals coexist with narrow-row lookup", () =>
        {
            var ranges = new List<(double, double)> { (0, 2000000) }; ranges.AddRange(Enumerable.Range(0, 100000).Select(n => ((double)n * 20, n * 20d + 18)));
            var index = new VerticalIntervalIndex(ranges); var hits = index.Query(1000000, 1000200, out int examined);
            Conformance.Equal(11, hits.Count); Conformance.Equal(0, hits[0]); Conformance.Equal(true, examined < 96);
        });
        yield return Test("invalid intervals and pre-cancelled queries are rejected", () =>
        {
            Conformance.Throws<ArgumentException>(() => new VerticalIntervalIndex([(2d, 1d)]));
            Conformance.Throws<ArgumentException>(() => new VerticalIntervalIndex([(0d, double.NaN)]));
            var index = new VerticalIntervalIndex([(0d, 10d)]); Conformance.Throws<OperationCanceledException>(() => index.Query(0, 10, out _, new(true)));
        });
        yield return Test("large table keeps each row once and repeats each physical-page header", () =>
        {
            var report = new ReportPreparer().Prepare(TableReport()); var text = report.Pages.SelectMany(p => p.Items).Where(i => i.Source is TextItem).ToArray();
            Conformance.Equal(223, report.Pages.Count); Conformance.Equal(2000, text.Count(i => i.Text.StartsWith("Value ")));
            Conformance.Equal(2000, text.Where(i => i.Text.StartsWith("Value ")).Select(i => i.Text).Distinct().Count());
            Conformance.Equal(223, text.Count(i => i.Text == "Heading"));
            Conformance.Equal(true, report.Pages.All(p => p.Items.Where(i => i.Source is TextItem).All(i => i.Bounds.Y >= 10 && i.Bounds.Bottom <= 230.001)));
        });
        yield return Test("large growing text retains every line across indexed slices", () =>
        {
            var lines = Enumerable.Range(0, 1000).Select(n => "Line " + n).ToArray();
            var document = new ReportDocument { Pages = [new() { Width = 200, Height = 240, Margins = new(10, 10, 10, 10), Bands = [new()
            { Kind = BandKind.ReportTitle, Height = 0, Items = [new TextItem { CanGrow = true, Text = string.Join('\n', lines), Bounds = new(0, 0, 180, 20) }] }] }] };
            var report = new ReportPreparer().Prepare(document);
            Conformance.Equal(string.Join('|', lines), string.Join('|', report.Pages.SelectMany(p => p.Items).Where(i => i.Source is TextItem).Select(i => i.Text)));
        });
    }
}
