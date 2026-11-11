using ReportScene.Rendering.Skia;
using SkiaSharp;

namespace ReportScene.Validation;

public static class ReportGlyphRasterTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action)
        { try { action(); return new("glyph raster: " + name, true); } catch (Exception error) { return new("glyph raster: " + name, false, error.ToString()); } }
        yield return Test("COLR palette and foreground layers retain premultiplied colors and opacity", () =>
        {
            using var face = Fixture();
            Conformance.Equal(true, ReportGlyphRaster.HasIntrinsicColor(face));
            using var font = new SKFont(face, 100) { Edging = SKFontEdging.Antialias, Subpixel = true };
            ushort glyph = font.GetGlyphs("\uE000").Single();
            Conformance.Equal((ushort)1, glyph);
            var bounds = Bounds(font, glyph);
            foreach (var foreground in new[] { new SKColor(0, 255, 0, 128), new SKColor(0, 0, 255, 64) })
            {
                using var bitmap = ReportGlyphRaster.Rasterize(font, glyph, bounds, foreground, intrinsicColor: true);
                int left = (int)(25 - bounds.Left), right = (int)(75 - bounds.Left), y = (int)(-40 - bounds.Top);
                AssertPixel(bitmap, left, y, new SKColor(255, 0, 0, foreground.Alpha));
                AssertPixel(bitmap, right, y, foreground);
                Conformance.Equal((byte)0, bitmap.GetPixel(0, 0).Alpha);
            }
        });
        yield return Test("ordinary outline coverage remains independent of the requested GPU tint", () =>
        {
            using var face = Fixture();
            using var font = new SKFont(face, 100) { Edging = SKFontEdging.Antialias, Subpixel = true };
            // Glyph 2 is the fixture's ordinary rectangle outline, without COLR
            // layers of its own. Coverage-only rendering must stay opaque white.
            const ushort glyph = 2;
            var bounds = Bounds(font, glyph);
            using var bitmap = ReportGlyphRaster.Rasterize(font, glyph, bounds, new SKColor(0, 255, 0, 64), intrinsicColor: false);
            AssertPixel(bitmap, (int)(25 - bounds.Left), (int)(-40 - bounds.Top), SKColors.White);
            Conformance.Equal((byte)0, bitmap.GetPixel(0, 0).Alpha);
        });
    }

    private static SKTypeface Fixture()
    {
        using var stream = typeof(ReportGlyphRasterTests).Assembly.GetManifestResourceStream("ReportScene.Validation.ColorGlyphFixture.ttf")
            ?? throw new InvalidOperationException("The deterministic color-glyph font fixture is missing.");
        using var data = SKData.Create(stream);
        return SKTypeface.FromData(data) ?? throw new InvalidOperationException("The color-glyph font fixture is invalid.");
    }

    private static SKRect Bounds(SKFont font, ushort glyph)
    {
        Span<ushort> glyphs = stackalloc ushort[1] { glyph };
        Span<SKRect> bounds = stackalloc SKRect[1];
        font.GetGlyphWidths(glyphs, Span<float>.Empty, bounds);
        int guard = ReportGlyphRaster.CoverageGuard;
        return new(MathF.Floor(bounds[0].Left) - guard, MathF.Floor(bounds[0].Top) - guard,
            MathF.Ceiling(bounds[0].Right) + guard, MathF.Ceiling(bounds[0].Bottom) + guard);
    }

    private static void AssertPixel(SKBitmap bitmap, int x, int y, SKColor expected)
    {
        // The GPU samples raw premultiplied bytes, so inspect that representation
        // rather than relying only on SKBitmap.GetPixel's unpremultiplied value.
        var pixels = bitmap.GetPixelSpan();
        int offset = y * bitmap.RowBytes + x * 4;
        Conformance.Equal((byte)((expected.Red * expected.Alpha + 127) / 255), pixels[offset]);
        Conformance.Equal((byte)((expected.Green * expected.Alpha + 127) / 255), pixels[offset + 1]);
        Conformance.Equal((byte)((expected.Blue * expected.Alpha + 127) / 255), pixels[offset + 2]);
        Conformance.Equal(expected.Alpha, pixels[offset + 3]);
    }
}
