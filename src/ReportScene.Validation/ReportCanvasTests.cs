using ReportScene.Engine;
using ReportScene.Model;
using ReportScene.Rendering.Skia;
using ReportScene.Samples;
using SkiaSharp;

namespace ReportScene.Validation;

/// <summary>Semantic checks shared by console and real browser validation hosts.</summary>
public static class ReportCanvasTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action)
        { try { action(); return new("report canvas: " + name, true); } catch (Exception error) { return new("report canvas: " + name, false, error.ToString()); } }

        yield return Test("positive-area clips reject offscreen and degenerate bounds away from the origin", () =>
        {
            var clip = new SKRect(10, 20, 30, 40);
            Conformance.Equal(true, ReportViewportClip.HasPositiveArea(clip));
            foreach (var bounds in new[] {
                new SKRect(10, 20, 10, 20), new SKRect(10, 20, 30, 20), new SKRect(10, 20, 10, 40),
                new SKRect(30, 20, 10, 40), new SKRect(10, 40, 30, 20), SKRect.Empty })
            {
                Conformance.Equal(false, ReportViewportClip.HasPositiveArea(bounds));
                Conformance.Equal(SKRect.Empty, ReportViewportClip.Intersect(bounds, clip));
            }
            foreach (var bounds in new[] {
                new SKRect(10, 100, 30, 120), new SKRect(100, 20, 120, 40),
                new SKRect(30, 20, 50, 40), new SKRect(10, 40, 30, 60) })
            {
                Conformance.Equal(SKRect.Empty, ReportViewportClip.Intersect(bounds, clip));
                Conformance.Equal(SKRect.Empty, ReportViewportClip.Intersect(clip, bounds));
            }
            Conformance.Equal(new SKRect(20, 30, 30, 40), ReportViewportClip.Intersect(clip, new SKRect(20, 30, 50, 60)));
        });

        yield return Test("nested clips, affine transforms and translucent painter order", () =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(96, 96));
            surface.Canvas.Clear(SKColors.White);
            ReportCanvas canvas = new SkiaReportCanvas(surface.Canvas);
            using var paint = new SKPaint { Color = SKColors.Red, IsAntialias = false };
            canvas.Save();
            canvas.Translate(8, 5);
            canvas.Scale(2, .5f);
            canvas.ClipRect(new(0, 0, 16, 40));
            canvas.DrawRect(0, 0, 24, 40, paint);
            canvas.Save();
            canvas.ClipRect(new(4, 8, 12, 28));
            paint.Color = new(0, 0, 255, 128);
            canvas.DrawRect(0, 0, 50, 50, paint);
            canvas.Restore();
            canvas.Restore();
            paint.Color = SKColors.Green;
            canvas.DrawRect(70, 70, 10, 10, paint);
            using var image = surface.Snapshot();
            using var pixels = SKBitmap.FromImage(image);
            Conformance.Equal(SKColors.White, pixels.GetPixel(4, 4));
            Conformance.Equal(SKColors.Red, pixels.GetPixel(10, 6));
            Conformance.Equal(SKColors.White, pixels.GetPixel(42, 12));
            var overlap = pixels.GetPixel(20, 12);
            Conformance.Equal(true, overlap.Red is >= 126 and <= 128 && overlap.Blue is >= 127 and <= 129 && overlap.Green == 0 && overlap.Alpha == 255);
            Conformance.Equal(SKColors.Green, pixels.GetPixel(75, 75));
            Conformance.Equal(SKMatrix.Identity, canvas.TotalMatrix);
        });
        yield return Test("all prepared sample types draw through an independent command backend", () =>
        {
            using var renderer = new ReportRenderer();
            var probe = new CommandProbeCanvas();
            foreach (string name in SampleReports.Names)
            {
                var report = new ReportPreparer(renderer.Metrics).Prepare(SampleReports.Create(name));
                foreach (var page in report.Pages.Take(2))
                    renderer.DrawPage(probe, page);
            }
            Conformance.Equal(0, probe.Depth);
            Conformance.Equal(true, probe.Primitives > 100);
            Conformance.Equal(true, probe.TextRuns > 10);
        });
        yield return Test("authoring matrix and subreport representations use the same command backend", () =>
        {
            using var renderer = new ReportRenderer();
            var probe = new CommandProbeCanvas();
            var matrix = new MatrixItem { Bounds = new(0, 0, 400, 180) };
            var subreport = new SubreportItem { Bounds = new(10, 190, 200, 50), PageName = "Detail" };
            renderer.DrawItem(probe, new(matrix, matrix.Bounds), designer: true);
            renderer.DrawItem(probe, new(subreport, subreport.Bounds), designer: true);
            Conformance.Equal(0, probe.Depth);
            Conformance.Equal(true, probe.TextRuns > 1);
            Conformance.Equal(true, probe.Primitives > 1);
        });
        yield return Test("backend text failure restores nested chart, item and page states", () =>
        {
            using var renderer = new ReportRenderer();
            var probe = new CommandProbeCanvas { FailOnText = true };
            var chart = new ChartItem { Title = "Failure inside chart title", Bounds = new(0, 0, 180, 120) };
            var page = new PreparedPage(1, 200, 150, [new(chart, chart.Bounds, Points: [new("A", 10), new("B", 20)])]);
            Conformance.Throws<ReportRenderException>(() => renderer.DrawPage(probe, page));
            Conformance.Equal(0, probe.Depth);
            probe.FailOnText = false;
            renderer.DrawPage(probe, page);
            Conformance.Equal(0, probe.Depth);
            Conformance.Equal(true, probe.TextRuns > 0);
        });
        yield return Test("direct text failure restores the caller canvas state", () =>
        {
            using var renderer = new ReportRenderer();
            var probe = new CommandProbeCanvas { FailOnText = true };
            probe.Save();
            Conformance.Throws<InvalidOperationException>(() => renderer.DrawText(probe, "Fault", new(0, 0, 80, 30), new()));
            Conformance.Equal(1, probe.Depth);
            probe.Restore();
        });
        yield return Test("shaped glyph advances match pagination metrics", () =>
        {
            using var metrics = new SkiaTextMeasurer();
            var style = new ItemStyle { FontSize = 17, Bold = true };
            const string text = "Zażółć · office · a\u0301";
            var shaped = metrics.ShapeText(text, style);
            Conformance.Equal(true, shaped.Codepoints.Length > 0);
            Conformance.Equal(shaped.Codepoints.Length, shaped.Points.Length);
            Conformance.Equal(shaped.Codepoints.Length, shaped.Clusters.Length);
            Conformance.Equal(metrics.MeasureWidth(text, style), (double)shaped.Width);
            Conformance.Equal(0, metrics.ShapeText("", style).Codepoints.Length);
        });
        yield return Test("procedural-grid seam retains bounds, budgets and cancellation", () =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(100, 100));
            ReportCanvas canvas = new SkiaReportCanvas(surface.Canvas);
            using var paint = new SKPaint { Color = SKColors.Black, StrokeWidth = 1 };
            var bounds = new RectD(0, 0, 100, 100);
            var viewport = new RectD(10, 10, 20, 20);
            Conformance.Equal(25, new ReportGridPainter().Draw(canvas, bounds, viewport, 4, paint).Points);
            Conformance.Throws<ArgumentException>(() => canvas.DrawGrid(bounds, viewport, 4, paint, maximumPoints: 4));
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            Conformance.Throws<OperationCanceledException>(() => canvas.DrawGrid(bounds, viewport, 4, paint, cancellationToken: canceled.Token));
        });
    }

    /// <summary>Consumes report commands without requiring or wrapping a native SKCanvas.</summary>
    private sealed class CommandProbeCanvas : ReportCanvas
    {
        public int Depth { get; private set; }
        public int Primitives { get; private set; }
        public int TextRuns { get; private set; }
        public bool FailOnText { get; set; }
        public override SKRect LocalClipBounds => new(0, 0, 10000, 10000);
        public override SKMatrix TotalMatrix => SKMatrix.Identity;
        public override void Save() => Depth++;
        public override void Restore()
        {
            if (Depth <= 0) throw new InvalidOperationException("Unbalanced canvas restore.");
            Depth--;
        }
        public override void Translate(float x, float y) { }
        public override void Scale(float x, float y) { }
        public override void ClipRect(SKRect rect, SKClipOperation operation = SKClipOperation.Intersect, bool antialias = false) { }
        public override void DrawRect(SKRect rect, SKPaint paint) => Primitives++;
        public override void DrawRoundRect(SKRect rect, float radiusX, float radiusY, SKPaint paint) => Primitives++;
        public override void DrawOval(SKRect rect, SKPaint paint) => Primitives++;
        public override void DrawLine(float x1, float y1, float x2, float y2, SKPaint paint) => Primitives++;
        public override void DrawPath(SKPath path, SKPaint paint) => Primitives++;
        public override void DrawArc(SKRect oval, float startAngle, float sweepAngle, bool useCenter, SKPaint paint) => Primitives++;
        public override void DrawImage(SKImage image, SKRect destination, SKSamplingOptions sampling) => Primitives++;
        public override void DrawText(SkiaTextMeasurer metrics, string text, float x, float baseline, ItemStyle style, SKPaint paint)
        {
            if (FailOnText) throw new InvalidOperationException("Injected backend text failure.");
            TextRuns++;
        }
        public override GridPaintStatistics DrawGrid(RectD bounds, RectD viewport, double spacing, SKPaint paint,
            int maximumPoints = 1_048_576, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The report renderer does not emit authoring grids.");
    }
}
