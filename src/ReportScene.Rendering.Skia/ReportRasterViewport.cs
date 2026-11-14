using ReportScene.Model;

namespace ReportScene.Rendering.Skia;

/// <summary>
/// A logical viewport expanded to whole root-device pixels. The expansion is less
/// than one physical pixel per edge and does not change document coordinates.
/// </summary>
public readonly record struct ReportRasterViewport(RectD Bounds, int PixelWidth, int PixelHeight)
{
    // Uno stores a visual's size in Vector2. Larger integers are not all exactly
    // representable there; the active GPU's texture limit is normally much lower.
    private const int MaximumExactPixelSize = 1 << 24;

    /// <summary>
    /// Compares an existing raster mapping without treating double-precision
    /// cancellation from a whole-pixel ancestor move as new content. This only
    /// suppresses invalidation below 1e-7 physical pixels; it never rounds or
    /// changes the outward bounds produced by <see cref="Calculate"/>.
    /// </summary>
    public bool HasSamePixelMapping(ReportRasterViewport other, double rasterizationScale)
        => double.IsFinite(rasterizationScale) && rasterizationScale > 0
            && PixelWidth == other.PixelWidth && PixelHeight == other.PixelHeight
            && Math.Abs(Bounds.X - other.Bounds.X) * rasterizationScale <= 1e-7
            && Math.Abs(Bounds.Y - other.Bounds.Y) * rasterizationScale <= 1e-7
            && Math.Abs(Bounds.Width - other.Bounds.Width) * rasterizationScale <= 1e-7
            && Math.Abs(Bounds.Height - other.Bounds.Height) * rasterizationScale <= 1e-7;

    /// <summary>
    /// Maps a translated element's viewport onto the root's physical pixel grid.
    /// The origin is in root DIPs and the viewport is in element-local DIPs.
    /// Empty viewports stay empty. Invalid or unrepresentable geometry is rejected
    /// before it can become a native texture allocation.
    /// </summary>
    public static ReportRasterViewport Calculate(RectD viewport, double rootOriginX, double rootOriginY,
        double rasterizationScale)
    {
        if (!double.IsFinite(rasterizationScale) || rasterizationScale <= 0
            || !float.IsFinite((float)rasterizationScale) || (float)rasterizationScale == 0
            || !float.IsFinite((float)(1 / rasterizationScale)))
            throw new ArgumentOutOfRangeException(nameof(rasterizationScale), "The rasterization scale must have a finite positive inverse.");
        if (!Finite(viewport) || viewport.Width < 0 || viewport.Height < 0
            || !double.IsFinite(rootOriginX) || !double.IsFinite(rootOriginY))
            throw new ArgumentException("The viewport and root origin must be finite, with nonnegative dimensions.", nameof(viewport));
        if (viewport.Width == 0 || viewport.Height == 0) return default;

        double left = Math.Floor((rootOriginX + viewport.X) * rasterizationScale);
        double top = Math.Floor((rootOriginY + viewport.Y) * rasterizationScale);
        double right = Math.Ceiling((rootOriginX + viewport.Right) * rasterizationScale);
        double bottom = Math.Ceiling((rootOriginY + viewport.Bottom) * rasterizationScale);
        double width = right - left, height = bottom - top;
        // Beyond 2^52 a double cannot retain every neighbouring pixel edge.
        const double maximumExactPixelEdge = 4503599627370496;
        if (!double.IsFinite(left) || !double.IsFinite(top) || !double.IsFinite(right) || !double.IsFinite(bottom)
            || Math.Abs(left) > maximumExactPixelEdge || Math.Abs(top) > maximumExactPixelEdge
            || Math.Abs(right) > maximumExactPixelEdge || Math.Abs(bottom) > maximumExactPixelEdge
            || width < 1 || height < 1 || width > MaximumExactPixelSize || height > MaximumExactPixelSize)
            throw new ArgumentException("The physical viewport must have representable, positive integer dimensions.", nameof(viewport));

        var aligned = new RectD(left / rasterizationScale - rootOriginX, top / rasterizationScale - rootOriginY,
            width / rasterizationScale, height / rasterizationScale);
        if (!Finite(aligned) || !float.IsFinite((float)aligned.X) || !float.IsFinite((float)aligned.Y)
            || !float.IsFinite((float)aligned.Right) || !float.IsFinite((float)aligned.Bottom))
            throw new ArgumentException("The logical viewport cannot be represented by the drawing surface.", nameof(viewport));
        return new(aligned, checked((int)width), checked((int)height));
    }

    private static bool Finite(RectD rect) => double.IsFinite(rect.X) && double.IsFinite(rect.Y)
        && double.IsFinite(rect.Width) && double.IsFinite(rect.Height)
        && double.IsFinite(rect.Right) && double.IsFinite(rect.Bottom);
}
