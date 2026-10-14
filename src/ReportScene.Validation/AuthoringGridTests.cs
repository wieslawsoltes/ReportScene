using ReportScene.Model;
using ReportScene.Rendering.Skia;
using SkiaSharp;

namespace ReportScene.Validation;

public static class AuthoringGridTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action body) { try { body(); return new("authoring grid: " + name, true); } catch (Exception e) { return new("authoring grid: " + name, false, e.ToString()); } }
        foreach (double zoom in new[] { .15, .5, .85, 1d, 4d })
        {
            double scale = zoom * 96 / 72;
            yield return Test("batched pixels equal individual points at zoom " + zoom, () =>
            {
                foreach (float alpha in new[] { 1f, .5f })
                {
                    using var a = SKSurface.Create(new SKImageInfo(700, 500)); using var b = SKSurface.Create(new SKImageInfo(700, 500));
                    a.Canvas.Clear(SKColors.White); b.Canvas.Clear(SKColors.White);
                    a.Canvas.Scale((float)scale); b.Canvas.Scale((float)scale);
                    a.Canvas.Translate(-20.25f, -10.5f); b.Canvas.Translate(-20.25f, -10.5f);
                    var viewport = new RectD(20, 10, 700 / scale, 500 / scale);
                    var band = new RectD(32.125, 18.25, 1800, 1500);
                    using var paint = new SKPaint { IsAntialias = false, StrokeWidth = (float)(1 / scale), Color = new SKColor(123, 143, 163, (byte)(alpha * 255)) };
                    int count = Reference(a.Canvas, band, viewport, 4, paint);
                    var painter = new ReportGridPainter(); var stats = painter.Draw(b.Canvas, band, viewport, 4, paint);
                    Conformance.Equal(count, stats.Points); Conformance.Equal((count + ReportGridPainter.BatchSize - 1) / ReportGridPainter.BatchSize, stats.Submissions);
                    using var ia = a.Snapshot(); using var ib = b.Snapshot(); using var ba = SKBitmap.FromImage(ia); using var bb = SKBitmap.FromImage(ib);
                    Conformance.Equal(true, ba.Pixels.Any(c => c.Red < 240));
                    Conformance.Equal(true, ba.Bytes.SequenceEqual(bb.Bytes));
                }
            });
        }
        yield return Test("large visible lattice uses bounded submissions and buffer ownership", () =>
        {
            using var recorder = new SKPictureRecorder(); var canvas = recorder.BeginRecording(new SKRect(0, 0, 2000, 1800));
            using var paint = new SKPaint { StrokeWidth = 1, Color = SKColors.Gray };
            var painter = new ReportGridPainter(); var r = new RectD(0, 0, 2000, 1800);
            var stats = painter.Draw(canvas, r, r, 4, paint);
            Conformance.Equal(225000, stats.Points); Conformance.Equal(55, stats.Submissions);
            Conformance.Equal(true, painter.RetainedPointCapacity <= ReportGridPainter.BatchSize * 2 - 1);
            using var picture = recorder.EndRecording();
            using var next = new SKPictureRecorder(); var nextCanvas = next.BeginRecording(new SKRect(0, 0, 2000, 1800));
            Conformance.Equal(stats, painter.Draw(nextCanvas, r, r, 4, paint));
            using var nextPicture = next.EndRecording();
            painter.Clear(); Conformance.Equal(0, painter.RetainedPointCapacity);
            Console.WriteLine($"GRID WORK: {stats.Points} reference native point calls -> {stats.Submissions} batched submissions; retained coordinates bounded to 8191 points.");
        });
        yield return Test("empty and disjoint viewports allocate no point buffers", () =>
        {
            using var recorder = new SKPictureRecorder(); var canvas = recorder.BeginRecording(new SKRect(0, 0, 200, 200)); using var paint = new SKPaint();
            var painter = new ReportGridPainter(); var band = new RectD(0, 0, 100, 100);
            foreach (var viewport in new[] { new RectD(150, 150, 10, 10), new RectD(0, 0, 0, 100), new RectD(0, 0, 100, 0) })
                Conformance.Equal(default(GridPaintStatistics), painter.Draw(canvas, band, viewport, 4, paint));
            Conformance.Equal(0, painter.RetainedPointCapacity);
        });
        yield return Test("tail reuse cannot replay points from an earlier larger batch", () =>
        {
            var painter = new ReportGridPainter();
            using var a = SKSurface.Create(new SKImageInfo(200, 200)); using var b = SKSurface.Create(new SKImageInfo(200, 200));
            using var paint = new SKPaint { Color = SKColors.Red, StrokeWidth = 1 };
            var large = new RectD(0, 0, 200, 200); _ = painter.Draw(b.Canvas, large, large, 4, paint);
            b.Canvas.Clear(SKColors.White); a.Canvas.Clear(SKColors.White);
            var tiny = new RectD(20, 20, 9, 13); Reference(a.Canvas, tiny, large, 4, paint); var stats = painter.Draw(b.Canvas, tiny, large, 4, paint);
            Conformance.Equal(12, stats.Points); Conformance.Equal(1, stats.Submissions);
            using var ia = a.Snapshot(); using var ib = b.Snapshot(); using var ba = SKBitmap.FromImage(ia); using var bb = SKBitmap.FromImage(ib);
            Conformance.Equal(true, ba.Bytes.SequenceEqual(bb.Bytes));
        });
        yield return Test("invalid geometry point budgets and cancellation are explicit", () =>
        {
            using var recorder = new SKPictureRecorder(); var canvas = recorder.BeginRecording(new SKRect(0, 0, 200, 200)); using var paint = new SKPaint();
            var painter = new ReportGridPainter(); var r = new RectD(0, 0, 200, 200);
            Conformance.Throws<ArgumentOutOfRangeException>(() => painter.Draw(canvas, r, r, 0, paint));
            Conformance.Throws<ArgumentException>(() => painter.Draw(canvas, r, new(double.NaN, 0, 1, 1), 4, paint));
            Conformance.Throws<ArgumentException>(() => painter.Draw(canvas, r, r, 4, paint, maximumPoints: 1));
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            Conformance.Throws<OperationCanceledException>(() => painter.Draw(canvas, r, r, 4, paint, cancellationToken: cancel.Token));
            Conformance.Equal(0, painter.RetainedPointCapacity);
        });
    }
    private static int Reference(SKCanvas canvas, RectD band, RectD view, double spacing, SKPaint paint)
    {
        int count = 0;
        double firstX = band.X + Math.Max(0, Math.Ceiling((view.X - band.X) / spacing)) * spacing;
        double firstY = band.Y + Math.Max(0, Math.Ceiling((view.Y - band.Y) / spacing)) * spacing;
        for (double y = firstY; y < Math.Min(band.Bottom, view.Bottom); y += spacing)
            for (double x = firstX; x < Math.Min(band.Right, view.Right); x += spacing)
            { canvas.DrawPoint((float)x, (float)y, paint); count++; }
        return count;
    }
}
