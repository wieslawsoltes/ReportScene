using ReportScene.Model;
using ReportScene.Rendering.Skia;
using SkiaSharp;

namespace ReportScene.UnoPackageTests;

public static class GridPainterConsumer
{
    public static GridPaintStatistics Draw(SKCanvas canvas, SKPaint paint)
    {
        var painter = new ReportGridPainter(); var area = new RectD(0, 0, 100, 100);
        canvas.Save(); _ = ReportViewportClip.Apply(canvas, area); canvas.Restore();
        _ = painter.RetainedMeshBytes;
        var result = painter.Draw(canvas, area, area, 4, paint); _ = painter.RetainedPointCapacity; painter.Clear(); return result;
    }
}
