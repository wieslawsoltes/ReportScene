using System.Text;
using System.Text.RegularExpressions;
using ReportScene.Export;
using ReportScene.Model;

namespace ReportScene.Validation;

public static class ReportPrintTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action body) { try { body(); return new("print document: " + name, true); } catch (Exception e) { return new("print document: " + name, false, e.ToString()); } }
        PreparedReport Fixture() => new("Print <range> & report", new[] { 0, 7, 7, 20 }.Select((n, i) => new PreparedPage(n, 100 + i, 200 + i,
            [new(new TextItem(), new(10, 10, 70, 20), "Position " + (i + 1))])).ToArray(), [], TimeSpan.Zero);
        string Html(PreparedReport report, ReportPageSelection selection, bool reverse = false) =>
            Encoding.UTF8.GetString(new ReportExporter().CreatePrintDocument(report, selection, reverse).Content);
        int[] Numbers(string html) => Regex.Matches(html, "aria-label=\"Page (-?[0-9]+)\"").Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        yield return Test("all displayed positions include zero and repeated physical page numbers", () =>
        { Conformance.Equal(true, Numbers(Html(Fixture(), ReportPageSelection.Create(4))).SequenceEqual([0, 7, 7, 20])); });
        yield return Test("current page is a displayed ordinal not a printed label", () =>
        {
            string text = Html(Fixture(), ReportPageSelection.Create(4, PageSelectionMode.Current, 2));
            Conformance.Equal(true, Numbers(text).SequenceEqual([7])); Conformance.Equal(true, text.Contains("width:102pt;height:202pt"));
        });
        yield return Test("custom ranges coalesce and preserve original report order", () =>
        { Conformance.Equal(true, Numbers(Html(Fixture(), ReportPageSelection.Create(4, PageSelectionMode.Custom, pageNumbers: "4,1-2,2"))).SequenceEqual([0, 7, 20])); });
        yield return Test("reverse applies after deduplication and does not renumber", () =>
        { Conformance.Equal(true, Numbers(Html(Fixture(), ReportPageSelection.Create(4, PageSelectionMode.Custom, pageNumbers: "4,1-2,2"), true)).SequenceEqual([20, 7, 0])); });
        yield return Test("reverse does not mutate the source page list or objects", () =>
        {
            var report = Fixture(); var original = report.Pages.ToArray();
            _ = Html(report, ReportPageSelection.Create(4), true);
            Conformance.Equal(true, report.Pages.SequenceEqual(original));
            Conformance.Equal(true, report.Pages.Select((p, i) => ReferenceEquals(p, original[i])).All(same => same));
        });
        yield return Test("print document retains export escaping and print page breaks", () =>
        {
            var file = new ReportExporter().CreatePrintDocument(Fixture(), ReportPageSelection.Create(4));
            string html = Encoding.UTF8.GetString(file.Content);
            Conformance.Equal("text/html", file.MediaType); Conformance.Equal(true, file.Name.EndsWith(".html", StringComparison.Ordinal));
            Conformance.Equal(true, html.Contains("Print &lt;range&gt; &amp; report"));
            Conformance.Equal(true, html.Contains("@media print") && html.Contains("page-break-after:always") && html.Contains(".page:last-child"));
        });
        yield return Test("empty stale and cancelled selections do not produce a print document", () =>
        {
            var exporter = new ReportExporter();
            Conformance.Throws<ArgumentException>(() => exporter.CreatePrintDocument(Fixture(), ReportPageSelection.Create(3)));
            Conformance.Throws<ArgumentException>(() => exporter.CreatePrintDocument(new("Empty", [], [], TimeSpan.Zero), ReportPageSelection.Create(0)));
            using var c = new CancellationTokenSource(); c.Cancel();
            Conformance.Throws<OperationCanceledException>(() => exporter.CreatePrintDocument(Fixture(), ReportPageSelection.Create(4), cancellationToken: c.Token));
        });
        yield return Test("reports with errors cannot reach the printer", () =>
        {
            var report = Fixture() with { Diagnostics = [new("PRINT_TEST", "Invalid report", DiagnosticSeverity.Error)] };
            Conformance.Throws<InvalidOperationException>(() => new ReportExporter().CreatePrintDocument(report, ReportPageSelection.Create(4)));
        });
    }
}
