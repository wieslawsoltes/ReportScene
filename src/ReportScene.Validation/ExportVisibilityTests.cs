using System.Text;
using ReportScene.Export;
using ReportScene.Model;
using ReportScene.Rendering.Skia;

namespace ReportScene.Validation;

public static class ExportVisibilityTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("export visibility: " + name, true); } catch (Exception e) { return new("export visibility: " + name, false, e.ToString()); } }
        PreparedReport Report(params PreparedItem[] items) => new("Visibility", [new(1, 100, 100, items)], [], TimeSpan.Zero);
        PreparedItem Text(string text, RectD bounds, RectD? clip = null, string? link = null) => new(new TextItem { Hyperlink = link }, bounds, text, Clip: clip);
        string Export(PreparedReport report, ExportFormat format) => Encoding.UTF8.GetString(new ReportExporter().Export(report, format).Content);
        yield return Test("fully clipped text is absent from plain-text output", () =>
        {
            var report = Report(Text("hidden", new(0, 0, 50, 20), new(0, 30, 50, 20)), Text("visible", new(0, 40, 50, 20)));
            Conformance.Equal("visible", Export(report, ExportFormat.Text));
        });
        yield return Test("off-paper text is absent while visible reading order is retained", () =>
        {
            var report = Report(Text("second", new(0, 40, 50, 20)), Text("outside", new(120, 0, 50, 20)), Text("first", new(0, 0, 50, 20)));
            Conformance.Equal("first\nsecond", Export(report, ExportFormat.Text));
        });
        yield return Test("HTML link hit areas use the clipped intersection", () =>
        {
            string html = Export(Report(Text("visible", new(0, 0, 40, 20), new(10, 5, 20, 10), "https://example.com/visible")), ExportFormat.Html);
            Conformance.Equal(true, html.Contains("href=\"https://example.com/visible\""));
            Conformance.Equal(true, html.Contains("left:10pt;top:5pt;width:20pt;height:10pt"));
        });
        yield return Test("HTML omits off-paper and fully clipped link annotations", () =>
        {
            var report = Report(Text("hidden", new(0, 0, 50, 20), new(0, 30, 50, 20), "https://example.com/hidden"),
                Text("outside", new(0, 120, 50, 20), link: "https://example.com/outside"));
            string html = Export(report, ExportFormat.Html);
            Conformance.Equal(false, html.Contains("<a class=\"link\""));
        });
        yield return Test("PDF omits invisible URL annotations", () =>
        {
            var report = Report(Text("hidden", new(0, 0, 50, 20), new(0, 30, 50, 20), "https://example.com/hidden"));
            var bytes = new ReportExporter().Export(report, ExportFormat.Pdf).Content;
            Conformance.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
            Conformance.Equal(false, Encoding.Latin1.GetString(bytes).Contains("/URI"));
        });
        yield return Test("direct raster export validates NaN resolution before native allocation", () =>
        {
            using var renderer = new ReportRenderer();
            Conformance.Throws<ArgumentOutOfRangeException>(() => ReportExporter.Png(renderer, new(1, 100, 100, []), new(Dpi: double.NaN)));
        });
        yield return Test("direct vector export rejects unrepresentable paper geometry", () =>
        {
            using var renderer = new ReportRenderer();
            Conformance.Throws<ArgumentException>(() => ReportExporter.Svg(renderer, new(1, double.PositiveInfinity, 100, [])));
            Conformance.Throws<ArgumentException>(() => ReportExporter.Svg(renderer, new(1, double.MaxValue, 100, [])));
        });
        yield return Test("export rejects invalid prepared dimensions before entering the backend", () =>
        {
            var report = new PreparedReport("Invalid", [new(1, double.NaN, 100, [])], [], TimeSpan.Zero);
            Conformance.Throws<ArgumentException>(() => new ReportExporter().Export(report, ExportFormat.Pdf));
        });
    }
}
