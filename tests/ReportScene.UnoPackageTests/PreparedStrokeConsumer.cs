using ReportScene.Model;

namespace ReportScene.UnoPackageTests;

/// <summary>Compile the shared paint-extent API from the generated package feed, without source references.</summary>
public static class PreparedStrokeConsumer
{
    public static RectD? Bounds(PreparedPage page, PreparedItem item)
    { _ = PreparedPaintBounds.StrokeOutset(item.Source, item.Bounds); return PreparedPaintBounds.VisibleBounds(page, item); }
}
