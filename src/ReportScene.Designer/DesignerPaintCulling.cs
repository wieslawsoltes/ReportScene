using ReportScene.Model;

namespace ReportScene.Designer;

/// <summary>Conservative authoring culling in report points. Report/object strokes and six-DIP selection adorners may paint beyond nominal item bounds.</summary>
public static class DesignerPaintCulling
{
    public static bool IsVisible(DesignObject item, RectD viewport, double pointScale)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!double.IsFinite(pointScale) || pointScale <= 0) throw new ArgumentOutOfRangeException(nameof(pointScale));
        Validate(viewport, nameof(viewport)); Validate(item.Bounds, nameof(item));
        double border = item.Item.Style.BorderWidth;
        if (!double.IsFinite(border) || border < 0) throw new ArgumentException("Border width must be finite and nonnegative.", nameof(item));
        if (viewport.Width == 0 || viewport.Height == 0) return false;
        // Match renderer-owned strokes as well as explicit report borders. The
        // checkbox outline scales with its side, even with BorderWidth == 0.
        double stroke = border;
        if (item.Item is ShapeItem { Shape: ShapeKind.Line }) stroke = Math.Max(stroke, .5);
        if (item.Item is CheckBoxItem) stroke = Math.Max(stroke, Math.Max(1, Math.Min(item.Bounds.Width, item.Bounds.Height) / 14));
        double tolerance = Math.Max(stroke / 2, 6 / pointScale);
        // Saturation of a conservative bound at infinity is safe: it visits extra
        // content rather than discarding a potentially visible stroke. Geometry
        // and scale themselves are validated above and remain finite.
        var bounds = item.Bounds;
        return bounds.Right + tolerance >= viewport.X && bounds.X - tolerance <= viewport.Right
            && bounds.Bottom + tolerance >= viewport.Y && bounds.Y - tolerance <= viewport.Bottom;
    }
    private static void Validate(RectD bounds, string parameter)
    {
        if (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) || !double.IsFinite(bounds.Width)
            || !double.IsFinite(bounds.Height) || !double.IsFinite(bounds.Right) || !double.IsFinite(bounds.Bottom)
            || bounds.Width < 0 || bounds.Height < 0)
            throw new ArgumentException("Culling bounds must be finite with nonnegative size.", parameter);
    }
}
