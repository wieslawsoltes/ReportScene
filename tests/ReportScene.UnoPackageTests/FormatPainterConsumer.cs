using ReportScene.Controls.Uno;
using ReportScene.Designer;
using ReportScene.Model;
using ReportScene.Rendering.Skia;

namespace ReportScene.UnoPackageTests;

public static class FormatPainterConsumer
{
    public static ReportDesignView Create(ReportDocument document, ReportRenderer renderer)
    {
        var repeatButton = new ReportRepeatToggleButton();
        repeatButton.RepeatRequested += () => { _ = repeatButton.IsChecked; };
        var view = new ReportDesignView(new DesignerSession(document), renderer);
        _ = view.FormatPainter.IsActive; _ = view.FormatPainter.IsRepeating;
        view.FormatPainter.StateChanged += () => { _ = view.FormatPainter.SourceName; };
        return view; // The owning host disposes the view, including its format painter.
    }
}
