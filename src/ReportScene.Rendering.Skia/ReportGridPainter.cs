using ReportScene.Model;
using SkiaSharp;

namespace ReportScene.Rendering.Skia;

public readonly record struct GridPaintStatistics(int Points, int Submissions);

/// <summary>
/// Draws the same row-major point lattice as individual DrawPoint calls in bounded
/// native batches, using square meshes for eligible paints. Own one instance per
/// rendering thread. No page, canvas or paint is retained. Point buffers retain at
/// most 8191 centers; optional mesh buffers have a separate byte bound.
/// </summary>
public sealed partial class ReportGridPainter
{
    public const int BatchSize = 4096;
    private SKPoint[]? batch;
    private SKPoint[]? tail;
    public int RetainedPointCapacity => (batch?.Length ?? 0) + (tail?.Length ?? 0);
    public void Clear() { batch = null; tail = null; ClearMesh(); }

    public GridPaintStatistics Draw(SKCanvas canvas, RectD bounds, RectD viewport, double spacing, SKPaint paint,
        int maximumPoints = 1_048_576, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(canvas); ArgumentNullException.ThrowIfNull(paint);
        return DrawCore(canvas, bounds, viewport, spacing, paint, maximumPoints, cancellationToken, CanUseSquareMesh(canvas, paint));
    }

    private GridPaintStatistics DrawCore(SKCanvas canvas, RectD bounds, RectD viewport, double spacing, SKPaint paint,
        int maximumPoints, CancellationToken cancellationToken, bool squareMesh)
    {
        ArgumentNullException.ThrowIfNull(canvas); ArgumentNullException.ThrowIfNull(paint);
        Validate(bounds); Validate(viewport);
        if (!double.IsFinite(spacing) || spacing < 4) throw new ArgumentOutOfRangeException(nameof(spacing));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPoints);
        cancellationToken.ThrowIfCancellationRequested();
        if (bounds.Width == 0 || bounds.Height == 0 || viewport.Width == 0 || viewport.Height == 0) return default;
        double firstX = bounds.X + Math.Max(0, Math.Ceiling((viewport.X - bounds.X) / spacing)) * spacing;
        double firstY = bounds.Y + Math.Max(0, Math.Ceiling((viewport.Y - bounds.Y) / spacing)) * spacing;
        double endX = Math.Min(bounds.Right, viewport.Right), endY = Math.Min(bounds.Bottom, viewport.Bottom);
        if (firstX >= endX || firstY >= endY) return default;
        if (!double.IsFinite(firstX) || !double.IsFinite(firstY) || firstX + spacing <= firstX || firstY + spacing <= firstY)
            throw new ArgumentException("The grid spacing cannot advance within these coordinates.");
        // An initially advancing addition can stop after crossing a binary64
        // exponent boundary. Reject an under-resolved lattice before allocating
        // or submitting anything, rather than repeating a coordinate to the budget.
        ValidateAdvance(firstX, endX, spacing); ValidateAdvance(firstY, endY, spacing);
        double columns = Math.Ceiling((endX - firstX) / spacing), rows = Math.Ceiling((endY - firstY) / spacing);
        if (columns * rows > maximumPoints) throw new ArgumentException("Visible grid exceeds the configured point budget.");
        batch ??= new SKPoint[BatchSize]; int count = 0, total = 0, calls = 0;
        for (double y = firstY; y < endY; y += spacing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (double x = firstX; x < endX; x += spacing)
            {
                if (++total > maximumPoints) throw new ArgumentException("Visible grid exceeds the configured point budget.");
                batch[count++] = new((float)x, (float)y);
                if (count != BatchSize) continue;
                cancellationToken.ThrowIfCancellationRequested();
                Submit(canvas, batch, paint, squareMesh); calls++; count = 0;
            }
        }
        if (count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (tail?.Length != count) tail = new SKPoint[count];
            Array.Copy(batch, tail, count);
            Submit(canvas, tail, paint, squareMesh); calls++;
        }
        return new(total, calls);
    }
    private static void ValidateAdvance(double first, double end, double spacing)
    {
        double magnitude = Math.Max(Math.Abs(first), Math.Abs(end));
        if (spacing < Math.BitIncrement(magnitude) - magnitude)
            throw new ArgumentException("The grid spacing is smaller than the coordinate precision over this viewport.");
    }
    private static void Validate(RectD r)
    {
        if (!double.IsFinite(r.X) || !double.IsFinite(r.Y) || !double.IsFinite(r.Width) || !double.IsFinite(r.Height)
            || !double.IsFinite(r.Right) || !double.IsFinite(r.Bottom) || r.Width < 0 || r.Height < 0
            || !float.IsFinite((float)r.X) || !float.IsFinite((float)r.Y) || !float.IsFinite((float)r.Right) || !float.IsFinite((float)r.Bottom))
            throw new ArgumentException("Grid geometry must be finite, nonnegative in size and representable by Skia.");
    }
}
