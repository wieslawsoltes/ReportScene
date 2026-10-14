using ReportScene.Model;
using SkiaSharp;

namespace ReportScene.Rendering.Skia;

public sealed partial class ReportGridPainter
{
    private SKPoint[]? meshPositions, meshTailPositions;
    private ushort[]? meshIndices, meshTailIndices;
    /// <summary>Retained managed mesh-array payload bytes, excluding headers and Skia-owned copies.</summary>
    public int RetainedMeshBytes => ((meshPositions?.Length ?? 0) + (meshTailPositions?.Length ?? 0)) * 8
        + ((meshIndices?.Length ?? 0) + (meshTailIndices?.Length ?? 0)) * 2;
    private void ClearMesh() { meshPositions = meshTailPositions = null; meshIndices = meshTailIndices = null; }

    // Preserve DrawPoints semantics for effects or transforms not covered by the
    // exact square-raster contract. No configuration or timer is needed.
    private static bool CanUseSquareMesh(SKCanvas canvas, SKPaint paint)
    {
        var m = canvas.TotalMatrix;
        return !paint.IsAntialias && paint.StrokeCap != SKStrokeCap.Round
            && float.IsFinite(paint.StrokeWidth) && paint.StrokeWidth > 0
            && paint.Shader is null && paint.PathEffect is null && paint.MaskFilter is null
            && paint.ImageFilter is null && paint.ColorFilter is null && paint.BlendMode == SKBlendMode.SrcOver
            && m.SkewX == 0 && m.SkewY == 0 && m.ScaleX > 0 && m.ScaleY > 0
            && m.Persp0 == 0 && m.Persp1 == 0 && m.Persp2 == 1;
    }
    private void Submit(SKCanvas canvas, SKPoint[] points, SKPaint paint, bool squareMesh)
    {
        if (!squareMesh) { canvas.DrawPoints(SKPointMode.Points, points, paint); return; }
        bool full = points.Length == BatchSize;
        ref var positions = ref (full ? ref meshPositions : ref meshTailPositions);
        ref var indices = ref (full ? ref meshIndices : ref meshTailIndices);
        int vertices = checked(points.Length * 4);
        if (positions?.Length != vertices)
        {
            positions = new SKPoint[vertices]; indices = new ushort[points.Length * 6];
            for (int n = 0; n < points.Length; n++)
            {
                int v = n * 4, i = n * 6;
                indices[i] = (ushort)v; indices[i + 1] = (ushort)(v + 1); indices[i + 2] = (ushort)(v + 2);
                indices[i + 3] = (ushort)v; indices[i + 4] = (ushort)(v + 2); indices[i + 5] = (ushort)(v + 3);
            }
        }
        float half = paint.StrokeWidth * .5f;
        for (int n = 0; n < points.Length; n++)
        {
            var p = points[n]; float left = p.X - half, right = p.X + half, top = p.Y - half, bottom = p.Y + half;
            if (!float.IsFinite(left) || !float.IsFinite(right) || !float.IsFinite(top) || !float.IsFinite(bottom))
            { canvas.DrawPoints(SKPointMode.Points, points, paint); return; }
            int i = n * 4;
            positions[i] = new(left, top); positions[i + 1] = new(right, top);
            positions[i + 2] = new(right, bottom); positions[i + 3] = new(left, bottom);
        }
        // Skia copies vertex data; no managed arrays, paints, canvas or document
        // are captured by the native recording. Dispose our owner after submission.
        using var mesh = SKVertices.CreateCopy(SKVertexMode.Triangles, positions, null, null, indices);
        canvas.DrawVertices(mesh, SKBlendMode.Modulate, paint);
    }
}
