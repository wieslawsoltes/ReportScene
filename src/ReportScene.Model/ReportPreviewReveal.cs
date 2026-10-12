namespace ReportScene.Model;

public readonly record struct PreviewScrollTarget(double HorizontalOffset, double VerticalOffset);

/// <summary>Minimal two-axis reveal from a target's actual viewport-relative DIP bounds. Oversized targets align their leading edge.</summary>
public static class ReportPreviewReveal
{
    public static PreviewScrollTarget Calculate(RectD target, double viewportWidth, double viewportHeight,
        double horizontalOffset, double verticalOffset, double extentWidth, double extentHeight, double margin = 16)
    {
        if (!double.IsFinite(target.X) || !double.IsFinite(target.Y) || !double.IsFinite(target.Right) || !double.IsFinite(target.Bottom)
            || !double.IsFinite(target.Width) || !double.IsFinite(target.Height) || target.Width < 0 || target.Height < 0)
            throw new ArgumentException("Reveal bounds must be finite with nonnegative size.", nameof(target));
        Validate(viewportWidth); Validate(viewportHeight); Validate(horizontalOffset); Validate(verticalOffset);
        Validate(extentWidth); Validate(extentHeight); Validate(margin);
        double Axis(double start, double size, double viewport, double offset, double extent)
        {
            double maximum = Math.Max(0, extent - viewport);
            if (viewport == 0 || size == 0) return Math.Clamp(offset, 0, maximum);
            double inset = Math.Min(margin, viewport / 4), delta = 0;
            if (size > viewport - 2 * inset || start < inset) delta = start - inset;
            else if (start + size > viewport - inset) delta = start + size - viewport + inset;
            // Finite operands can still overflow when added. Saturate only the
            // scroll result, which is bounded by the already-validated extent.
            return Math.Clamp(offset + delta, 0, maximum);
        }
        return new(Axis(target.X, target.Width, viewportWidth, horizontalOffset, extentWidth),
            Axis(target.Y, target.Height, viewportHeight, verticalOffset, extentHeight));
    }
    private static void Validate(double value)
    { if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Viewport, extent, offsets and margin must be finite and nonnegative."); }
}
