using ReportScene.Model;
using ReportScene.Rendering.Skia;

namespace ReportScene.Validation;

public static class ReportRasterViewportTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action)
        {
            try { action(); return new("GPU raster viewport: " + name, true); }
            catch (Exception error) { return new("GPU raster viewport: " + name, false, error.ToString()); }
        }

        yield return Test("Retina doubles density without changing logical extent", () =>
        {
            var visible = new RectD(0, 0, 600, 400);
            Conformance.Equal(new ReportRasterViewport(visible, 1200, 800), ReportRasterViewport.Calculate(visible, 32, 48, 2));
            Conformance.Equal(new ReportRasterViewport(visible, 600, 400), ReportRasterViewport.Calculate(visible, 32, 48, 1));
        });

        yield return Test("fractional root origin expands each edge to a whole physical pixel", () =>
        {
            var actual = ReportRasterViewport.Calculate(new(3.1, 10.75, 17.2, 8.6), .2, -5.25, 1.25);
            Conformance.Equal(22, actual.PixelWidth); Conformance.Equal(12, actual.PixelHeight);
            Near(3, actual.Bounds.X); Near(10.05, actual.Bounds.Y);
            Near(17.6, actual.Bounds.Width); Near(9.6, actual.Bounds.Height);
            Near(4, (actual.Bounds.X + .2) * 1.25);
            Near(6, (actual.Bounds.Y - 5.25) * 1.25);
        });

        yield return Test("distant scroll offsets allocate only the visible viewport", () =>
        {
            foreach (double offset in new[] { 1000d, 1000000d, 10000000d })
            {
                var actual = ReportRasterViewport.Calculate(new(offset + .5, offset + .875, 123.25, 98.125),
                    -offset - .25, -offset - .75, 2);
                Conformance.Equal(247, actual.PixelWidth); Conformance.Equal(197, actual.PixelHeight);
                Near(offset + .25, actual.Bounds.X); Near(offset + .75, actual.Bounds.Y);
                Near(123.5, actual.Bounds.Width); Near(98.5, actual.Bounds.Height);
                Near(0, (actual.Bounds.X - offset - .25) * 2);
            }
        });

        yield return Test("whole-pixel moves retain raster phase while fractional moves update it", () =>
        {
            var visible = new RectD(10.25, 20.5, 120.125, 90.25);
            var first = ReportRasterViewport.Calculate(visible, 3.2, 6.4, 1.25);
            var wholePixelMove = ReportRasterViewport.Calculate(visible, 11.2, 22.4, 1.25);
            Conformance.Equal(first.PixelWidth, wholePixelMove.PixelWidth);
            Conformance.Equal(first.PixelHeight, wholePixelMove.PixelHeight);
            Near(first.Bounds.X, wholePixelMove.Bounds.X); Near(first.Bounds.Y, wholePixelMove.Bounds.Y);
            Conformance.Equal(true, first.HasSamePixelMapping(wholePixelMove, 1.25));
            var fractionalMove = ReportRasterViewport.Calculate(visible, 3.6, 6.4, 1.25);
            Conformance.Equal(true, Math.Abs(first.Bounds.X - fractionalMove.Bounds.X) > .1);
            Conformance.Equal(false, first.HasSamePixelMapping(fractionalMove, 1.25));
            Near(0, (fractionalMove.Bounds.X + 3.6) * 1.25 - Math.Round((fractionalMove.Bounds.X + 3.6) * 1.25));
        });

        yield return Test("fractional scales preserve coverage and one-to-one pixel dimensions", () =>
        {
            foreach (double scale in new[] { .75, 1, 1.25, 1.5, 2, 3 })
                foreach (double origin in new[] { -173.125, -.4, 0, .2, 351.75 })
                    foreach (double offset in new[] { 0, .125, 49.7, 100000.25 })
                    {
                        var visible = new RectD(offset, offset + .2, 641.3, 479.15);
                        var actual = ReportRasterViewport.Calculate(visible, origin - offset, -origin - offset, scale);
                        double tolerance = 1e-8;
                        Conformance.Equal(true, actual.Bounds.X <= visible.X + tolerance && actual.Bounds.Y <= visible.Y + tolerance);
                        Conformance.Equal(true, actual.Bounds.Right >= visible.Right - tolerance && actual.Bounds.Bottom >= visible.Bottom - tolerance);
                        Conformance.Equal(true, (visible.X - actual.Bounds.X) * scale < 1 + tolerance);
                        Conformance.Equal(true, (actual.Bounds.Right - visible.Right) * scale < 1 + tolerance);
                        Conformance.Equal(true, (visible.Y - actual.Bounds.Y) * scale < 1 + tolerance);
                        Conformance.Equal(true, (actual.Bounds.Bottom - visible.Bottom) * scale < 1 + tolerance);
                        Near(actual.PixelWidth, actual.Bounds.Width * scale);
                        Near(actual.PixelHeight, actual.Bounds.Height * scale);
                        double x = (origin - offset + actual.Bounds.X) * scale;
                        double y = (-origin - offset + actual.Bounds.Y) * scale;
                        Near(Math.Round(x), x); Near(Math.Round(y), y);
                    }
        });

        yield return Test("empty invalid and unrepresentable surfaces cannot request textures", () =>
        {
            Conformance.Equal(default(ReportRasterViewport), ReportRasterViewport.Calculate(new(10, 20, 0, 10), 0, 0, 2));
            Conformance.Equal(default(ReportRasterViewport), ReportRasterViewport.Calculate(new(10, 20, 10, 0), 0, 0, 2));
            foreach (double scale in new[] { 0, -1, double.NaN, double.PositiveInfinity, double.Epsilon, 1e300 })
                Conformance.Throws<ArgumentOutOfRangeException>(() => ReportRasterViewport.Calculate(new(0, 0, 10, 10), 0, 0, scale));
            foreach (var rect in new[] { new RectD(0, 0, -1, 10), new RectD(double.NaN, 0, 10, 10),
                new RectD(0, 0, double.PositiveInfinity, 10), new RectD(0, 0, 16777217, 10), new RectD(double.MaxValue, 0, double.MaxValue, 10) })
                Conformance.Throws<ArgumentException>(() => ReportRasterViewport.Calculate(rect, 0, 0, 1));
            Conformance.Throws<ArgumentException>(() => ReportRasterViewport.Calculate(new(0, 0, 10, 10), double.NaN, 0, 1));
            Conformance.Throws<ArgumentException>(() => ReportRasterViewport.Calculate(new(0, 0, 10, 10), 9007199254740992, 0, 1));
        });
    }

    private static void Near(double expected, double actual)
    {
        if (!double.IsFinite(actual) || Math.Abs(expected - actual) > 1e-8)
            throw new InvalidOperationException($"Expected {expected:R}; actual {actual:R}.");
    }
}
