using ReportScene.Model;
using ReportScene.Rendering.Skia;
using SkiaSharp;

namespace ReportScene.Validation;

public static class GridMeshTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action body) { try { body(); return new("grid mesh: " + name, true); } catch (Exception e) { return new("grid mesh: " + name, false, e.ToString()); } }
        foreach (double zoom in new[] { .15, .5, .85, 1d, 4d })
            yield return Test("exact square-dot pixels at zoom " + zoom, () =>
            {
                foreach (byte alpha in new byte[] { 255, 127 })
                    foreach (float offset in new[] { 0f, .125f, .5f })
                        Compare(zoom, offset, new SKPaint { Color = new(123, 143, 163, alpha), StrokeWidth = (float)(72 / (zoom * 96)), IsAntialias = false }, zoom >= .5);
            });
        yield return Test("overlapping translucent squares retain exact point blending", () =>
        {
            foreach (float width in new[] { 4f, 5f, 8f })
                Compare(1, .125f, new SKPaint { Color = new(120, 50, 80, 127), StrokeWidth = width }, false);
        });
        yield return Test("unsupported round antialiased and hairline paints retain points", () =>
        {
            Compare(.85, .125f, new SKPaint { Color = SKColors.Red, StrokeWidth = 2, StrokeCap = SKStrokeCap.Round }, false);
            Compare(.85, .125f, new SKPaint { Color = SKColors.Red, StrokeWidth = 2, IsAntialias = true }, false);
            Compare(.85, .125f, new SKPaint { Color = SKColors.Red, StrokeWidth = 0 }, false);
        });
        yield return Test("rotated skewed mirrored and non-source-over paints retain points", () =>
        {
            Compare(.85, .125f, new SKPaint { Color = SKColors.Red, StrokeWidth = 2 }, false, c => c.RotateDegrees(5));
            Compare(.85, .125f, new SKPaint { Color = SKColors.Red, StrokeWidth = 2 }, false, c => c.Skew(.15f, .1f));
            Compare(.85, .125f, new SKPaint { Color = SKColors.Red, StrokeWidth = 2 }, false, c => { c.Translate(450, 0); c.Scale(-1, 1); });
            Compare(.85, .125f, new SKPaint { Color = SKColors.Red, StrokeWidth = 2, BlendMode = SKBlendMode.Multiply }, false);
        });
        yield return Test("crossing a coordinate precision boundary is rejected before submission", () =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(20, 20)); surface.Canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Red, StrokeWidth = 1 };
            var grid = new ReportGridPainter(); const double edge = 36028797018963968d; // 2^55: the ULP changes from 4 to 8.
            Conformance.Equal(true, edge - 16 + 4 > edge - 16); // The old first-addition guard accepted this range.
            foreach (var bounds in new[] { new RectD(edge - 16, 0, 32, 4), new RectD(0, edge - 16, 4, 32) })
            {
                Conformance.Throws<ArgumentException>(() => grid.Draw(surface.Canvas, bounds, bounds, 4, paint));
                Conformance.Equal(0, grid.RetainedPointCapacity); Conformance.Equal(0, grid.RetainedMeshBytes);
            }
            using var image = surface.Snapshot(); using var bitmap = SKBitmap.FromImage(image);
            Conformance.Equal(true, bitmap.Pixels.All(c => c == SKColors.White));
        });
        yield return Test("bounded reusable mesh tails cannot retain earlier dots", () =>
        {
            using var recorder = new SKPictureRecorder(); var c = recorder.BeginRecording(new SKRect(0, 0, 2000, 1800));
            using var paint = new SKPaint { Color = SKColors.Gray, StrokeWidth = 1 };
            var grid = new ReportGridPainter(); var bounds = new RectD(0, 0, 2000, 1800);
            Conformance.Equal(new GridPaintStatistics(225000, 55), grid.Draw(c, bounds, bounds, 4, paint));
            Conformance.Equal(true, grid.RetainedMeshBytes <= (2 * ReportGridPainter.BatchSize - 1) * 44);
            Conformance.Equal(true, grid.RetainedPointCapacity <= 2 * ReportGridPainter.BatchSize - 1);
            using var picture = recorder.EndRecording();
            using var a = SKSurface.Create(new SKImageInfo(200, 200)); using var b = SKSurface.Create(new SKImageInfo(200, 200));
            a.Canvas.Clear(SKColors.White); b.Canvas.Clear(SKColors.White); var small = new RectD(20, 20, 9, 13);
            var stats = grid.Draw(a.Canvas, small, bounds, 4, paint);
            Conformance.Equal(new GridPaintStatistics(12, 1), stats); Reference(b.Canvas, small, bounds, paint);
            Equal(a, b); grid.Clear(); Conformance.Equal(0, grid.RetainedMeshBytes); Conformance.Equal(0, grid.RetainedPointCapacity);
        });
        yield return Test("empty cancellation and budget rejection allocate no mesh", () =>
        {
            using var a = SKSurface.Create(new SKImageInfo(200, 200)); using var paint = new SKPaint { StrokeWidth = 1 };
            var grid = new ReportGridPainter(); var bounds = new RectD(0, 0, 200, 200);
            Conformance.Equal(default(GridPaintStatistics), grid.Draw(a.Canvas, bounds, new(0, 0, 0, 200), 4, paint));
            Conformance.Throws<ArgumentException>(() => grid.Draw(a.Canvas, bounds, bounds, 4, paint, 1));
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            Conformance.Throws<OperationCanceledException>(() => grid.Draw(a.Canvas, bounds, bounds, 4, paint, cancellationToken: cancel.Token));
            Conformance.Equal(0, grid.RetainedMeshBytes); Conformance.Equal(0, grid.RetainedPointCapacity);
        });
    }
    private static void Compare(double zoom, float offset, SKPaint paint, bool meshExpected, Action<SKCanvas>? transform = null)
    {
        using (paint)
        {
            double scale = zoom * 96 / 72;
            using var a = SKSurface.Create(new SKImageInfo(500, 350)); using var b = SKSurface.Create(new SKImageInfo(500, 350));
            foreach (var c in new[] { a.Canvas, b.Canvas })
            {
                c.Clear(SKColors.White); transform?.Invoke(c); c.Scale((float)scale); c.Translate(-20.25f + offset, -10.5f + offset);
                c.ClipRect(new SKRect(25, 15, (float)(480 / scale), (float)(330 / scale)));
            }
            var bounds = new RectD(32.125, 18.25, 1800, 1500); var viewport = new RectD(20, 10, 500 / scale, 350 / scale);
            var reference = Reference(a.Canvas, bounds, viewport, paint);
            var grid = new ReportGridPainter(); Conformance.Equal(reference, grid.Draw(b.Canvas, bounds, viewport, 4, paint));
            Conformance.Equal(meshExpected, grid.RetainedMeshBytes > 0); Equal(a, b);
        }
    }
    private static GridPaintStatistics Reference(SKCanvas canvas, RectD band, RectD viewport, SKPaint paint)
    {
        int count = 0;
        double firstX = band.X + Math.Max(0, Math.Ceiling((viewport.X - band.X) / 4)) * 4;
        double firstY = band.Y + Math.Max(0, Math.Ceiling((viewport.Y - band.Y) / 4)) * 4;
        for (double y = firstY; y < Math.Min(band.Bottom, viewport.Bottom); y += 4)
            for (double x = firstX; x < Math.Min(band.Right, viewport.Right); x += 4)
            { canvas.DrawPoint((float)x, (float)y, paint); count++; }
        return new(count, (count + ReportGridPainter.BatchSize - 1) / ReportGridPainter.BatchSize);
    }
    private static void Equal(SKSurface a, SKSurface b)
    {
        using var ia = a.Snapshot(); using var ib = b.Snapshot(); using var ba = SKBitmap.FromImage(ia); using var bb = SKBitmap.FromImage(ib);
        Conformance.Equal(true, ba.Pixels.Any(c => Math.Min(c.Red, Math.Min(c.Green, c.Blue)) < 240));
        Conformance.Equal(true, ba.Bytes.SequenceEqual(bb.Bytes));
    }
}
