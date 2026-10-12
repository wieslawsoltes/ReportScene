namespace ReportScene.Model;

/// <summary>Converts actual paper-element bounds in viewport DIPs into a conservative visible rectangle in report points.</summary>
public static class ReportPreviewViewport
{
    // Layout rounds element sizes to device pixels. Include a small guard without
    // depending on a recording canvas's possibly quantized clip-query result.
    public const double PixelGuard = 2;
    public static RectD Calculate(RectD paperBounds, double viewportWidth, double viewportHeight, double pointScale)
    {
        if (!double.IsFinite(paperBounds.X) || !double.IsFinite(paperBounds.Y)
            || !double.IsFinite(paperBounds.Right) || !double.IsFinite(paperBounds.Bottom)
            || paperBounds.Width < 0 || paperBounds.Height < 0)
            throw new ArgumentException("Paper-element bounds must be finite and nonnegative in size.", nameof(paperBounds));
        if (!double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight) || viewportWidth < 0 || viewportHeight < 0)
            throw new ArgumentException("Viewport dimensions must be finite and nonnegative.");
        if (!double.IsFinite(pointScale) || pointScale <= 0) throw new ArgumentOutOfRangeException(nameof(pointScale));
        if (viewportWidth == 0 || viewportHeight == 0 || paperBounds.Width == 0 || paperBounds.Height == 0) return default;
        var visible = PreparedVisibility.Intersect(paperBounds, new(-PixelGuard, -PixelGuard, viewportWidth + 2 * PixelGuard, viewportHeight + 2 * PixelGuard));
        if (visible.Width <= 0 || visible.Height <= 0) return default;
        var result = new RectD((visible.X - paperBounds.X) / pointScale, (visible.Y - paperBounds.Y) / pointScale,
            visible.Width / pointScale, visible.Height / pointScale);
        if (!double.IsFinite(result.X) || !double.IsFinite(result.Y) || !double.IsFinite(result.Right) || !double.IsFinite(result.Bottom))
            throw new ArgumentOutOfRangeException(nameof(pointScale), "The point-space viewport is not representable.");
        return result;
    }
}
