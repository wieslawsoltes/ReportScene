namespace ReportScene.Model;

/// <summary>Geometric paint extents for built-in report objects, distinct from nominal search/bookmark areas.</summary>
public static class PreparedPaintBounds
{
    /// <summary>Includes explicit borders and renderer-owned line/checkbox strokes, using prepared dimensions.</summary>
    public static double StrokeOutset(ReportItem source, RectD bounds)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(source.Style);
        Validate(bounds, nameof(bounds), nonnegative: true);
        double border = source.Style.BorderWidth;
        if (!double.IsFinite(border) || border < 0 || !float.IsFinite((float)border))
            throw new ArgumentException("Border width must be finite, nonnegative and representable by the renderer.", nameof(source));
        // The renderer submits binary32 stroke widths. Rounding up cannot be
        // ignored when constructing a conservative double-precision index.
        double width = Math.Max(border, (double)(float)border);
        if (source is ShapeItem { Shape: ShapeKind.Line }) width = Math.Max(width, .5);
        if (source is CheckBoxItem)
        {
            double side = Math.Min(bounds.Width, bounds.Height);
            if (!float.IsFinite((float)side)) throw new ArgumentException("Checkbox size exceeds the renderer's range.", nameof(bounds));
            width = Math.Max(width, Math.Max(1, Math.Max(side / 14, (double)((float)side / 14f))));
        }
        return width / 2;
    }

    /// <summary>Conservative stroke extents intersected with the paper and optional prepared-item clip.</summary>
    public static RectD? VisibleBounds(PreparedPage page, PreparedItem item)
    {
        ArgumentNullException.ThrowIfNull(page); ArgumentNullException.ThrowIfNull(item);
        if (!double.IsFinite(page.Width) || !double.IsFinite(page.Height) || page.Width <= 0 || page.Height <= 0)
            throw new ArgumentException("Page dimensions must be finite and positive.", nameof(page));
        double outset = StrokeOutset(item.Source, item.Bounds);
        if (item.Clip is { } clip)
        {
            Validate(clip, nameof(item), nonnegative: false);
            // Match DrawItem: a finite zero/negative clip suppresses drawing.
            if (clip.Width <= 0 || clip.Height <= 0) return null;
        }
        var b = item.Bounds;
        // Clamp expanded edges before forming dimensions, avoiding overflow in
        // width + 2*outset for finite extreme values outside the paper.
        double left = Math.Max(0, b.X - outset), top = Math.Max(0, b.Y - outset);
        double right = Math.Min(page.Width, b.Right + outset), bottom = Math.Min(page.Height, b.Bottom + outset);
        if (item.Clip is { } c)
        {
            left = Math.Max(left, c.X); top = Math.Max(top, c.Y);
            right = Math.Min(right, c.Right); bottom = Math.Min(bottom, c.Bottom);
        }
        return right > left && bottom > top ? new RectD(left, top, right - left, bottom - top) : null;
    }

    private static void Validate(RectD b, string parameter, bool nonnegative)
    {
        if (!double.IsFinite(b.X) || !double.IsFinite(b.Y) || !double.IsFinite(b.Width) || !double.IsFinite(b.Height)
            || !double.IsFinite(b.Right) || !double.IsFinite(b.Bottom) || nonnegative && (b.Width < 0 || b.Height < 0))
            throw new ArgumentException("Paint geometry must be finite with nonnegative object size.", parameter);
    }
}
