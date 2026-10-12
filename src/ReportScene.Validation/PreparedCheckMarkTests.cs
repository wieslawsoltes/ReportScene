using ReportScene.Model;
using ReportScene.Rendering.Skia;
using SkiaSharp;

namespace ReportScene.Validation;

public static class PreparedCheckMarkTests
{
    public static IEnumerable<TestResult> Run()
    {
        foreach (double side in new[] { 1d, .25 })
        {
            TestResult result;
            try
            {
                var bounds = new RectD(2, 2, side, side);
                var source = new CheckBoxItem { Style = new() { Foreground = "#108840" } };
                var item = new PreparedItem(source, bounds, "True");
                var page = new PreparedPage(1, 6, 6, [item]);
                double stroke = PreparedPaintBounds.StrokeOutset(source, bounds);
                var boxOnly = new RectD(bounds.X - stroke, bounds.Y - stroke, side + 2 * stroke, side + 2 * stroke);
                var query = new RectD(2, bounds.Bottom + stroke, 1, .5);
                Conformance.Equal(0d, PreparedVisibility.Intersect(boxOnly, query).Height);
                Conformance.Equal(true, PreparedPaintBounds.PaintOutset(source, bounds) > stroke);
                Conformance.Equal(1, new PreparedPageIndex(page).Query(query).Count);
                using var full = SKSurface.Create(new SKImageInfo(384, 384));
                using var indexed = SKSurface.Create(new SKImageInfo(384, 384));
                using var linear = SKSurface.Create(new SKImageInfo(384, 384));
                using var renderer = new ReportRenderer();
                foreach (var surface in new[] { full, indexed, linear })
                { surface.Canvas.Clear(SKColors.Transparent); surface.Canvas.Scale(64); surface.Canvas.ClipRect(ReportRenderer.Rect(query)); }
                renderer.DrawPage(full.Canvas, page);
                renderer.DrawIndexedPage(indexed.Canvas, new PreparedPageIndex(page), query);
                renderer.DrawPage(linear.Canvas, page, query);
                using var a = full.Snapshot(); using var b = indexed.Snapshot(); using var c = linear.Snapshot();
                using var aa = SKBitmap.FromImage(a); using var bb = SKBitmap.FromImage(b); using var cc = SKBitmap.FromImage(c);
                Conformance.Equal(true, aa.Pixels.Any(p => p.Alpha > 0 && p.Green < 230));
                Conformance.Equal(true, aa.Bytes.SequenceEqual(bb.Bytes) && aa.Bytes.SequenceEqual(cc.Bytes));
                result = new("checked-box miter outside rectangular stroke: " + side, true);
            }
            catch (Exception e) { result = new("checked-box miter outside rectangular stroke: " + side, false, e.ToString()); }
            yield return result;
        }
    }
}
