using ReportScene.Model;

namespace ReportScene.Designer;

/// <summary>Conservative authoring culling in report points, sharing built-in stroke extents with prepared rendering.</summary>
public static class DesignerPaintCulling
{
    public static bool IsVisible(DesignObject item, RectD viewport, double pointScale)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!double.IsFinite(pointScale) || pointScale <= 0) throw new ArgumentOutOfRangeException(nameof(pointScale));
        Validate(viewport, nameof(viewport));
        double outset = PreparedPaintBounds.StrokeOutset(item.Item, item.Bounds);
        if (viewport.Width == 0 || viewport.Height == 0) return false;
        double tolerance = Math.Max(outset, 6 / pointScale);
        var b = item.Bounds;
        return b.Right + tolerance >= viewport.X && b.X - tolerance <= viewport.Right
            && b.Bottom + tolerance >= viewport.Y && b.Y - tolerance <= viewport.Bottom;
    }
    private static void Validate(RectD b, string parameter)
    {
        if (!double.IsFinite(b.X) || !double.IsFinite(b.Y) || !double.IsFinite(b.Width) || !double.IsFinite(b.Height)
            || !double.IsFinite(b.Right) || !double.IsFinite(b.Bottom) || b.Width < 0 || b.Height < 0)
            throw new ArgumentException("Culling bounds must be finite with nonnegative size.", parameter);
    }
}
