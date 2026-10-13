using ReportScene.Model;
using SkiaSharp;

namespace ReportScene.Rendering.Skia;

/// <summary>
/// Applies a known native viewport to a recording canvas, without querying its
/// potentially quantized LocalClipBounds. The caller owns Save/Restore scope.
/// </summary>
public static class ReportViewportClip
{
    public static SKRect Apply(SKCanvas canvas, RectD viewport)
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
            || !float.IsFinite((float)viewport.X) || !float.IsFinite((float)viewport.Y)
            || !float.IsFinite((float)viewport.Right) || !float.IsFinite((float)viewport.Bottom))
            throw new ArgumentException("Viewport edges must be finite and representable by Skia.", nameof(viewport));
        if (viewport.Width == 0 || viewport.Height == 0) return SKRect.Empty;
        static float Lower(double value) { float result = (float)value; return result > value ? MathF.BitDecrement(result) : result; }
        static float Upper(double value) { float result = (float)value; return result < value ? MathF.BitIncrement(result) : result; }
        return new(Lower(viewport.X), Lower(viewport.Y), Upper(viewport.Right), Upper(viewport.Bottom));
    }
}
