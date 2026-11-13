using System.Diagnostics;
using ReportScene.Model;
using ReportScene.Rendering.Skia;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

internal static class TextRenderingBenchmarks
{
    public static IEnumerable<object> Run()
    {
        var style = new ItemStyle { FontSize = 10, Padding = 0 };
        string[] texts = Enumerable.Range(0, 128).Select(n => $"Invoice AV office café a\u0301 item {n:D3}").ToArray();
        using var metrics = new SkiaTextMeasurer();
        using var font = metrics.CreateFont(style); using var shaper = new SKShaper(font.Typeface);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var actual = ReportImageRaster.Create(1000, 520); using var expected = ReportImageRaster.Create(1000, 520);
        using var actualCanvas = new SKCanvas(actual); using var expectedCanvas = new SKCanvas(expected);
        int Draw(bool cached, int frames)
        {
            var canvas = cached ? actualCanvas : expectedCanvas;
            for (int frame = 0; frame < frames; frame++)
            {
                canvas.Clear(SKColors.White);
                for (int i = 0; i < texts.Length; i++)
                {
                    float x = 4 + i % 4 * 245, y = 14 + i / 4 * 16;
                    if (cached) metrics.Draw(canvas, texts[i], x, y, style, paint);
                    else canvas.DrawShapedText(shaper, texts[i], x, y, SKTextAlign.Left, font, paint);
                }
            }
            canvas.Flush(); return frames * texts.Length;
        }
        Draw(false, 1); Draw(true, 1);
        if (!actual.GetPixelSpan().SequenceEqual(expected.GetPixelSpan())) throw new InvalidOperationException("Cached text drawing differs from native DrawShapedText.");
        if (!actual.GetPixelSpan().ToArray().Any(b => b != 255)) throw new InvalidOperationException("Text benchmark rendered an empty white surface.");
        const int frames = 64;
        var referenceDraw = Measure(() => Draw(false, frames));
        long shapeStart = metrics.ShapeOperationCount, blobStart = metrics.NativeBlobBuildCount, fontStart = metrics.NativeFontCreationCount;
        var cachedDraw = Measure(() => Draw(true, frames));
        long shapes = metrics.ShapeOperationCount - shapeStart, blobs = metrics.NativeBlobBuildCount - blobStart, fonts = metrics.NativeFontCreationCount - fontStart;
        if (shapes != 0 || blobs != 0 || fonts != 0) throw new InvalidOperationException("Warm native text drawing rebuilt a shape, text blob, or font.");
        if (!actual.GetPixelSpan().SequenceEqual(expected.GetPixelSpan())) throw new InvalidOperationException("Measured text draws changed pixels.");
        yield return new
        {
            name = "text-native-repeated-draw", uniqueRuns = texts.Length, framesPerMeasurement = frames, drawsPerMeasurement = frames * texts.Length,
            referenceDraw, cachedDraw, cachedShapeOperations = shapes, cachedBlobBuilds = blobs, cachedFontCreations = fonts,
            metrics.CachedNativeBlobCount, metrics.CachedBlobCharacterCount, metrics.CachedBlobGlyphCount,
            note = "Exact native raster pixels checked. Reference reuses the same font and HarfBuzz shaper but rebuilds shaped arrays/text blobs as DrawShapedText does; optimized draws reuse bounded offset-exact native blobs. CPU raster work only, not GPU/FPS evidence."
        };

        const float rasterScale = 1.25f;
        const int rasterIterations = 4096;
        var runs = texts.Select(text => metrics.GetTextRun(text, style)).ToArray();
        int shapedGlyphsPerMeasurement = 32 * runs.Sum(run => run.Glyphs.Length);
        int referenceInkCount = 0, cachedInkCount = 0;
        int ReferenceRaster()
        {
            int checksum = 0; referenceInkCount = 0;
            Span<SKRect> bounds = stackalloc SKRect[128];
            for (int iteration = 0; iteration < rasterIterations; iteration++)
            {
                var run = runs[iteration % runs.Length];
                using var rasterFont = metrics.CreateFont(style); rasterFont.Size *= rasterScale;
                for (int start = 0; start < run.Glyphs.Length; start += 128)
                {
                    int count = Math.Min(128, run.Glyphs.Length - start);
                    rasterFont.GetGlyphWidths(run.Glyphs.Slice(start, count), Span<float>.Empty, bounds[..count]);
                    for (int i = 0; i < count; i++)
                    {
                        // Production rejects spaces and other zero-ink glyphs
                        // before reading the native font's atlas-key fields.
                        if (!ReportViewportClip.HasPositiveArea(bounds[i])) continue;
                        referenceInkCount++;
                        // The old atlas-key path read these native font properties
                        // per visible glyph, even when the atlas already held it.
                        var descriptor = ReportRasterFont.FromFont(rasterFont);
                        checksum = unchecked(checksum + Fingerprint(run.Glyphs[start + i], bounds[i], descriptor));
                    }
                }
            }
            return checksum;
        }
        int CachedRaster()
        {
            int checksum = 0; cachedInkCount = 0;
            for (int iteration = 0; iteration < rasterIterations; iteration++)
            {
                if (!metrics.TryGetRasterTextRun(texts[iteration % texts.Length], style, rasterScale, out var run)) throw new InvalidOperationException("The bounded fixture must fit the raster-run cache.");
                for (int i = 0; i < run.TextRun.Glyphs.Length; i++)
                {
                    if (!ReportViewportClip.HasPositiveArea(run.InkBounds[i])) continue;
                    cachedInkCount++;
                    checksum = unchecked(checksum + Fingerprint(run.TextRun.Glyphs[i], run.InkBounds[i], run.Font));
                }
            }
            return checksum;
        }
        int expectedRasterChecksum = ReferenceRaster();
        int positiveInkGlyphsPerMeasurement = referenceInkCount;
        if (expectedRasterChecksum != CachedRaster()) throw new InvalidOperationException("Cached raster geometry/font descriptors differ from native queries.");
        if (positiveInkGlyphsPerMeasurement == 0 || positiveInkGlyphsPerMeasurement >= shapedGlyphsPerMeasurement)
            throw new InvalidOperationException("The raster fixture must exercise both visible ink and zero-ink glyphs.");
        if (cachedInkCount != positiveInkGlyphsPerMeasurement)
            throw new InvalidOperationException("Reference and cached traversal must reject exactly the same zero-ink glyphs.");
        var referenceRaster = Measure(ReferenceRaster);
        long rasterStart = metrics.RasterBoundsQueryCount; fontStart = metrics.NativeFontCreationCount; shapeStart = metrics.ShapeOperationCount;
        var cachedRaster = Measure(CachedRaster);
        long rasterQueries = metrics.RasterBoundsQueryCount - rasterStart;
        fonts = metrics.NativeFontCreationCount - fontStart; shapes = metrics.ShapeOperationCount - shapeStart;
        if (rasterQueries != 0 || fonts != 0 || shapes != 0) throw new InvalidOperationException("Warm raster geometry performs native font/shaping work.");
        if (referenceInkCount != positiveInkGlyphsPerMeasurement || cachedInkCount != positiveInkGlyphsPerMeasurement)
            throw new InvalidOperationException("Measured raster traversal changed the positive-ink glyph count.");
        if (referenceRaster.Checksum != expectedRasterChecksum || cachedRaster.Checksum != expectedRasterChecksum)
            throw new InvalidOperationException("Measured raster geometry/font descriptors changed their checksum.");
        yield return new
        {
            name = "text-raster-run-preparation", uniqueRuns = texts.Length, runsPerMeasurement = rasterIterations,
            shapedGlyphsPerMeasurement, positiveInkGlyphsPerMeasurement,
            zeroInkGlyphsPerMeasurement = shapedGlyphsPerMeasurement - positiveInkGlyphsPerMeasurement, rasterScale,
            referenceRaster, cachedRaster, cachedBoundsQueries = rasterQueries, cachedFontCreations = fonts, cachedShapeOperations = shapes,
            metrics.CachedRasterRunCount, metrics.CachedRasterCharacterCount, metrics.CachedRasterGlyphCount,
            note = "Differential CPU pre-atlas work: original bounded glyph-bound queries and per-positive-ink-glyph native font-key reads versus immutable cached geometry/descriptor lookup. Both paths reject zero-ink bounds with ReportViewportClip.HasPositiveArea before descriptor/fingerprint work, matching production. Exact positive-ink counts and descriptor/bounds checksums match. No additional viewport clipping is simulated. Atlas lookup, rasterization, GPU upload, submission, presentation and UI are excluded."
        };
    }

    private static int Fingerprint(ushort glyph, SKRect bounds, ReportRasterFont font) => unchecked(
        glyph + BitConverter.SingleToInt32Bits(bounds.Left) + BitConverter.SingleToInt32Bits(bounds.Top)
        + BitConverter.SingleToInt32Bits(bounds.Right) + BitConverter.SingleToInt32Bits(bounds.Bottom)
        + BitConverter.SingleToInt32Bits(font.Size) + BitConverter.SingleToInt32Bits(font.ScaleX) + BitConverter.SingleToInt32Bits(font.SkewX)
        + font.Flags + (int)font.TypefaceIdentity + (font.IntrinsicColor ? 1 : 0));

    private static Measurement Measure(Func<int> action)
    {
        _ = action(); var runs = new List<Measurement>();
        for (int i = 0; i < 3; i++)
        {
            long bytes = GC.GetAllocatedBytesForCurrentThread(), started = Stopwatch.GetTimestamp(); int checksum = action();
            runs.Add(new(Stopwatch.GetElapsedTime(started).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - bytes, checksum));
        }
        return runs.OrderBy(run => run.Milliseconds).ElementAt(1);
    }
}
