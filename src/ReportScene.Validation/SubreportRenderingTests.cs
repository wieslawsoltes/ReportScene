using System.Text;
using ReportScene.Engine;
using ReportScene.Export;
using ReportScene.Model;
using ReportScene.Rendering.Skia;
using ReportScene.Samples;
using ReportScene.Serialization;
using SkiaSharp;

namespace ReportScene.Validation;

public static class SubreportRenderingTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action)
        { try { action(); return new("subreport rendering: " + name, true); } catch (Exception e) { return new("subreport rendering: " + name, false, e.ToString()); } }
        void Equal<T>(T expected, T actual) => Conformance.Equal(expected, actual);
        yield return Test("authoring representation uses the shared renderer", () =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(240, 60)); using var renderer = new ReportRenderer(); surface.Canvas.Clear(SKColors.White);
            var item = new SubreportItem { PageName = "Detail", Bounds = new(0, 0, 200, 40) }; renderer.DrawItem(surface.Canvas, new(item, item.Bounds), true);
            using var image = surface.Snapshot(); using var bitmap = SKBitmap.FromImage(image); Equal(false, bitmap.GetPixel(5, 5) == SKColors.White);
        });
        yield return Test("raw references cannot masquerade as prepared content", () =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(100, 100)); using var renderer = new ReportRenderer(); var item = new SubreportItem();
            Conformance.Throws<ReportRenderException>(() => renderer.DrawItem(surface.Canvas, new(item, item.Bounds)));
        });
        foreach (var name in new[] { "Dashboard", "Master-detail" })
        {
            ReportDocument Sample() => name == "Dashboard" ? SubreportSampleReports.Dashboard() : SubreportSampleReports.MasterDetail();
            yield return Test(name + " sample validates and survives native round-trip", () =>
            {
                var document = ReportJson.Clone(Sample()); Equal(0, DocumentValidator.Validate(document).Count);
                Equal(1, SubreportGraph.PrintablePages(document).Count);
            });
            yield return Test(name + " sample preserves every source row with font-backed metrics", () =>
            {
                using var renderer = new ReportRenderer(); var document = Sample(); var report = new ReportPreparer(renderer.Metrics).Prepare(document);
                var texts = report.Pages.SelectMany(p => p.Items).Where(i => i.Source is TextItem).ToArray();
                if (name == "Dashboard") { Equal(28, texts.Count(i => i.Source.Name == "OrderNumber")); Equal(17, texts.Count(i => i.Source.Name == "TaskTitle")); }
                else { Equal(24, texts.Count(i => i.Source.Name == "Reference")); Equal(3, texts.Count(i => i.Source.Name == "AccountEnd")); }
                Equal(false, report.Pages.SelectMany(p => p.Items).Any(i => i.Source is SubreportItem));
                Equal(true, report.Pages.Count > 1);
            });
            yield return Test(name + " prepared content exports through PDF SVG PNG", () =>
            {
                using var renderer = new ReportRenderer(); var report = new ReportPreparer(renderer.Metrics).Prepare(Sample()); var exporter = new ReportExporter();
                var pdf = exporter.Export(report, ExportFormat.Pdf); Equal("%PDF", Encoding.ASCII.GetString(pdf.Content, 0, 4));
                var svg = exporter.Export(report, ExportFormat.Svg); Equal(true, svg.Name.EndsWith(".zip", StringComparison.Ordinal) || Encoding.UTF8.GetString(svg.Content).Contains("<svg"));
                var png = ReportExporter.Png(renderer, report.Pages[0], new(Dpi: 72)); Equal((byte)137, png[0]); Equal("PNG", Encoding.ASCII.GetString(png, 1, 3));
            });
        }
        yield return Test("runtime nesting budget is shared across child runs", () =>
        {
            var d = SubreportTests.Parallel(1, 0); var nested = d.Pages[1] with { Name = "Deep" };
            d.Pages[1] = new() { Name = "LeftPage", Bands = [new() { Kind = BandKind.ReportTitle, Items = [new SubreportItem { PageName = "Deep", Bounds = new(0, 0, 210, 24) }] }] }; d.Pages.Add(nested);
            Conformance.Throws<ReportPreparationException>(() => new ReportPreparer().Prepare(d, options: new(MaximumSubreportDepth: 1)));
        });
        yield return Test("target page headers and footers occupy their own lane only", () =>
        {
            var d = SubreportTests.Parallel(10, 1);
            d.Pages[1].Bands.Insert(0, new() { Kind = BandKind.PageHeader, Height = 20, Items = [new TextItem { Name = "TargetHeader", Text = "Child [PageN]", Bounds = new(0, 0, 180, 20) }] });
            d.Pages[1].Bands.Add(new() { Kind = BandKind.PageFooter, Height = 20, Items = [new TextItem { Name = "TargetFooter", Text = "Child footer", Bounds = new(0, 0, 180, 20) }] });
            var p = new ReportPreparer().Prepare(d); var items = p.Pages.SelectMany(page => page.Items).ToArray();
            Equal(10, items.Count(i => i.Source.Name == "AlphaValue")); Equal(1, items.Count(i => i.Source.Name == "BetaValue"));
            Equal(true, items.Where(i => i.Source.Name == "TargetFooter").All(i => i.Bounds.Right <= 230 && i.Bounds.Bottom <= 220.001));
            Equal(true, items.Count(i => i.Source.Name == "TargetHeader") >= 2);
        });
    }
}
