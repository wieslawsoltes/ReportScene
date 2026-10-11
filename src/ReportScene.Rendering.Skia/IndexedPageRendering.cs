using ReportScene.Model;
using SkiaSharp;

namespace ReportScene.Rendering.Skia;

public static class IndexedPageRendering
{
    /// <summary>Draws only spatial candidates, in original painter order; returns examined leaf entries.</summary>
    public static int DrawIndexedPage(this ReportRenderer renderer, SKCanvas canvas, PreparedPageIndex index, RectD viewport, bool paper = true)
    {
        ArgumentNullException.ThrowIfNull(renderer); ArgumentNullException.ThrowIfNull(canvas); ArgumentNullException.ThrowIfNull(index);
        var page = index.Page; canvas.Save();
        try
        {
            canvas.ClipRect(SKRect.Create((float)page.Width, (float)page.Height)); canvas.ClipRect(ReportRenderer.Rect(viewport));
            if (paper) { using var white = new SKPaint { Color = SKColors.White }; canvas.DrawRect(SKRect.Create((float)page.Width, (float)page.Height), white); }
            var candidates = index.Query(viewport, out int examined);
            foreach (var item in candidates) renderer.DrawItem(canvas, item);
            return examined;
        }
        finally { canvas.Restore(); }
    }
}
