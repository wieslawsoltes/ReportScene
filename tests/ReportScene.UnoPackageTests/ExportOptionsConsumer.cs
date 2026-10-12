using ReportScene.Controls.Uno;
using ReportScene.Model;

namespace ReportScene.UnoPackageTests;

public static class ExportOptionsConsumer
{
    public static ReportPageSelectionEditor CreatePageSelector()
    {
        var editor = new ReportPageSelectionEditor(4, 1); editor.AllPagesOnly = true;
        ReportPageSelection pages = editor.ReadSelection(); _ = pages.IntervalCount; _ = editor.Elements;
        editor.AllPagesOnly = false; return editor;
    }
}
