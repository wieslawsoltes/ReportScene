using ReportScene.Model;
using SkiaSharp;

namespace ReportScene.Rendering.Skia;

/// <summary>
/// Applies a known native viewport to a recording canvas, without querying its
/// potentially quantized LocalClipBounds. The caller owns Save/Restore scope.
/// </summary>
public static class ReportViewportClip
{
    /// <summary>
    /// Tests geometric area, including rectangles away from the origin.
    /// SKRect.IsEmpty only identifies the all-zero sentinel in SkiaSharp.
    /// </summary>
    public static bool HasPositiveArea(SKRect rect) => rect.Right > rect.Left && rect.Bottom > rect.Top;

    /// <summary>Intersects positive-area bounds, returning the canonical empty rectangle for disjoint or touching edges.</summary>
    public static SKRect Intersect(SKRect a, SKRect b)
    {
        if (!HasPositiveArea(a) || !HasPositiveArea(b)) return SKRect.Empty;
        float left = Math.Max(a.Left, b.Left), top = Math.Max(a.Top, b.Top);
        float right = Math.Min(a.Right, b.Right), bottom = Math.Min(a.Bottom, b.Bottom);
        return right > left && bottom > top ? new(left, top, right, bottom) : SKRect.Empty;
    }

    public static SKRect Apply(SKCanvas canvas, RectD viewport)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var clip = Bounds(viewport);
        canvas.ClipRect(clip, SKClipOperation.Intersect, antialias: false);
        return clip;
    }

    public static SKRect Apply(ReportCanvas canvas, RectD viewport)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var clip = Bounds(viewport);
        canvas.ClipRect(clip, SKClipOperation.Intersect, antialias: false);
        return clip;
    }

    /// <summary>Converts each edge outwards so binary32 conversion never cuts inward.</summary>
    public static SKRect Bounds(RectD viewport)
    {
        if (!double.IsFinite(viewport.X) || !double.IsFinite(viewport.Y)
            || !double.IsFinite(viewport.Width) || !double.IsFinite(viewport.Height)
            || !double.IsFinite(viewport.Right) || !double.IsFinite(viewport.Bottom)
            || viewport.Width < 0 || viewport.Height < 0
            || Math.Abs(viewport.X) > float.MaxValue || Math.Abs(viewport.Y) > float.MaxValue
            || Math.Abs(viewport.Right) > float.MaxValue || Math.Abs(viewport.Bottom) > float.MaxValue)
            throw new ArgumentException("Viewport edges must be finite and representable by Skia.", nameof(viewport));
        if (viewport.Width == 0 || viewport.Height == 0) return SKRect.Empty;
        static float Lower(double value) { float result = (float)value; return result > value ? MathF.BitDecrement(result) : result; }
        static float Upper(double value) { float result = (float)value; return result < value ? MathF.BitIncrement(result) : result; }
        return new(Lower(viewport.X), Lower(viewport.Y), Upper(viewport.Right), Upper(viewport.Bottom));
    }
}
