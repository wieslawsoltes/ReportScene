using ReportScene.Controls.Uno;
using ReportScene.Designer;

namespace ReportScene.UnoPackageTests;

public static class AppearanceConsumer
{
    public static ReportBorderWidthSelector Create(DesignerSession session)
    {
        var selector = new ReportBorderWidthSelector();
        selector.SetWidth(1.125); selector.SetWidth(null, mixed: true);
        selector.WidthRequested += session.SetSelectionBorderWidth;
        _ = selector.BorderWidth; _ = selector.Editor;
        return selector;
    }
}
