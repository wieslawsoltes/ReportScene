using SkiaSharp;

namespace ReportScene.Rendering.Skia;

/// <summary>Shared CPU glyph rasterization for bounded GPU atlases and headless validation.</summary>
public static class ReportGlyphRaster
{
    public const int CoverageGuard = 2;

    /// <summary>
    /// Identifies color-capable OpenType faces. Cache this result with the face:
    /// even monochrome glyphs in such a face must retain the requested foreground
    /// because COLR foreground-index layers and ordinary outlines can coexist.
    /// </summary>
    public static bool HasIntrinsicColor(SKTypeface typeface)
    {
        ArgumentNullException.ThrowIfNull(typeface);
        return typeface.GetTableSize(0x434f4c52) > 0 // COLR
            || typeface.GetTableSize(0x43424454) > 0 // CBDT
            || typeface.GetTableSize(0x73626978) > 0 // sbix
            || typeface.GetTableSize(0x53564720) > 0; // SVG
    }

    /// <summary>
    /// Renders a positioned glyph into premultiplied sRGB RGBA8. Color-capable
    /// faces retain intrinsic palette/bitmap colors and foreground-index layers;
    /// ordinary coverage masks use white and can be reused with any GPU tint.
    /// Color glyph opacity is applied once after rasterization, avoiding native
    /// COLR foreground-index paths that otherwise apply paint opacity twice.
    /// The caller owns the result and supplies pixel-aligned baseline-relative bounds.
    /// </summary>
    public static SKBitmap Rasterize(SKFont font, ushort glyph, SKRect bounds, SKColor foreground, bool intrinsicColor)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (!float.IsFinite(bounds.Left) || !float.IsFinite(bounds.Top) || !float.IsFinite(bounds.Right) || !float.IsFinite(bounds.Bottom)
            || !float.IsFinite(bounds.Width) || !float.IsFinite(bounds.Height) || !ReportViewportClip.HasPositiveArea(bounds)
            || bounds.Width != MathF.Truncate(bounds.Width) || bounds.Height != MathF.Truncate(bounds.Height))
            throw new ArgumentException("Glyph raster bounds must describe a finite, positive pixel rectangle.", nameof(bounds));
        var bitmap = ReportImageRaster.Create(checked((int)bounds.Width), checked((int)bounds.Height));
        try
        {
            using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint { Color = intrinsicColor ? foreground.WithAlpha(255) : SKColors.White, IsAntialias = true };
            using var builder = new SKTextBlobBuilder();
            Span<ushort> glyphs = stackalloc ushort[1] { glyph };
            Span<SKPoint> positions = stackalloc SKPoint[1] { SKPoint.Empty };
            builder.AddPositionedRun(glyphs, font, positions);
            using var blob = builder.Build() ?? throw new InvalidOperationException("The glyph text blob could not be created.");
            canvas.DrawText(blob, -bounds.Left, -bounds.Top, paint);
            canvas.Flush();
            if (intrinsicColor && foreground.Alpha != 255)
            {
                var pixels = bitmap.GetPixelSpan();
                int alpha = foreground.Alpha;
                for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)((pixels[i] * alpha + 127) / 255);
            }
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }
}
