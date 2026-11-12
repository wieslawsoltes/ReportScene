using ReportScene.Model;
using ReportScene.Rendering.Skia;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace ReportScene.Validation;

public static class TextRunCacheTests
{
    private static ItemStyle Style => new() { FontFamily = "fixture", FontSize = 17.3, Padding = 2, LineHeight = 1.2 };
    private static SkiaTextMeasurer Metrics(bool color = false) => new((_, _) => ReportGlyphRasterTests.Fixture(outline: !color));

    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action)
        { try { action(); return new("text run cache: " + name, true); } catch (Exception error) { return new("text run cache: " + name, false, error.ToString()); } }

        yield return Test("immutable positions match HarfBuzz and remain isolated from caller-owned arrays", () =>
        {
            using var metrics = Metrics(); var style = Style;
            foreach (string text in new[] { "\uE000\uE000\uE001", "office · a\u0301 · العربية", "" })
            {
                var run = metrics.GetTextRun(text, style);
                var owned = metrics.ShapeText(text, style);
                Conformance.Equal(true, run.Glyphs.ToArray().Select(g => (uint)g).SequenceEqual(owned.Codepoints));
                Conformance.Equal(true, run.Positions.SequenceEqual(owned.Points));
                Conformance.Equal(true, run.Clusters.SequenceEqual(owned.Clusters));
                Conformance.Equal(owned.Width, run.Width);
                Conformance.Equal((double)run.Width, metrics.MeasureWidth(text, style));
                var again = metrics.GetTextRun(text, style with { Foreground = "#FF0000", Alignment = TextAlign.Right, Underline = true });
                Conformance.Equal(true, ReferenceEquals(run, again));
                if (owned.Codepoints.Length > 0)
                {
                    ushort first = run.Glyphs[0]; var point = run.Positions[0]; uint cluster = run.Clusters[0];
                    owned.Codepoints[0] = uint.MaxValue; owned.Points[0] = new(1234, 5678); owned.Clusters[0] = uint.MaxValue;
                    Conformance.Equal(first, again.Glyphs[0]); Conformance.Equal(point, again.Positions[0]); Conformance.Equal(cluster, again.Clusters[0]);
                }
            }
            var original = metrics.GetTextRun("\uE000\uE000", style);
            foreach (var variant in new[] { style with { FontSize = 19.7 }, style with { Bold = true }, style with { Italic = true }, style with { FontFamily = "another" } })
            {
                var actual = metrics.GetTextRun("\uE000\uE000", variant); var expected = metrics.ShapeText("\uE000\uE000", variant);
                Conformance.Equal(false, ReferenceEquals(original, actual));
                Conformance.Equal(expected.Width, actual.Width); Conformance.Equal(true, actual.Positions.SequenceEqual(expected.Points));
            }
        });

        yield return Test("raster runs preserve exact scaled ink bounds and native font identity", () =>
        {
            foreach (bool color in new[] { false, true })
            {
                using var metrics = Metrics(color); var style = Style;
                foreach (float scale in new[] { .125f, 1f, 1.25f, 3.1415927f, 12f })
                {
                    Conformance.Equal(true, metrics.TryGetRasterTextRun("\uE000\uE000\uE001", style, scale, out var run));
                    using var font = metrics.CreateFont(style); font.Size *= scale;
                    var bounds = new SKRect[run!.TextRun.Glyphs.Length];
                    font.GetGlyphWidths(run.TextRun.Glyphs, Span<float>.Empty, bounds);
                    Conformance.Equal(true, run.InkBounds.SequenceEqual(bounds));
                    Conformance.Equal(ReportRasterFont.FromFont(font), run.Font);
                    Conformance.Equal(color, run.Font.IntrinsicColor);
                    Conformance.Equal((float)style.FontSize * scale, run.Font.Size);
                    long shapes = metrics.ShapeOperationCount, queries = metrics.RasterBoundsQueryCount, fonts = metrics.NativeFontCreationCount;
                    Conformance.Equal(true, metrics.TryGetRasterTextRun("\uE000\uE000\uE001", style with { Foreground = "#800000FF" }, scale, out var warm));
                    Conformance.Equal(true, ReferenceEquals(run, warm));
                    Conformance.Equal(shapes, metrics.ShapeOperationCount); Conformance.Equal(queries, metrics.RasterBoundsQueryCount); Conformance.Equal(fonts, metrics.NativeFontCreationCount);
                }
            }
        });

        yield return Test("width-only pagination avoids retaining rendering arrays and reuses existing runs", () =>
        {
            using var metrics = Metrics(); var style = Style;
            for (int i = 0; i < 512; i++) metrics.MeasureWidth("probe" + i, style);
            Conformance.Equal(512, metrics.CachedWidthCount);
            Conformance.Equal(0, metrics.CachedTextRunCount); Conformance.Equal(0, metrics.CachedRasterRunCount);
            Conformance.Equal(0, metrics.CachedNativeBlobCount); Conformance.Equal(1L, metrics.NativeFontCreationCount);
            var run = metrics.GetTextRun("\uE000\uE000\uE001", style);
            long shapes = metrics.ShapeOperationCount;
            Conformance.Equal((double)run.Width, metrics.MeasureWidth("\uE000\uE000\uE001", style));
            Conformance.Equal(shapes, metrics.ShapeOperationCount);
        });

        yield return Test("oversized raster runs exit before font resolution or ink allocation", () =>
        {
            using var metrics = new SkiaTextMeasurer((_, _) => throw new InvalidOperationException("Oversized text must not resolve a font."));
            Conformance.Equal(false, metrics.TryGetRasterTextRun(new string('x', SkiaTextMeasurer.MaximumCachedRunTextLength + 1), Style, 1, out var longRun));
            Conformance.Equal<ReportRasterTextRun?>(null, longRun);
            Conformance.Equal(false, metrics.TryGetRasterTextRun("x", Style with { FontFamily = new('x', SkiaTextMeasurer.MaximumCachedRunTextLength) }, 1, out _));
            Conformance.Equal(0L, metrics.ShapeOperationCount); Conformance.Equal(0L, metrics.RasterBoundsQueryCount); Conformance.Equal(0L, metrics.NativeFontCreationCount);
            foreach (float scale in new[] { 0, -1, float.NaN, float.PositiveInfinity })
                Conformance.Throws<ArgumentOutOfRangeException>(() => metrics.TryGetRasterTextRun("x", Style, scale, out _));
        });

        yield return Test("oversized native color draws avoid a duplicate whole-run glyph array", () =>
        {
            using var metrics = Metrics(color: true);
            using var bitmap = ReportImageRaster.Create(4, 4); using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint { Color = new(0, 255, 0, 128), IsAntialias = true };
            string text = new('\uE000', SkiaTextMeasurer.MaximumCachedRunTextLength * 4);
            var style = Style;
            long ShapeBytes()
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                var shaped = metrics.ShapeText(text, style);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Conformance.Equal(text.Length, shaped.Codepoints.Length);
                return allocated;
            }
            long DrawBytes(ColorGlyphRasterOptions? options)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                metrics.Draw(canvas, text, 1000, 80, style, paint, options);
                return GC.GetAllocatedBytesForCurrentThread() - before;
            }
            foreach (var options in new ColorGlyphRasterOptions?[] { null, new(72, 4096) })
            {
                // Warm interop/JIT metadata without retaining this oversized run.
                ShapeBytes(); DrawBytes(options);
                long shapedBytes = ShapeBytes(), drawnBytes = DrawBytes(options);
                // Fixed paint/builder/matrix wrappers are allowed. A duplicate
                // ushort[16384] adds more than 32 KiB and must fail this bound.
                Conformance.Equal(true, drawnBytes <= shapedBytes + 4096);
                Conformance.Equal(0, metrics.CachedTextRunCount);
                Conformance.Equal(0, metrics.CachedRasterRunCount);
                Conformance.Equal(0, metrics.CachedNativeBlobCount);
            }
            Conformance.Equal(true, bitmap.GetPixelSpan().ToArray().All(value => value == 0));
        });

        yield return Test("native cached blobs preserve exact offset accumulation transforms and paints", () =>
        {
            using var metrics = Metrics(); using var font = metrics.CreateFont(Style); using var shaper = new SKShaper(font.Typeface);
            using var filter = SKColorFilter.CreateBlendMode(SKColors.Blue, SKBlendMode.SrcIn);
            foreach (float origin in new[] { 3.251953f, 1000003.25f })
            foreach (bool effect in new[] { false, true })
            {
                using var actual = ReportImageRaster.Create(260, 160); using var expected = ReportImageRaster.Create(260, 160);
                using var paint = new SKPaint { Color = new(10, 180, 240, 128), IsAntialias = true,
                    Style = effect ? SKPaintStyle.Stroke : SKPaintStyle.Fill, StrokeWidth = 1.25f, ColorFilter = effect ? filter : null };
                using (var canvas = new SKCanvas(actual))
                {
                    canvas.Translate(-MathF.Floor(origin / 1000000) * 1000000, 0);
                    metrics.Draw(canvas, "\uE000\uE000\uE001", origin, 80.25f, Style, paint);
                    long shapes = metrics.ShapeOperationCount, blobs = metrics.NativeBlobBuildCount, fonts = metrics.NativeFontCreationCount;
                    canvas.Clear(SKColors.Transparent);
                    metrics.Draw(canvas, "\uE000\uE000\uE001", origin, 80.25f, Style, paint);
                    Conformance.Equal(shapes, metrics.ShapeOperationCount); Conformance.Equal(blobs, metrics.NativeBlobBuildCount); Conformance.Equal(fonts, metrics.NativeFontCreationCount);
                }
                using (var canvas = new SKCanvas(expected))
                {
                    canvas.Translate(-MathF.Floor(origin / 1000000) * 1000000, 0);
                    canvas.DrawShapedText(shaper, "\uE000\uE000\uE001", origin, 80.25f, SKTextAlign.Left, font, paint);
                }
                Conformance.Equal(true, actual.GetPixelSpan().SequenceEqual(expected.GetPixelSpan()));
                Conformance.Equal(true, actual.GetPixelSpan().ToArray().Any(b => b != 0));
            }
        });

        yield return Test("run raster and native blob retention survives bounded FIFO churn", () =>
        {
            using var metrics = Metrics(); using var surface = SKSurface.Create(new SKImageInfo(2, 2));
            using var paint = new SKPaint { Color = SKColors.Black };
            var style = Style;
            var first = metrics.GetTextRun("entry0", style);
            for (int i = 0; i < SkiaTextMeasurer.MaximumCachedTextRuns + 2; i++)
            {
                string text = "entry" + i;
                metrics.GetTextRun(text, style); metrics.TryGetRasterTextRun(text, style, 1, out _);
                metrics.Draw(surface.Canvas, text, 1000, 1000, style, paint);
            }
            Conformance.Equal(SkiaTextMeasurer.MaximumCachedTextRuns, metrics.CachedTextRunCount);
            Conformance.Equal(SkiaTextMeasurer.MaximumCachedTextRuns, metrics.CachedRasterRunCount);
            Conformance.Equal(SkiaTextMeasurer.MaximumCachedTextRuns, metrics.CachedNativeBlobCount);
            Conformance.Equal(false, ReferenceEquals(first, metrics.GetTextRun("entry0", style)));
            for (int i = 0; i < 300; i++)
            {
                string text = i + new string('\uE001', 500);
                metrics.GetTextRun(text, style); metrics.TryGetRasterTextRun(text, style, 1.25f, out _);
                metrics.Draw(surface.Canvas, text, 1000, 1000, style, paint);
            }
            Conformance.Equal(true, metrics.CachedRunCharacterCount <= SkiaTextMeasurer.MaximumCachedRunCharacters && metrics.CachedRunGlyphCount <= SkiaTextMeasurer.MaximumCachedRunGlyphs);
            Conformance.Equal(true, metrics.CachedRasterCharacterCount <= SkiaTextMeasurer.MaximumCachedRunCharacters && metrics.CachedRasterGlyphCount <= SkiaTextMeasurer.MaximumCachedRunGlyphs);
            Conformance.Equal(true, metrics.CachedBlobCharacterCount <= SkiaTextMeasurer.MaximumCachedRunCharacters && metrics.CachedBlobGlyphCount <= SkiaTextMeasurer.MaximumCachedRunGlyphs);
            Conformance.Equal(true, metrics.CachedTextRunCount > 0 && metrics.CachedRasterRunCount > 0 && metrics.CachedNativeBlobCount > 0);
        });

        yield return Test("face eviction invalidates geometry and uses a new nonrecycled identity", () =>
        {
            bool outline = false; SKTypeface? firstFace = null;
            using var metrics = new SkiaTextMeasurer((_, _) => { var face = ReportGlyphRasterTests.Fixture(outline); firstFace ??= face; return face; });
            var style = Style;
            metrics.TryGetRasterTextRun("\uE000", style, 1, out var old);
            Conformance.Equal(true, old!.Font.IntrinsicColor);
            for (int i = 0; i < 129; i++) using (metrics.CreateFont(style with { FontFamily = "other" + i })) { }
            Conformance.Equal(IntPtr.Zero, firstFace!.Handle);
            Conformance.Equal(0, metrics.CachedRasterRunCount);
            outline = true;
            metrics.TryGetRasterTextRun("\uE000", style, 1, out var current);
            Conformance.Equal(false, current!.Font.IntrinsicColor);
            Conformance.Equal(false, current.Font.TypefaceIdentity == old.Font.TypefaceIdentity);
            Conformance.Equal((ushort)1, old.TextRun.Glyphs[0]);
            Conformance.Equal(true, old.InkBounds[0].Width > 0);
        });

        yield return Test("font-size churn and owner disposal release all native cache entries", () =>
        {
            var metrics = Metrics(); var style = Style;
            metrics.TryGetRasterTextRun("\uE000", style, 1, out var retained);
            using var surface = SKSurface.Create(new SKImageInfo(2, 2)); using var paint = new SKPaint();
            metrics.Draw(surface.Canvas, "\uE000", 1000, 1000, style, paint);
            for (int i = 0; i < SkiaTextMeasurer.MaximumCachedFonts + 2; i++) metrics.GetFontMetrics(style with { FontSize = 10 + i });
            Conformance.Equal(SkiaTextMeasurer.MaximumCachedFonts, metrics.CachedFontCount);
            metrics.Dispose(); metrics.Dispose();
            Conformance.Equal(0, metrics.CachedFontCount); Conformance.Equal(0, metrics.CachedTextRunCount);
            Conformance.Equal(0, metrics.CachedRasterRunCount); Conformance.Equal(0, metrics.CachedNativeBlobCount);
            Conformance.Equal(0, metrics.CachedRunCharacterCount + metrics.CachedRunGlyphCount + metrics.CachedRasterCharacterCount + metrics.CachedRasterGlyphCount + metrics.CachedBlobCharacterCount + metrics.CachedBlobGlyphCount);
            Conformance.Equal((ushort)1, retained!.TextRun.Glyphs[0]);
            Conformance.Throws<ObjectDisposedException>(() => metrics.GetTextRun("", style));
            Conformance.Throws<ObjectDisposedException>(() => metrics.GetFontMetrics(style));
            Conformance.Throws<ObjectDisposedException>(() => metrics.TryGetRasterTextRun("x", style, 1, out _));
        });

        yield return Test("report text layout remains byte equivalent for multiline alignment and underlines", () =>
        {
            using var metrics = Metrics(); using var renderer = new ReportRenderer(metrics);
            foreach (string text in new[] { "\uE000\uE000\uE001", "\uE000\uE001\n\n\uE001\n" })
            foreach (TextAlign alignment in Enum.GetValues<TextAlign>())
            foreach (VerticalAlign vertical in Enum.GetValues<VerticalAlign>())
            foreach (bool underline in new[] { false, true })
            {
                var style = Style with { Alignment = alignment, VerticalAlignment = vertical, Underline = underline, Foreground = "#803060A0" };
                var bounds = new RectD(7.25, 9.5, 200, 115);
                using var actual = ReportImageRaster.Create(240, 140); using var expected = ReportImageRaster.Create(240, 140);
                using (var canvas = new SKCanvas(actual)) renderer.DrawText(canvas, text, bounds, style);
                using (var canvas = new SKCanvas(expected)) OriginalDrawText(canvas, metrics, text, bounds, style);
                Conformance.Equal(true, actual.GetPixelSpan().SequenceEqual(expected.GetPixelSpan()));
            }
        });
    }

    private static void OriginalDrawText(SKCanvas canvas, SkiaTextMeasurer metrics, string text, RectD bounds, ItemStyle style)
    {
        var lines = text.Split('\n'); canvas.Save();
        try
        {
            canvas.ClipRect(ReportRenderer.Rect(bounds));
            using var font = metrics.CreateFont(style); using var paint = new SKPaint { Color = ReportRenderer.Color(style.Foreground), IsAntialias = true };
            double lineHeight = metrics.LineHeight(style), available = bounds.Height - 2 * style.Padding;
            double offset = style.VerticalAlignment switch { VerticalAlign.Center => Math.Max(0, (available - lines.Length * lineHeight) / 2), VerticalAlign.Bottom => Math.Max(0, available - lines.Length * lineHeight), _ => 0 };
            var fontMetrics = font.Metrics; double topPadding = Math.Max(0, (lineHeight - (fontMetrics.Descent - fontMetrics.Ascent)) / 2);
            for (int n = 0; n < lines.Length; n++)
            {
                string line = lines[n]; double width = metrics.MeasureWidth(line, style);
                double x = bounds.X + style.Padding + (style.Alignment switch { TextAlign.Center => (bounds.Width - style.Padding * 2 - width) / 2, TextAlign.Right => bounds.Width - style.Padding * 2 - width, _ => 0 });
                double y = bounds.Y + style.Padding + offset + n * lineHeight + topPadding - fontMetrics.Ascent;
                metrics.Draw(canvas, line, (float)x, (float)y, style, paint);
                if (style.Underline) { paint.StrokeWidth = (float)Math.Max(.5, style.FontSize / 16); canvas.DrawLine((float)x, (float)(y + style.FontSize * .1), (float)(x + width), (float)(y + style.FontSize * .1), paint); }
            }
        }
        finally { canvas.Restore(); }
    }
}
