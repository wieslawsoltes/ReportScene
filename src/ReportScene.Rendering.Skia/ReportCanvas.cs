using ReportScene.Model;
using SkiaSharp;

namespace ReportScene.Rendering.Skia;

/// <summary>
/// Immediate report drawing contract shared by interactive GPU rendering and
/// offline document exporters. Coordinates, paints and paths are command data;
/// implementations consume or copy them during each call because callers retain
/// ownership and may immediately mutate or dispose them.
/// </summary>
/// <remarks>
/// Operations preserve painter order. Saved state includes transform and clip.
/// Text uses the supplied metrics owner so display and pagination agree on glyph
/// selection, positioning and advances. The canvas does not own these resources.
/// </remarks>
public abstract class ReportCanvas
{
    public abstract SKRect LocalClipBounds { get; }
    public abstract SKMatrix TotalMatrix { get; }
    public abstract void Save();
    public abstract void Restore();
    public abstract void Translate(float x, float y);
    public abstract void Scale(float x, float y);
    public void Scale(float scale) => Scale(scale, scale);
    public abstract void ClipRect(SKRect rect, SKClipOperation operation = SKClipOperation.Intersect, bool antialias = false);
    public abstract void DrawRect(SKRect rect, SKPaint paint);
    public void DrawRect(float x, float y, float width, float height, SKPaint paint) => DrawRect(SKRect.Create(x, y, width, height), paint);

    /// <summary>Draws a rectangular dashed outline with a continuous phase around its perimeter.</summary>
    public virtual void DrawDashedRect(SKRect rect, SKPaint paint, float dash = 2, float gap = 2)
    {
        ArgumentNullException.ThrowIfNull(paint);
        if (!float.IsFinite(dash) || dash <= 0) throw new ArgumentOutOfRangeException(nameof(dash));
        if (!float.IsFinite(gap) || gap <= 0) throw new ArgumentOutOfRangeException(nameof(gap));
        if (!ReportViewportClip.HasPositiveArea(rect)) return;
        var clip = LocalClipBounds;
        if (!ReportViewportClip.HasPositiveArea(clip)) return;
        var matrix = TotalMatrix;
        double scale = Math.Max(.00001, Math.Min(Math.Abs(matrix.ScaleX), Math.Abs(matrix.ScaleY)));
        float outset = (float)(Math.Max(0, paint.StrokeWidth) * .5 + 2 / scale);
        clip.Inflate(outset, outset);
        double phase = 0, period = (double)dash + gap;
        int segments = 0;
        void Edge(float x, float y, float dx, float dy, double length)
        {
            double begin, end;
            if (dx != 0)
            {
                if (y < clip.Top || y > clip.Bottom) { phase += length; return; }
                begin = Math.Max(0, dx > 0 ? clip.Left - x : x - clip.Right);
                end = Math.Min(length, dx > 0 ? clip.Right - x : x - clip.Left);
            }
            else
            {
                if (x < clip.Left || x > clip.Right) { phase += length; return; }
                begin = Math.Max(0, dy > 0 ? clip.Top - y : y - clip.Bottom);
                end = Math.Min(length, dy > 0 ? clip.Bottom - y : y - clip.Top);
            }
            if (begin < end)
            {
                double start = Math.Floor((phase + begin) / period) * period - phase;
                while (start < end)
                {
                    double a = Math.Max(0, start), b = Math.Min(length, start + dash);
                    if (b > a && b >= begin)
                    {
                        if (++segments > 1_048_576) throw new InvalidOperationException("Visible dashed outline exceeds the segment budget.");
                        DrawLine((float)(x + dx * a), (float)(y + dy * a), (float)(x + dx * b), (float)(y + dy * b), paint);
                    }
                    if (start + period <= start) throw new ArgumentException("Dash spacing cannot advance within these coordinates.");
                    start += period;
                }
            }
            phase += length;
        }
        Edge(rect.Left, rect.Top, 1, 0, rect.Width);
        Edge(rect.Right, rect.Top, 0, 1, rect.Height);
        Edge(rect.Right, rect.Bottom, -1, 0, rect.Width);
        Edge(rect.Left, rect.Bottom, 0, -1, rect.Height);
    }

    public abstract void DrawRoundRect(SKRect rect, float radiusX, float radiusY, SKPaint paint);
    public abstract void DrawOval(SKRect rect, SKPaint paint);
    public void DrawOval(float centerX, float centerY, float radiusX, float radiusY, SKPaint paint) =>
        DrawOval(new(centerX - radiusX, centerY - radiusY, centerX + radiusX, centerY + radiusY), paint);
    public virtual void DrawCircle(float centerX, float centerY, float radius, SKPaint paint) => DrawOval(centerX, centerY, radius, radius, paint);
    public abstract void DrawLine(float x1, float y1, float x2, float y2, SKPaint paint);
    public abstract void DrawPath(SKPath path, SKPaint paint);
    public abstract void DrawArc(SKRect oval, float startAngle, float sweepAngle, bool useCenter, SKPaint paint);
    public abstract void DrawImage(SKImage image, SKRect destination, SKSamplingOptions sampling);
    public abstract void DrawText(SkiaTextMeasurer metrics, string text, float x, float baseline, ItemStyle style, SKPaint paint);

    /// <summary>
    /// Draws the bounded row-major lattice used by designer bands. Backends may
    /// submit the lattice procedurally. Point count is the logical lattice size;
    /// submission count describes the actual drawing work.
    /// </summary>
    public abstract GridPaintStatistics DrawGrid(RectD bounds, RectD viewport, double spacing, SKPaint paint,
        int maximumPoints = 1_048_576, CancellationToken cancellationToken = default);
}

/// <summary>
/// Non-owning adapter for existing Skia exports and callers. The supplied canvas
/// and its lifetime remain the caller's responsibility.
/// </summary>
public sealed class SkiaReportCanvas : ReportCanvas
{
    private readonly ReportGridPainter gridPainter = new();
    public SKCanvas Canvas { get; }
    public SkiaReportCanvas(SKCanvas canvas) { ArgumentNullException.ThrowIfNull(canvas); Canvas = canvas; }
    public override SKRect LocalClipBounds => Canvas.LocalClipBounds;
    public override SKMatrix TotalMatrix => Canvas.TotalMatrix;
    public override void Save() => Canvas.Save();
    public override void Restore() => Canvas.Restore();
    public override void Translate(float x, float y) => Canvas.Translate(x, y);
    public override void Scale(float x, float y) => Canvas.Scale(x, y);
    public override void ClipRect(SKRect rect, SKClipOperation operation = SKClipOperation.Intersect, bool antialias = false) => Canvas.ClipRect(rect, operation, antialias);
    public override void DrawRect(SKRect rect, SKPaint paint) => Canvas.DrawRect(rect, paint);
    public override void DrawDashedRect(SKRect rect, SKPaint paint, float dash = 2, float gap = 2)
    {
        if (!float.IsFinite(dash) || dash <= 0) throw new ArgumentOutOfRangeException(nameof(dash));
        if (!float.IsFinite(gap) || gap <= 0) throw new ArgumentOutOfRangeException(nameof(gap));
        if (!ReportViewportClip.HasPositiveArea(rect)) return;
        using var stroke = paint.Clone();
        using var effect = SKPathEffect.CreateDash([dash, gap], 0);
        stroke.PathEffect = effect;
        stroke.Style = SKPaintStyle.Stroke;
        Canvas.DrawRect(rect, stroke);
    }
    public override void DrawRoundRect(SKRect rect, float radiusX, float radiusY, SKPaint paint) => Canvas.DrawRoundRect(rect, radiusX, radiusY, paint);
    public override void DrawOval(SKRect rect, SKPaint paint) => Canvas.DrawOval(rect, paint);
    public override void DrawCircle(float centerX, float centerY, float radius, SKPaint paint) => Canvas.DrawCircle(centerX, centerY, radius, paint);
    public override void DrawLine(float x1, float y1, float x2, float y2, SKPaint paint) => Canvas.DrawLine(x1, y1, x2, y2, paint);
    public override void DrawPath(SKPath path, SKPaint paint) => Canvas.DrawPath(path, paint);
    public override void DrawArc(SKRect oval, float startAngle, float sweepAngle, bool useCenter, SKPaint paint) => Canvas.DrawArc(oval, startAngle, sweepAngle, useCenter, paint);
    public override void DrawImage(SKImage image, SKRect destination, SKSamplingOptions sampling) => Canvas.DrawImage(image, destination, sampling);
    public override void DrawText(SkiaTextMeasurer metrics, string text, float x, float baseline, ItemStyle style, SKPaint paint) => metrics.Draw(Canvas, text, x, baseline, style, paint);
    public override GridPaintStatistics DrawGrid(RectD bounds, RectD viewport, double spacing, SKPaint paint,
        int maximumPoints = 1_048_576, CancellationToken cancellationToken = default) =>
        gridPainter.Draw(Canvas, bounds, viewport, spacing, paint, maximumPoints, cancellationToken);
}
