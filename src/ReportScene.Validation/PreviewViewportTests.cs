using ReportScene.Model;
using ReportScene.Rendering.Skia;
using SkiaSharp;

namespace ReportScene.Validation;

public static class PreviewViewportTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("preview viewport: " + name, true); } catch (Exception e) { return new("preview viewport: " + name, false, e.ToString()); } }
        yield return Test("centered whole paper uses actual element geometry", () =>
        {
            const double scale = .6806114813099098;
            var visible = ReportPreviewViewport.Calculate(new(279, 16, 405, 573), 963, 613, scale);
            Conformance.Equal(new RectD(0, 0, 405 / scale, 573 / scale), visible);
            Conformance.Equal(true, visible.Bottom > 840);
        });
        yield return Test("scroll translation is inverted before point conversion", () =>
        {
            Conformance.Equal(new RectD(149, 399, 402, 302), ReportPreviewViewport.Calculate(new(-300, -800, 2000, 5000), 800, 600, 2));
        });
        yield return Test("empty and disjoint viewports contain no invented paper", () =>
        {
            Conformance.Equal(default(RectD), ReportPreviewViewport.Calculate(new(0, 0, 100, 100), 0, 100, 1));
            Conformance.Equal(default(RectD), ReportPreviewViewport.Calculate(new(1000, 1000, 100, 100), 800, 600, 1));
            Conformance.Equal(default(RectD), ReportPreviewViewport.Calculate(new(0, 0, 0, 100), 800, 600, 1));
        });
        yield return Test("invalid or unrepresentable geometry is rejected", () =>
        {
            Conformance.Throws<ArgumentException>(() => ReportPreviewViewport.Calculate(new(double.NaN, 0, 100, 100), 800, 600, 1));
            Conformance.Throws<ArgumentException>(() => ReportPreviewViewport.Calculate(new(0, 0, -1, 100), 800, 600, 1));
            Conformance.Throws<ArgumentException>(() => ReportPreviewViewport.Calculate(new(0, 0, 100, 100), -1, 600, 1));
            Conformance.Throws<ArgumentOutOfRangeException>(() => ReportPreviewViewport.Calculate(new(0, 0, 100, 100), 800, 600, 0));
            Conformance.Throws<ArgumentOutOfRangeException>(() => ReportPreviewViewport.Calculate(new(0, 0, 100, 100), 800, 600, double.Epsilon));
        });
        yield return Test("actual scrolling bounds retain indexed candidate reduction", () =>
        {
            var items = Enumerable.Range(0, 100000).Select(n => new PreparedItem(new TextItem { Id = n.ToString() }, new(0, n * 20, 100, 18), "Row " + n)).ToArray();
            var index = new PreparedPageIndex(new(1, 200, 2000000, items));
            var visible = ReportPreviewViewport.Calculate(new(0, -1000000, 200, 2000000), 200, 200, 1);
            var hits = index.Query(visible, out int examined);
            Conformance.Equal(string.Join(',', Enumerable.Range(50000, 11)), string.Join(',', hits.Select(i => i.Source.Id)));
            Conformance.Equal(true, examined < 64);
        });
        foreach (var (width, height, scale) in new[] { (635, 898, 1.0666666666666669), (405, 573, .6806114813099098) })
        {
            yield return Test($"large-cull recorder preserves right and lower page content at {width}x{height}", () =>
            {
                using var renderer = new ReportRenderer();
                var right = new ShapeItem { Bounds = new(570, 100, 20, 20), Style = new() { Fill = "#305EA0", BorderWidth = 0 } };
                var lower = new ShapeItem { Bounds = new(50, 817, 40, 16), Style = new() { Fill = "#305EA0", BorderWidth = 0 } };
                var page = new PreparedPage(1, 595.276, 841.89, [new(right, right.Bounds), new(lower, lower.Bounds)]);
                var index = new PreparedPageIndex(page);
                using var recorder = new SKPictureRecorder();
                // Same large recording bounds used by pinned Uno 6.7.135 Visual.PaintStep.
                // No large bitmap allocation: the recorder stores vector commands.
                const float safeEdge = 536870880;
                var canvas = recorder.BeginRecording(new(-safeEdge, -safeEdge, safeEdge, safeEdge));
                canvas.ClipRect(SKRect.Create(width, height), antialias: true); canvas.Scale((float)scale);
                var visible = ReportPreviewViewport.Calculate(new(0, 0, width, height), width, height, scale);
                renderer.DrawIndexedPage(canvas, index, visible);
                using var picture = recorder.EndRecording();
                using var actual = SKSurface.Create(new SKImageInfo(width, height));
                using var expected = SKSurface.Create(new SKImageInfo(width, height));
                actual.Canvas.Clear(SKColors.Transparent); actual.Canvas.DrawPicture(picture);
                expected.Canvas.Clear(SKColors.Transparent); expected.Canvas.ClipRect(SKRect.Create(width, height), antialias: true);
                expected.Canvas.Scale((float)scale); renderer.DrawPage(expected.Canvas, page);
                using var ai = actual.Snapshot(); using var ei = expected.Snapshot();
                using var a = SKBitmap.FromImage(ai); using var e = SKBitmap.FromImage(ei);
                Conformance.Equal(true, a.Bytes.SequenceEqual(e.Bytes));
                Conformance.Equal(new SKColor(48, 94, 160), a.GetPixel((int)(60 * scale), (int)(825 * scale)));
            });
        }
    }
}
