using ReportScene.Controls.Uno;

namespace ReportScene.UnoPackageTests;

public static class ZoomSelectorConsumer
{
    public static ReportZoomSelector Create()
    {
        var selector = new ReportZoomSelector();
        selector.SetZoom(.6886291558279585); _ = selector.Zoom; _ = selector.Editor.Items.Count;
        selector.ZoomRequested += value => selector.SetZoom(value);
        return selector;
    }
}
