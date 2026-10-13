using ReportScene.Controls.Uno;
using ReportScene.Export;
using ReportScene.Model;

namespace ReportScene.UnoPackageTests;

public static class PrintOptionsConsumer
{
    public static ReportPrintOptionsEditor Create()
    {
        var editor = new ReportPrintOptionsEditor("Reusable print draft", 4, 1) { Reverse = true };
        _ = editor.ReadSelection(); _ = editor.Elements; return editor;
    }
    public static ExportFile PrintDocument(PreparedReport report)
        => new ReportExporter().CreatePrintDocument(report, ReportPageSelection.Create(report.Pages.Count), reverse: true);
}
