using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace ReportScene.Rendering.Skia;

/// <summary>
/// Embeds solid-fill intrinsic color glyphs as self-contained images for drawing
/// backends, such as Skia SVG, that cannot preserve native color glyphs or opacity
/// layers. Ordinary text remains native vector text. DPI is measured in report
/// points (72 per inch), and the canvas transform contributes its maximum scale.
/// MaximumPixels limits each visible glyph image; the shared hard limits remain
/// 16,384 pixels on either axis and 32 megapixels. Clipping precedes allocation.
/// </summary>
public sealed record ColorGlyphRasterOptions(double Dpi = 144, int MaximumPixels = ReportImageRaster.MaximumDecodedPixels)
{
    internal void Validate()
    {
        if (!double.IsFinite(Dpi) || Dpi < 36 || Dpi > 1200 || MaximumPixels <= 0)
            throw new ArgumentOutOfRangeException(nameof(ColorGlyphRasterOptions), "Color glyph raster options require 36–1200 DPI and a positive pixel limit.");
    }
}

/// <summary>Shared CPU glyph rasterization for bounded GPU atlases and headless validation.</summary>
public static class ReportGlyphRaster
{
    public const int CoverageGuard = 2;
    public const int MaximumRasterDimension = 16_384;
    public const int MaximumOpacityLayerDimension = 2048;

    // The public metrics API also accepts arbitrary native paints. Preserve its
    // original drawing path for those paints rather than dropping effects or
    // changing their compositing order. ReportRenderer uses this solid-fill subset.
    internal static bool IsSolidSourceOver(SKPaint paint) => paint.Style == SKPaintStyle.Fill
        && paint.BlendMode == SKBlendMode.SrcOver && paint.Blender is null
        && paint.Shader is null && paint.ColorFilter is null && paint.ImageFilter is null
        && paint.MaskFilter is null && paint.PathEffect is null;

    /// <summary>Preserves native vector glyphs while applying paint opacity once per shaped glyph.</summary>
    internal static void DrawWithOpacity(SKCanvas canvas, SKFont font, SKShaper.Result shaped,
        float x, float baseline, SKPaint paint)
    {
        if (paint.Color.Alpha == 0 || canvas.IsClipEmpty) return;
        using var opaque = paint.Clone(); opaque.Color = paint.Color.WithAlpha(255);
        using var composite = new SKPaint { Color = SKColors.White.WithAlpha(paint.Color.Alpha) };
        var matrix = canvas.TotalMatrix;
        var matrix44 = canvas.TotalMatrix44;
        var clip = canvas.DeviceClipBounds;
        const int batchSize = 128;
        Span<ushort> glyphs = stackalloc ushort[batchSize];
        Span<SKRect> bounds = stackalloc SKRect[batchSize];
        Span<SKPoint> origin = stackalloc SKPoint[1] { SKPoint.Empty };
        using var builder = new SKTextBlobBuilder();
        for (int start = 0; start < shaped.Codepoints.Length; start += batchSize)
        {
            int count = Math.Min(batchSize, shaped.Codepoints.Length - start);
            for (int i = 0; i < count; i++) glyphs[i] = checked((ushort)shaped.Codepoints[start + i]);
            font.GetGlyphWidths(glyphs[..count], Span<float>.Empty, bounds[..count]);
            for (int i = 0; i < count; i++)
            {
                if (!ReportViewportClip.HasPositiveArea(bounds[i])) continue;
                var point = shaped.Points[start + i];
                float glyphX = x + point.X, glyphY = baseline + point.Y;
                var ink = bounds[i]; ink.Offset(glyphX, glyphY);
                var device = matrix.MapRect(ink);
                RequireFinite(device);
                // Native hinted glyphs may extend to the next device pixel.
                // Round and guard in device space, then clip before any layer.
                double clipLeft = Math.Max(clip.Left, Math.Floor(device.Left) - CoverageGuard);
                double clipTop = Math.Max(clip.Top, Math.Floor(device.Top) - CoverageGuard);
                double clipRight = Math.Min(clip.Right, Math.Ceiling(device.Right) + CoverageGuard);
                double clipBottom = Math.Min(clip.Bottom, Math.Ceiling(device.Bottom) + CoverageGuard);
                if (clipRight <= clipLeft || clipBottom <= clipTop) continue;
                int left = checked((int)clipLeft), top = checked((int)clipTop);
                int right = checked((int)clipRight), bottom = checked((int)clipBottom);
                builder.AddPositionedRun(glyphs.Slice(i, 1), font, origin);
                using var blob = builder.Build() ?? throw new InvalidOperationException("The glyph text blob could not be created.");
                // SaveLayer bounds are only an allocation hint. An explicit,
                // non-antialiased device clip both enforces the layer limit and
                // partitions unusually large glyphs without overlap or seams.
                for (int tileTop = top; tileTop < bottom;)
                {
                    int tileBottom = (int)Math.Min((long)tileTop + MaximumOpacityLayerDimension, bottom);
                    for (int tileLeft = left; tileLeft < right;)
                    {
                        int tileRight = (int)Math.Min((long)tileLeft + MaximumOpacityLayerDimension, right);
                        var tile = new SKRect(tileLeft, tileTop, tileRight, tileBottom);
                        int saved = canvas.Save();
                        try
                        {
                            canvas.ResetMatrix();
                            canvas.ClipRect(tile, SKClipOperation.Intersect, antialias: false);
                            canvas.SaveLayer(tile, composite);
                            canvas.SetMatrix(matrix44);
                            canvas.DrawText(blob, glyphX, glyphY, opaque);
                        }
                        finally { canvas.RestoreToCount(saved); }
                        tileLeft = tileRight;
                    }
                    tileTop = tileBottom;
                }
            }
        }
    }

    internal static float GetRasterScale(SKCanvas canvas, ColorGlyphRasterOptions options)
    {
        options.Validate();
        var m = canvas.TotalMatrix;
        if (m.Persp0 != 0 || m.Persp1 != 0 || m.Persp2 != 1)
            throw new NotSupportedException("Color glyph image export requires an affine canvas transform.");
        // Largest singular value of the affine 2x2 matrix, including shear and
        // rotation. A max of ScaleX/ScaleY alone undersamples rotated transforms.
        double a = m.ScaleX, b = m.SkewX, c = m.SkewY, d = m.ScaleY;
        double scale = options.Dpi / 72 * (Math.Sqrt((a + d) * (a + d) + (c - b) * (c - b))
            + Math.Sqrt((a - d) * (a - d) + (c + b) * (c + b))) * .5;
        if (!double.IsFinite(scale) || scale > float.MaxValue)
            throw new InvalidOperationException("The color glyph raster scale is not representable.");
        return (float)scale;
    }

    internal static void DrawRasterized(SKCanvas canvas, SKFont rasterFont, SKShaper.Result shaped,
        float x, float baseline, SKPaint paint, float scale, ColorGlyphRasterOptions options)
    {
        if (paint.Color.Alpha == 0 || canvas.IsClipEmpty || scale <= 0) return;
        var clip = canvas.LocalClipBounds;
        if (!ReportViewportClip.HasPositiveArea(clip)) return;
        const int batchSize = 128;
        Span<ushort> glyphs = stackalloc ushort[batchSize];
        Span<SKRect> bounds = stackalloc SKRect[batchSize];
        for (int start = 0; start < shaped.Codepoints.Length; start += batchSize)
        {
            int count = Math.Min(batchSize, shaped.Codepoints.Length - start);
            for (int i = 0; i < count; i++) glyphs[i] = checked((ushort)shaped.Codepoints[start + i]);
            rasterFont.GetGlyphWidths(glyphs[..count], Span<float>.Empty, bounds[..count]);
            for (int i = 0; i < count; i++)
            {
                if (!ReportViewportClip.HasPositiveArea(bounds[i])) continue;
                var point = shaped.Points[start + i];
                float glyphX = x + point.X, glyphY = baseline + point.Y;
                var ink = new SKRect(MathF.Floor(bounds[i].Left) - CoverageGuard, MathF.Floor(bounds[i].Top) - CoverageGuard,
                    MathF.Ceiling(bounds[i].Right) + CoverageGuard, MathF.Ceiling(bounds[i].Bottom) + CoverageGuard);
                var visible = new SKRect((clip.Left - glyphX) * scale, (clip.Top - glyphY) * scale,
                    (clip.Right - glyphX) * scale, (clip.Bottom - glyphY) * scale);
                if (!ReportViewportClip.HasPositiveArea(ReportViewportClip.Intersect(ink, visible))) continue;
                // Preserve interpolation support when clipping a glyph image;
                // the destination canvas retains the original report clip.
                visible = new(MathF.Floor(visible.Left) - CoverageGuard, MathF.Floor(visible.Top) - CoverageGuard,
                    MathF.Ceiling(visible.Right) + CoverageGuard, MathF.Ceiling(visible.Bottom) + CoverageGuard);
                var rasterBounds = ReportViewportClip.Intersect(ink, visible);
                RequireFinite(rasterBounds);
                if (rasterBounds.Width > MaximumRasterDimension || rasterBounds.Height > MaximumRasterDimension
                    || (double)rasterBounds.Width * rasterBounds.Height > Math.Min(options.MaximumPixels, ReportImageRaster.MaximumDecodedPixels))
                    throw new InvalidOperationException("The visible color glyph raster exceeds the configured pixel limit or the 16,384-pixel / 32-megapixel glyph limit.");
                using var bitmap = Rasterize(rasterFont, glyphs[i], rasterBounds, paint.Color, intrinsicColor: true);
                using var image = SKImage.FromBitmap(bitmap);
                var target = new SKRect(glyphX + rasterBounds.Left / scale, glyphY + rasterBounds.Top / scale,
                    glyphX + rasterBounds.Right / scale, glyphY + rasterBounds.Bottom / scale);
                canvas.DrawImage(image, target, new SKSamplingOptions(SKFilterMode.Linear));
            }
        }
    }

    private static void RequireFinite(SKRect bounds)
    {
        if (!float.IsFinite(bounds.Left) || !float.IsFinite(bounds.Top) || !float.IsFinite(bounds.Right) || !float.IsFinite(bounds.Bottom)
            || !float.IsFinite(bounds.Width) || !float.IsFinite(bounds.Height))
            throw new InvalidOperationException("Color glyph bounds must be finite and representable.");
    }

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
