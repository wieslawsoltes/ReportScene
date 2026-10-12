using ReportScene.Model;
using ReportScene.Rendering.Skia;
using SkiaSharp;

namespace ReportScene.Validation;

public static class LinearStrokeTests
{
    public static IEnumerable<TestResult> Run()
    {
        foreach (var query in new[] { new RectD(26, 70, 3, 20), new RectD(171, 70, 3, 20), new RectD(70, 26, 20, 3), new RectD(70, 171, 20, 3) })
        {
            TestResult result;
            try
            {
                var source = new CheckBoxItem { Style = new() { Foreground = "#108840" } };
                var page = new PreparedPage(1, 200, 200, [new(source, new(30, 30, 140, 140), "False")]);
                using var renderer = new ReportRenderer(); using var full = SKSurface.Create(new SKImageInfo(200, 200)); using var linear = SKSurface.Create(new SKImageInfo(200, 200));
                full.Canvas.Clear(SKColors.Transparent); linear.Canvas.Clear(SKColors.Transparent);
                full.Canvas.ClipRect(ReportRenderer.Rect(query)); linear.Canvas.ClipRect(ReportRenderer.Rect(query));
                renderer.DrawPage(full.Canvas, page); renderer.DrawPage(linear.Canvas, page, query);
                using var a = full.Snapshot(); using var b = linear.Snapshot(); using var aa = SKBitmap.FromImage(a); using var bb = SKBitmap.FromImage(b);
                Conformance.Equal(true, aa.Pixels.Any(c => c.Alpha > 0 && c.Green < 230));
                Conformance.Equal(true, aa.Bytes.SequenceEqual(bb.Bytes));
                result = new("linear stroke viewport: " + query, true);
            }
            catch (Exception e) { result = new("linear stroke viewport: " + query, false, e.ToString()); }
            yield return result;
        }
    }
}
