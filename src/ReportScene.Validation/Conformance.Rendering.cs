using System.IO.Compression;
using System.Text;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using ReportScene.Model;
using ReportScene.Engine;
using ReportScene.Export;
using ReportScene.Rendering.Skia;
using ReportScene.Samples;
using SkiaSharp;
using ZXing;

namespace ReportScene.Validation;

[JsonSerializable(typeof(List<TestResult>))]
public partial class ValidationJsonContext : System.Text.Json.Serialization.JsonSerializerContext;

public static partial class Conformance
{
    static partial void AdditionalTests(Action<string, Action> test)
    {
        test("font-backed metrics are positive", () => { using var m = new SkiaTextMeasurer(); Equal(true, m.MeasureWidth("ReportScene", new()) > 0); });
        test("font-backed metrics distinguish glyph widths", () => { using var m = new SkiaTextMeasurer(); Equal(true, m.MeasureWidth("WWW", new()) > m.MeasureWidth("iii", new())); });
        test("HarfBuzz combining text measures", () => { using var m = new SkiaTextMeasurer(); Equal(true, m.MeasureWidth("Zażółć gęślą jaźń · café · a\u0301", new()) > 0); });
        foreach (var name in SampleReports.Names)
        {
            test("Skia PNG rendering: " + name, () =>
            {
                using var renderer = new ReportRenderer();
                var report = new ReportPreparer(renderer.Metrics).Prepare(SampleReports.Create(name));
                var png = ReportExporter.Png(renderer, report.Pages[0], new(Dpi: 72));
                using var bitmap = SKBitmap.Decode(png);
                Equal(true, bitmap is not null && bitmap.Width > 100 && bitmap.Height > 100);
                int nonwhite = 0;
                for (int y = 0; y < bitmap!.Height; y += 3) for (int x = 0; x < bitmap.Width; x += 3) if (bitmap.GetPixel(x, y) != SKColors.White) nonwhite++;
                Equal(true, nonwhite > 100);
            });
        }
        foreach (var kind in Enum.GetValues<BarcodeKind>())
        {
            test("barcode encoder: " + kind, () => { using var r = new ReportRenderer(); var matrix = r.EncodeBarcode(kind, kind == BarcodeKind.Ean13 ? "5901234123457" : "REPORT123"); Equal(true, matrix.Width > 1 && matrix.Height > 0); });
        }
        test("invalid EAN-13 is rejected", () => { using var r = new ReportRenderer(); Throws<ArgumentException>(() => r.EncodeBarcode(BarcodeKind.Ean13, "ABC")); });
        test("QR round-trip decoding", () =>
        {
            using var renderer = new ReportRenderer();
            var code = new BarcodeItem { Symbology = BarcodeKind.QrCode, Text = "ReportScene QR validation", ShowText = false, Bounds = new(0, 0, 256, 256) };
            var page = new PreparedPage(1, 256, 256, [new(code, code.Bounds, code.Text)]);
            var bytes = ReportExporter.Png(renderer, page, new(Dpi: 72));
            using var bitmap = SKBitmap.Decode(bytes);
            byte[] pixels = new byte[bitmap.Width * bitmap.Height * 4];
            for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++)
            { var color = bitmap.GetPixel(x, y); int offset = (y * bitmap.Width + x) * 4; pixels[offset] = color.Red; pixels[offset + 1] = color.Green; pixels[offset + 2] = color.Blue; pixels[offset + 3] = color.Alpha; }
            var decoded = new BarcodeReaderGeneric().Decode(pixels, bitmap.Width, bitmap.Height, RGBLuminanceSource.BitmapFormat.RGBA32);
            Equal(code.Text, decoded?.Text);
        });
        test("PDF export signature and trailer", () =>
        {
            var doc = SampleReports.Invoice(); using var metrics = new SkiaTextMeasurer();
            var file = new ReportExporter().Export(new ReportPreparer(metrics).Prepare(doc), ExportFormat.Pdf);
            var text = Encoding.Latin1.GetString(file.Content);
            Equal(true, text.StartsWith("%PDF-", StringComparison.Ordinal)); Equal(true, text.TrimEnd().EndsWith("%%EOF", StringComparison.Ordinal)); Equal(true, file.Content.Length > 1000);
        });
        test("SVG export is well-formed XML", () => { var doc = SampleReports.Invoice(); using var metrics = new SkiaTextMeasurer(); var file = new ReportExporter().Export(new ReportPreparer(metrics).Prepare(doc), ExportFormat.Svg); Equal("svg", XDocument.Parse(Encoding.UTF8.GetString(file.Content)).Root!.Name.LocalName); });
        test("multi-page image export is a ZIP", () => { var doc = SampleReports.GroupedSales(); using var metrics = new SkiaTextMeasurer(); var report = new ReportPreparer(metrics).Prepare(doc); var file = new ReportExporter().Export(report, ExportFormat.Png, new(Dpi: 36)); using var zip = new ZipArchive(new MemoryStream(file.Content)); Equal(report.Pages.Count, zip.Entries.Count); });
        test("HTML export escapes title", () => { var report = new PreparedReport("<script>alert(1)</script>", [new(1, 100, 100, [])], [], TimeSpan.Zero); string html = Encoding.UTF8.GetString(new ReportExporter().Export(report, ExportFormat.Html).Content); Equal(false, html.Contains("<script>")); Equal(true, html.Contains("&lt;script&gt;")); });
        test("HTML export refuses executable hyperlinks", () => { var item = new TextItem { Text = "Click", Hyperlink = "javascript:alert(1)" }; var report = new PreparedReport("Links", [new(1, 200, 200, [new(item, item.Bounds, "Click")])], [], TimeSpan.Zero); string html = Encoding.UTF8.GetString(new ReportExporter().Export(report, ExportFormat.Html).Content); Equal(false, html.Contains("javascript:")); });
        test("CSV exports data rather than page geometry", () => { var doc = SampleReports.Invoice(); var report = new ReportPreparer().Prepare(doc); var file = new ReportExporter().Export(report, ExportFormat.Csv, document: doc); Equal(true, Encoding.UTF8.GetString(file.Content).StartsWith("Description,Quantity,Price", StringComparison.Ordinal)); });
        test("export page range is enforced", () => { var report = new ReportPreparer().Prepare(SampleReports.GroupedSales()); var file = new ReportExporter().Export(report, ExportFormat.Svg, new(FirstPage: 2, LastPage: 2)); Equal("image/svg+xml", file.MediaType); });
        test("empty export range is rejected", () => Throws<ArgumentException>(() => new ReportExporter().Export(new ReportPreparer().Prepare(SampleReports.Invoice()), ExportFormat.Pdf, new(FirstPage: 500))));
        test("raster allocation limit is enforced", () => { using var r = new ReportRenderer(); Throws<InvalidOperationException>(() => ReportExporter.Png(r, new(1, 1000, 1000, []), new(MaximumPixels: 100))); });
    }
}
