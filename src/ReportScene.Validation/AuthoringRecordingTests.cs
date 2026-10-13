using ReportScene.Model;
using ReportScene.Rendering.Skia;
using SkiaSharp;

namespace ReportScene.Validation;

public static class AuthoringRecordingTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("authoring recording: " + name, true); } catch (Exception e) { return new("authoring recording: " + name, false, e.ToString()); } }
        foreach (double zoom in new[] { .15, .5, .85, 1d, 4d })
        {
            yield return Test("native viewport clip preserves full raster at zoom " + zoom, () =>
            {
                const int width = 480, height = 320;
                double scale = zoom * 96 / 72;
                var paper = new RectD(-800.25, -1100.5, 20000 * scale, 20000 * scale);
                var viewport = ReportPreviewViewport.Calculate(paper, width, height, scale);
                using var expected = SKSurface.Create(new SKImageInfo(width, height));
                using var actual = SKSurface.Create(new SKImageInfo(width, height));
                expected.Canvas.Clear(SKColors.White); actual.Canvas.Clear(SKColors.White);
                using var recorder = new SKPictureRecorder();
                const float safeEdge = 536870880;
                var recorded = recorder.BeginRecording(new(-safeEdge, -safeEdge, safeEdge, safeEdge));
                void Transform(SKCanvas c) { c.Translate((float)paper.X, (float)paper.Y); c.Scale((float)scale); }
                Transform(recorded); Transform(expected.Canvas);
                var clip = ReportViewportClip.Apply(recorded, viewport);
                Conformance.Equal(true, clip.Left <= viewport.X && clip.Top <= viewport.Y && clip.Right >= viewport.Right && clip.Bottom >= viewport.Bottom);
                Conformance.Equal(true, recorded.QuickReject(new SKRect(-10000, -10000, -9000, -9000)));
                void Paint(SKCanvas c)
                {
                    using var paint = new SKPaint { Color = SKColors.LightGray, IsAntialias = false, StrokeWidth = (float)(1 / scale) };
                    c.DrawRect(new SKRect(-10000, -10000, 20000, 20000), paint);
                    paint.Color = new SKColor(90, 110, 130, 128);
                    var grid = new ReportGridPainter(); _ = grid.Draw(c, new(32.125, 18.25, 19000, 19000), viewport, 4, paint);
                    // Nominal bounds outside the viewport, but a thick stroke crosses it.
                    paint.Color = SKColors.Red; paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = (float)(20 / scale);
                    c.DrawRect((float)(viewport.X - 9 / scale), (float)(viewport.Y + 20 / scale),
                        (float)(4 / scale), (float)(100 / scale), paint);
                    paint.Style = SKPaintStyle.Fill; paint.Color = new SKColor(16, 136, 64);
                    c.DrawRect((float)(viewport.Right - 20 / scale), (float)(viewport.Bottom - 20 / scale), (float)(18 / scale), (float)(18 / scale), paint);
                }
                Paint(recorded); Paint(expected.Canvas);
                using var picture = recorder.EndRecording(); actual.Canvas.DrawPicture(picture);
                using var ai = actual.Snapshot(); using var ei = expected.Snapshot();
                using var a = SKBitmap.FromImage(ai); using var e = SKBitmap.FromImage(ei);
                Conformance.Equal(true, a.Bytes.SequenceEqual(e.Bytes));
                Conformance.Equal(new SKColor(16, 136, 64), a.GetPixel(width - 8, height - 8));
                Conformance.Equal(SKColors.Red, a.GetPixel(1, 50));
            });
        }
        yield return Test("clip conversion rounds fractional edges outward", () =>
        {
            foreach (var r in new[] { new RectD(.1, -.3, .2, .7), new RectD(123456.789, -34567.891, .001, .002), new RectD(0, 0, 100, 100) })
            {
                var c = ReportViewportClip.Bounds(r);
                Conformance.Equal(true, c.Left <= r.X && c.Top <= r.Y && c.Right >= r.Right && c.Bottom >= r.Bottom);
                Conformance.Equal(true, double.IsFinite(c.Left) && double.IsFinite(c.Right));
            }
        });
        yield return Test("empty clips reject work and Save Restore owns their lifetime", () =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(100, 100));
            var c = surface.Canvas; c.Save();
            _ = ReportViewportClip.Apply(c, new(40, 40, 0, 10));
            Conformance.Equal(true, c.IsClipEmpty); c.Restore(); Conformance.Equal(false, c.IsClipEmpty);
            c.Save(); _ = ReportViewportClip.Apply(c, new(10, 10, 20, 20));
            Conformance.Equal(true, c.QuickReject(new SKRect(50, 50, 80, 80))); c.Restore();
            Conformance.Equal(false, c.QuickReject(new SKRect(50, 50, 80, 80)));
        });
        yield return Test("invalid clips are rejected before changing the canvas", () =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(100, 100));
            foreach (var r in new[] { new RectD(double.NaN, 0, 0, 0), new RectD(0, 0, -1, 1), new RectD(0, 0, double.PositiveInfinity, 1), new RectD(double.MaxValue, 0, 1, 1) })
                Conformance.Throws<ArgumentException>(() => ReportViewportClip.Apply(surface.Canvas, r));
            Conformance.Equal(false, surface.Canvas.IsClipEmpty);
        });
    }
}
