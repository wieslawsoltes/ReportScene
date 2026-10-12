using ReportScene.Controls.Uno;
using ReportScene.Designer;
using ReportScene.Model;

namespace ReportScene.UnoPackageTests;

/// <summary>Compile the public culling and surface-diagnostic APIs from isolated nupkgs.</summary>
public static class AuthoringViewportConsumer
{
    public static bool IsVisible(ReportDesignView view, DesignObject item)
    {
        _ = view.ViewportElement; _ = view.LastDrawnObjectCount;
        RectD viewport = view.LastPaintViewport;
        return DesignerPaintCulling.IsVisible(item, viewport, view.PointScale);
    }
}
