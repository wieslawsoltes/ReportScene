using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ReportScene.Data;
using ReportScene.Export;
using ReportScene.Model;
using ReportScene.Rendering.Skia;
using SkiaSharp;

namespace ReportScene.Validation;

public static class ColorGlyphExportTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action)
        { try { action(); return new("color glyph export: " + name, true); } catch (Exception error) { return new("color glyph export: " + name, false, error.ToString()); } }

        yield return Test("SVG embeds opaque and translucent intrinsic colors while ordinary text stays vector", () =>
        {
            foreach (byte alpha in new byte[] { 255, 128, 64 })
            {
                using var metrics = Metrics(); using var renderer = new ReportRenderer(metrics);
                string svg = ReportExporter.Svg(renderer, Page(alpha));
                var xml = XDocument.Parse(svg);
                Conformance.Equal(1, Images(xml).Count);
                Conformance.Equal(true, xml.Descendants().Any(e => e.Name.LocalName == "text" && e.Value.Contains('\uE001')));
                using var bitmap = Decode(Images(xml).Single());
                Conformance.Equal(204, bitmap.Width); Conformance.Equal(164, bitmap.Height);
                ReportGlyphRasterTests.AssertPixel(bitmap, 52, 82, new(255, 0, 0, alpha));
                ReportGlyphRasterTests.AssertPixel(bitmap, 152, 82, new(0, 255, 0, alpha));
                Conformance.Equal((byte)0, bitmap.GetPixel(0, 0).Alpha);
            }
        });

        yield return Test("SVG and HTML forward DPI and glyph pixel budgets through real export entry points", () =>
        {
            var exporter = new ReportExporter(Metrics);
            var report = Report(128);
            foreach (var format in new[] { ExportFormat.Svg, ExportFormat.Html })
            {
                foreach (int dpi in new[] { 72, 144 })
                {
                    var output = exporter.Export(report, format, new(Dpi: dpi));
                    var xml = SvgXml(Encoding.UTF8.GetString(output.Content));
                    using var bitmap = Decode(Images(xml).Single());
                    Conformance.Equal(dpi == 72 ? 104 : 204, bitmap.Width);
                    Conformance.Equal(dpi == 72 ? 84 : 164, bitmap.Height);
                    ReportGlyphRasterTests.AssertPixel(bitmap, dpi == 72 ? 77 : 152, dpi == 72 ? 42 : 82, new(0, 255, 0, 128));
                }
                Conformance.Throws<ReportRenderException>(() => exporter.Export(report, format, new(Dpi: 144, MaximumPixels: 100)));
            }
        });

        yield return Test("color glyph rasterization accounts for transforms and clips before allocation", () =>
        {
            using var metrics = ColorGlyphDrawingTests.Metrics();
            using var paint = new SKPaint { Color = new(0, 255, 0, 128), IsAntialias = true };
            string Draw(bool clipped, bool rotated)
            {
                using var stream = new MemoryStream();
                using (var canvas = SKSvgCanvas.Create(new SKRect(0, 0, 400, 300), stream))
                {
                    if (rotated) { canvas.Translate(300, 20); canvas.RotateDegrees(90); }
                    else canvas.Translate(20, 20);
                    canvas.Scale(2, 3);
                    if (clipped) canvas.ClipRect(new(20, 10, 21, 11));
                    int saves = canvas.SaveCount; var matrix = canvas.TotalMatrix44; var clip = canvas.LocalClipBounds;
                    var options = new ColorGlyphRasterOptions(72, clipped ? 200 : 100_000);
                    metrics.Draw(canvas, "\uE000", 0, 80, ColorGlyphDrawingTests.Style, paint, options);
                    // An invisible glyph must not consume even a one-pixel budget.
                    metrics.Draw(canvas, "\uE000", 1000, 80, ColorGlyphDrawingTests.Style, paint, new(72, 1));
                    Conformance.Equal(saves, canvas.SaveCount); Conformance.Equal(matrix, canvas.TotalMatrix44); Conformance.Equal(clip, canvas.LocalClipBounds);
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
            foreach (bool rotated in new[] { false, true })
            {
                using (var full = Decode(Images(XDocument.Parse(Draw(false, rotated))).Single()))
                using (var expectedFont = metrics.CreateFont(ColorGlyphDrawingTests.Style with { FontSize = 300 }))
                {
                    // Both transforms have maximum singular value three. The
                    // rotated matrix has zero diagonal: max(ScaleX, ScaleY)
                    // would drop or undersample the glyph instead.
                    var expected = ReportGlyphRasterTests.Bounds(expectedFont, 1);
                    Conformance.Equal((int)expected.Width, full.Width); Conformance.Equal((int)expected.Height, full.Height);
                }
                using (var clipped = Decode(Images(XDocument.Parse(Draw(true, rotated))).Single()))
                {
                    Conformance.Equal(true, clipped.Width <= 12 && clipped.Height <= 12);
                    Conformance.Equal(true, clipped.Width * clipped.Height <= 200);
                    ReportGlyphRasterTests.AssertPixel(clipped, clipped.Width / 2, clipped.Height / 2, new(255, 0, 0, 128));
                }
            }
            using var oversizedStream = new MemoryStream();
            using var oversizedCanvas = SKSvgCanvas.Create(new SKRect(0, 0, 25_000, 25_000), oversizedStream);
            int before = oversizedCanvas.SaveCount;
            Conformance.Throws<InvalidOperationException>(() => metrics.Draw(oversizedCanvas, "\uE000", 0, 16_000,
                ColorGlyphDrawingTests.Style with { FontSize = 20_000 }, paint, new(72, int.MaxValue)));
            Conformance.Equal(before, oversizedCanvas.SaveCount);
        });

        yield return Test("PDF retains native vector glyph forms and requested byte opacity", () =>
        {
            var exporter = new ReportExporter(Metrics);
            foreach (byte alpha in new byte[] { 255, 128, 64 })
            {
                string pdf = Encoding.Latin1.GetString(exporter.Export(Report(alpha), ExportFormat.Pdf).Content);
                Conformance.Equal(true, pdf.StartsWith("%PDF", StringComparison.Ordinal));
                Conformance.Equal(0, Regex.Matches(pdf, @"/Subtype\s*/Image\b").Count);
                Conformance.Equal(true, Regex.Matches(pdf, @"/Subtype\s*/Form\b").Count > 0);
                var opacity = Regex.Matches(pdf, @"/ca\s+([\d.]+)").Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
                // Skia serializes PDF alpha with three decimals. Verify the
                // resulting byte opacity rather than requiring extra precision.
                Conformance.Equal(true, opacity.Any(value => Math.Round(value * 255) == alpha));
            }
        });

        yield return Test("PNG export agrees with palette and foreground compositing on paper", () =>
        {
            var exporter = new ReportExporter(Metrics);
            foreach (int dpi in new[] { 72, 144 })
            {
                var output = exporter.Export(Report(128), ExportFormat.Png, new(Dpi: dpi));
                using var image = SKImage.FromEncodedData(output.Content);
                using var bitmap = ReportImageRaster.Decode(image);
                using var reference = PaperReference(dpi);
                int scale = dpi / 72;
                // Only the white destination contribution (255 - 128 = 127)
                // admits one RGBA8 step through native blending/PNG conversion.
                // Saturated source channels and opaque output alpha stay exact;
                // both images must obey these bounds and differ by at most one.
                ColorGlyphDrawingTests.AssertCompositedPixel(bitmap, reference, 35 * scale, 50 * scale,
                    [255, 126, 126, 255], [255, 128, 128, 255]);
                ColorGlyphDrawingTests.AssertCompositedPixel(bitmap, reference, 85 * scale, 50 * scale,
                    [126, 255, 126, 255], [128, 255, 128, 255]);
            }
        });

        yield return Test("export font factories own each drawing operation and bypass data-only formats", () =>
        {
            Conformance.Throws<ArgumentNullException>(() => new ReportExporter(null!));
            var owners = new List<SkiaTextMeasurer>();
            var exporter = new ReportExporter(() => { var owner = Metrics(); owners.Add(owner); return owner; });
            var report = Report(128);
            var document = new ReportDocument { Data = [XmlDataReader.Read("<data><row><Value>One</Value></row></data>", "Rows")] };
            foreach (var format in new[] { ExportFormat.Text, ExportFormat.PreparedJson, ExportFormat.Csv })
                exporter.Export(report, format, document: document);
            Conformance.Equal(0, owners.Count);
            exporter.Export(report, ExportFormat.Svg);
            Conformance.Equal(1, owners.Count);
            Conformance.Throws<ObjectDisposedException>(() => owners[0].CreateFont(ColorGlyphDrawingTests.Style));
            Conformance.Throws<ReportRenderException>(() => exporter.Export(report, ExportFormat.Html, new(MaximumPixels: 1)));
            Conformance.Equal(2, owners.Count);
            Conformance.Throws<ObjectDisposedException>(() => owners[1].CreateFont(ColorGlyphDrawingTests.Style));
            Conformance.Throws<InvalidOperationException>(() => new ReportExporter(() => null!).Export(report, ExportFormat.Svg));
        });
    }

    private static SKBitmap PaperReference(int dpi)
    {
        int scale = dpi / 72;
        // Match the export surface format and encoding route, but use ordinary
        // source-over rectangles with no font, glyph helper or opacity group.
        using var surface = SKSurface.Create(new SKImageInfo(220 * scale, 120 * scale, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("The native PNG reference surface could not be created.");
        surface.Canvas.Clear(SKColors.White); surface.Canvas.Scale(scale);
        using var paint = new SKPaint { Color = new(255, 0, 0, 128), BlendMode = SKBlendMode.SrcOver };
        surface.Canvas.DrawRect(new SKRect(10, 10, 60, 90), paint);
        paint.Color = new(0, 255, 0, 128);
        surface.Canvas.DrawRect(new SKRect(60, 10, 110, 90), paint);
        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        using var decoded = SKImage.FromEncodedData(encoded.ToArray());
        return ReportImageRaster.Decode(decoded);
    }

    private static SkiaTextMeasurer Metrics() => new((family, _) => ReportGlyphRasterTests.Fixture(family == "fixture-outline"));
    private static PreparedReport Report(byte alpha) => new("Color glyph export", [Page(alpha)], [], TimeSpan.Zero);
    private static PreparedPage Page(byte alpha)
    {
        var color = new TextItem { Id = "color", Bounds = new(10, 10, 110, 100),
            Style = ColorGlyphDrawingTests.Style with { Foreground = $"#{alpha:X2}00FF00" } };
        var outline = new TextItem { Id = "outline", Bounds = new(140, 10, 60, 100),
            Style = ColorGlyphDrawingTests.Style with { FontFamily = "fixture-outline" } };
        return new(1, 220, 120, [new(color, color.Bounds, "\uE000"), new(outline, outline.Bounds, "\uE001")]);
    }

    private static XDocument SvgXml(string output)
    {
        int start = output.IndexOf("<svg", StringComparison.Ordinal), end = output.IndexOf("</svg>", StringComparison.Ordinal);
        if (start < 0 || end < start) throw new InvalidOperationException("The export did not contain an SVG document.");
        return XDocument.Parse(output.Substring(start, end + 6 - start));
    }
    private static List<XElement> Images(XDocument xml) => xml.Descendants().Where(e => e.Name.LocalName == "image").ToList();
    private static SKBitmap Decode(XElement element)
    {
        string source = element.Attributes().Single(a => a.Name.LocalName == "href").Value;
        const string prefix = "data:image/png;base64,";
        Conformance.Equal(true, source.StartsWith(prefix, StringComparison.Ordinal));
        using var image = SKImage.FromEncodedData(Convert.FromBase64String(source[prefix.Length..]))
            ?? throw new InvalidOperationException("The exported glyph PNG is invalid.");
        return ReportImageRaster.Decode(image);
    }
}
