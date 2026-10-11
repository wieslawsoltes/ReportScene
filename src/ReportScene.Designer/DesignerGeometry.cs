using ReportScene.Model;

namespace ReportScene.Designer;

public enum ResizeHandle { TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left }
public enum DesignerUnit { Millimeters, Centimeters, Inches, Points }

/// <summary>UI-independent report geometry. All stored geometry remains in 1/72-inch points.</summary>
public static class DesignerGeometry
{
    public static double PointsPerUnit(DesignerUnit unit) => unit switch
    {
        DesignerUnit.Millimeters => 72d / 25.4, DesignerUnit.Centimeters => 720d / 25.4,
        DesignerUnit.Inches => 72, DesignerUnit.Points => 1, _ => throw new ArgumentOutOfRangeException(nameof(unit))
    };
    public static string UnitLabel(DesignerUnit unit) => unit switch
    {
        DesignerUnit.Millimeters => "mm", DesignerUnit.Centimeters => "cm", DesignerUnit.Inches => "in",
        DesignerUnit.Points => "pt", _ => throw new ArgumentOutOfRangeException(nameof(unit))
    };
    public static void ValidateBounds(RectD bounds)
    {
        if (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height)
            || !double.IsFinite(bounds.Right) || !double.IsFinite(bounds.Bottom) || bounds.X < 0 || bounds.Y < 0 || bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentException("Object bounds must be finite, positive in size and nonnegative in position.", nameof(bounds));
    }
    public static (double X, double Y) HandlePoint(RectD b, ResizeHandle handle) => handle switch
    {
        ResizeHandle.TopLeft => (b.X, b.Y), ResizeHandle.Top => (b.X + b.Width / 2, b.Y), ResizeHandle.TopRight => (b.Right, b.Y),
        ResizeHandle.Right => (b.Right, b.Y + b.Height / 2), ResizeHandle.BottomRight => (b.Right, b.Bottom),
        ResizeHandle.Bottom => (b.X + b.Width / 2, b.Bottom), ResizeHandle.BottomLeft => (b.X, b.Bottom),
        ResizeHandle.Left => (b.X, b.Y + b.Height / 2), _ => throw new ArgumentOutOfRangeException(nameof(handle))
    };
    public static ResizeHandle? HitHandle(RectD b, double x, double y, double tolerance)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(tolerance) || tolerance < 0) return null;
        // Corners win where very small objects have overlapping handle hit regions.
        ReadOnlySpan<ResizeHandle> order = [ResizeHandle.TopLeft, ResizeHandle.TopRight, ResizeHandle.BottomRight, ResizeHandle.BottomLeft,
            ResizeHandle.Top, ResizeHandle.Right, ResizeHandle.Bottom, ResizeHandle.Left];
        foreach (var handle in order)
        { var p = HandlePoint(b, handle); if (Math.Abs(p.X - x) <= tolerance && Math.Abs(p.Y - y) <= tolerance) return handle; }
        return null;
    }
    public static RectD Resize(RectD b, double dx, double dy, ResizeHandle handle, double minimumSize = 4)
    {
        ValidateBounds(b);
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) throw new ArgumentException("Resize deltas must be finite.");
        if (!double.IsFinite(minimumSize) || minimumSize <= 0) throw new ArgumentOutOfRangeException(nameof(minimumSize));
        if (!Enum.IsDefined(handle)) throw new ArgumentOutOfRangeException(nameof(handle));
        bool left = handle is ResizeHandle.TopLeft or ResizeHandle.Left or ResizeHandle.BottomLeft;
        bool right = handle is ResizeHandle.TopRight or ResizeHandle.Right or ResizeHandle.BottomRight;
        bool top = handle is ResizeHandle.TopLeft or ResizeHandle.Top or ResizeHandle.TopRight;
        bool bottom = handle is ResizeHandle.BottomLeft or ResizeHandle.Bottom or ResizeHandle.BottomRight;
        double x = b.X, y = b.Y, width = b.Width, height = b.Height;
        if (left) { x = Math.Clamp(b.X + dx, 0, Math.Max(0, b.Right - minimumSize)); width = b.Right - x; }
        if (right) width = Math.Max(minimumSize, b.Width + dx);
        if (top) { y = Math.Clamp(b.Y + dy, 0, Math.Max(0, b.Bottom - minimumSize)); height = b.Bottom - y; }
        if (bottom) height = Math.Max(minimumSize, b.Height + dy);
        var result = new RectD(x, y, width, height); ValidateBounds(result); return result;
    }
}
