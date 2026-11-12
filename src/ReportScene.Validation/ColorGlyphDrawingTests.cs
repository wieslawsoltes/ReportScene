using ReportScene.Model;
using ReportScene.Rendering.Skia;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace ReportScene.Validation;

public static class ColorGlyphDrawingTests
{
    internal static readonly ItemStyle Style = new() { FontFamily = "fixture-color", FontSize = 100, Padding = 0, LineHeight = 1 };
    internal static SkiaTextMeasurer Metrics(bool outline = false) => new((_, _) => ReportGlyphRasterTests.Fixture(outline));

    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action)
        { try { action(); return new("color glyph drawing: " + name, true); } catch (Exception error) { return new("color glyph drawing: " + name, false, error.ToString()); } }

        yield return Test("shared Skia adapter applies palette and foreground opacity once", () =>
        {
            using var metrics = Metrics();
            foreach (byte alpha in new byte[] { 255, 128, 64 })
            {
                using var bitmap = ReportImageRaster.Create(120, 100);
                using var canvas = new SKCanvas(bitmap);
                using var paint = new SKPaint { Color = new(0, 255, 0, alpha), IsAntialias = true };
                new SkiaReportCanvas(canvas).DrawText(metrics, "\uE000", 10, 90, Style, paint);
                canvas.Flush();
                ReportGlyphRasterTests.AssertPixel(bitmap, 35, 50, new(255, 0, 0, alpha));
                ReportGlyphRasterTests.AssertPixel(bitmap, 85, 50, paint.Color);
                Conformance.Equal((byte)0, bitmap.GetPixel(0, 0).Alpha);
                Conformance.Equal(new SKColor(0, 255, 0, alpha), paint.Color);
            }
        });

        yield return Test("HarfBuzz positioning and overlap retain per-glyph SrcOver opacity", () =>
        {
            using var metrics = Metrics();
            var shaped = metrics.ShapeText("\uE000\uE000", Style);
            Conformance.Equal(2, shaped.Codepoints.Length);
            Conformance.Equal(0f, shaped.Points[0].X);
            // HarfBuzz quantizes design-unit kerning at its font scale. Keep the
            // semantic 40pt overlap check independent of that subpixel rounding.
            Conformance.Equal(true, Math.Abs(shaped.Points[1].X - 60) < .1f);
            Conformance.Equal(true, Math.Abs(shaped.Width - 160) < .1f);
            foreach (byte alpha in new byte[] { 128, 64 })
            {
                using var bitmap = ReportImageRaster.Create(180, 100);
                using var canvas = new SKCanvas(bitmap);
                using var paint = new SKPaint { Color = new(0, 255, 0, alpha), IsAntialias = true };
                metrics.Draw(canvas, "\uE000\uE000", 10, 90, Style, paint);
                canvas.Flush();
                // The second glyph's red layer overlaps the first foreground
                // layer. A single opacity group around the run fails this check.
                int retained = (alpha * (255 - alpha) + 127) / 255;
                AssertRaw(bitmap, 85, 50, alpha, retained, 0, alpha + retained);
                ReportGlyphRasterTests.AssertPixel(bitmap, 35, 50, new(255, 0, 0, alpha));
                ReportGlyphRasterTests.AssertPixel(bitmap, 145, 50, paint.Color);
            }
        });

        yield return Test("transformed clipped glyphs restore the caller canvas state", () =>
        {
            using var metrics = Metrics();
            using var bitmap = ReportImageRaster.Create(220, 160);
            using var canvas = new SKCanvas(bitmap);
            canvas.Translate(10, 15); canvas.Scale(2, 1.5f); canvas.ClipRect(new(20, 10, 80, 70));
            int saves = canvas.SaveCount; var matrix = canvas.TotalMatrix44; var clip = canvas.LocalClipBounds;
            using var paint = new SKPaint { Color = new(0, 255, 0, 128), IsAntialias = true };
            metrics.Draw(canvas, "\uE000", 0, 80, Style, paint);
            // Disjoint glyphs at nonzero and extreme coordinates must not enter
            // an opacity layer or overflow integer device-bound conversion.
            metrics.Draw(canvas, "\uE000", 1000, 80, Style, paint);
            metrics.Draw(canvas, "\uE000", 1e20f, 80, Style, paint);
            canvas.Flush();
            Conformance.Equal(saves, canvas.SaveCount); Conformance.Equal(matrix, canvas.TotalMatrix44); Conformance.Equal(clip, canvas.LocalClipBounds);
            ReportGlyphRasterTests.AssertPixel(bitmap, 70, 75, new(255, 0, 0, 128));
            ReportGlyphRasterTests.AssertPixel(bitmap, 130, 75, paint.Color);
            Conformance.Equal((byte)0, bitmap.GetPixel(40, 75).Alpha);
            Conformance.Equal((byte)0, bitmap.GetPixel(130, 125).Alpha);
        });

        yield return Test("bounded opacity tiles have no seam or double blend", () =>
        {
            using var metrics = Metrics();
            using var bitmap = ReportImageRaster.Create(2210, 24);
            using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint { Color = new(0, 255, 0, 128), IsAntialias = true };
            // Only a 24px-high strip is visible from this 2200x1760 glyph. Its
            // device bounds span more than one 2048px opacity tile.
            metrics.Draw(canvas, "\uE000", 4, 1760, Style with { FontSize = 2200 }, paint);
            canvas.Flush();
            for (int x = 2046; x <= 2054; x++) ReportGlyphRasterTests.AssertPixel(bitmap, x, 12, paint.Color);
            ReportGlyphRasterTests.AssertPixel(bitmap, 100, 12, new(255, 0, 0, 128));
            Conformance.Equal(1, canvas.SaveCount);
        });

        yield return Test("ordinary fonts remain byte-equivalent to native shaped drawing", () =>
        {
            using var metrics = Metrics(outline: true);
            using var face = ReportGlyphRasterTests.Fixture(outline: true);
            Conformance.Equal(false, ReportGlyphRaster.HasIntrinsicColor(face));
            using var shaper = new SKShaper(face);
            using var font = new SKFont(face, 100) { Edging = SKFontEdging.Antialias, Subpixel = true };
            foreach (byte alpha in new byte[] { 255, 128, 64 })
            {
                using var actual = ReportImageRaster.Create(350, 140); using var expected = ReportImageRaster.Create(350, 140);
                using var paint = new SKPaint { Color = new(10, 180, 240, alpha), IsAntialias = true };
                using (var canvas = new SKCanvas(actual)) { canvas.Scale(1.25f); metrics.Draw(canvas, "\uE000\uE000\uE001", 3.25f, 90.5f, Style, paint); }
                using (var canvas = new SKCanvas(expected)) { canvas.Scale(1.25f); canvas.DrawShapedText(shaper, "\uE000\uE000\uE001", 3.25f, 90.5f, SKTextAlign.Left, font, paint); }
                Conformance.Equal(true, actual.GetPixelSpan().SequenceEqual(expected.GetPixelSpan()));
            }
        });

        yield return Test("custom native paints keep their effects and remain unmodified", () =>
        {
            using var metrics = Metrics(); using var face = ReportGlyphRasterTests.Fixture(); using var shaper = new SKShaper(face);
            using var font = new SKFont(face, 100) { Edging = SKFontEdging.Antialias, Subpixel = true };
            using var filter = SKColorFilter.CreateBlendMode(SKColors.Blue, SKBlendMode.SrcIn);
            foreach (bool filtered in new[] { false, true })
            {
                using var actual = ReportImageRaster.Create(180, 100); using var expected = ReportImageRaster.Create(180, 100);
                using var paint = new SKPaint { Color = new(0, 255, 0, 128), IsAntialias = true,
                    Style = filtered ? SKPaintStyle.Fill : SKPaintStyle.Stroke, StrokeWidth = 3, ColorFilter = filtered ? filter : null };
                var original = paint.Color; var originalStyle = paint.Style; var originalFilter = paint.ColorFilter?.Handle;
                using (var canvas = new SKCanvas(actual)) metrics.Draw(canvas, "\uE000\uE000", 10, 90, Style, paint);
                using (var canvas = new SKCanvas(expected)) canvas.DrawShapedText(shaper, "\uE000\uE000", 10, 90, SKTextAlign.Left, font, paint);
                Conformance.Equal(true, actual.GetPixelSpan().SequenceEqual(expected.GetPixelSpan()));
                Conformance.Equal(original, paint.Color); Conformance.Equal(originalStyle, paint.Style);
                Conformance.Equal(3f, paint.StrokeWidth); Conformance.Equal(originalFilter, paint.ColorFilter?.Handle);
            }
        });

        yield return Test("font resolvers transfer cache ownership and reject disposed faces", () =>
        {
            Conformance.Throws<ArgumentNullException>(() => new SkiaTextMeasurer(null!));
            int calls = 0; SKTypeface? transferred = null;
            using (var metrics = new SkiaTextMeasurer((family, style) =>
            {
                calls++; Conformance.Equal(Style.FontFamily, family); Conformance.Equal(SKFontStyleWeight.Normal, (SKFontStyleWeight)style.Weight);
                return transferred = ReportGlyphRasterTests.Fixture();
            }))
            {
                using (metrics.CreateFont(Style)) { }
                using (metrics.CreateFont(Style)) { }
                Conformance.Equal(1, calls); Conformance.Equal(true, transferred!.Handle != IntPtr.Zero);
            }
            Conformance.Equal(IntPtr.Zero, transferred!.Handle);
            using var disposedFace = ReportGlyphRasterTests.Fixture(); disposedFace.Dispose();
            using var invalid = new SkiaTextMeasurer((_, _) => disposedFace);
            Conformance.Throws<ObjectDisposedException>(() => invalid.CreateFont(Style));
        });
    }

    private static void AssertRaw(SKBitmap bitmap, int x, int y, int red, int green, int blue, int alpha)
    {
        var pixel = bitmap.GetPixelSpan().Slice(y * bitmap.RowBytes + x * 4, 4);
        Conformance.Equal((byte)red, pixel[0]); Conformance.Equal((byte)green, pixel[1]);
        Conformance.Equal((byte)blue, pixel[2]); Conformance.Equal((byte)alpha, pixel[3]);
    }
}
