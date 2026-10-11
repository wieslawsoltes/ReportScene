using ReportScene.Controls.Uno;
using ReportScene.Designer;
using ReportScene.Designer.Uno;
using ReportScene.Model;
using ReportScene.Rendering.Skia;

namespace ReportScene.UnoPackageTests;

/// <summary>Compiles against generated nupkgs only. Interactive execution is separately tested by the Uno browser app.</summary>
public static class PackageConsumers
{
    public static ReportDesignerControl CreateDesigner(IReportWorkspaceHost host, ReportDocument document) => new(host, document);
    public static ReportDesignView CreateDesignSurface(DesignerSession session, ReportRenderer renderer) => new(session, renderer);
    public static ReportPreviewView CreatePreviewSurface(ReportRenderer renderer) => new(renderer);
    public static MatrixDefinitionEditor CreateMatrixEditor(MatrixItem matrix, IEnumerable<DataTable> tables) => new(matrix, tables);
    public static SubreportDefinitionEditor CreateSubreportEditor(SubreportItem subreport, IEnumerable<ReportPage> pages, string owner) => new(subreport, pages, owner);
    public static DataTablePreview CreateDataPreview(DataTable table)
    { var preview = new DataTablePreview(); preview.SetTable(table); return preview; }
    public static PropertySheet CreatePropertySheet() => new();
}
