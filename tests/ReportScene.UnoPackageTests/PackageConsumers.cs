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
    public static ReportDesignerControl CreateDesignerWithPreferences(IReportWorkspaceHost host, ReportDocument document, IDesignerPreferenceStore store) => new(host, document, store);
    public static DesignerOptionsEditor CreateOptionsEditor(DesignerPreferences settings)
    { var editor = new DesignerOptionsEditor(settings); editor.RestoreDefaults(); _ = editor.ReadValue(); return editor; }
    public static async Task InitializePreferences(ReportDesignerControl designer)
    { await designer.RestorePreferencesAsync(); _ = designer.CurrentPreferences; _ = await designer.FlushPreferencesAsync(); }
    public static ReportDesignView CreateDesignSurface(DesignerSession session, ReportRenderer renderer)
        => new(session, renderer) { Unit = DesignerUnit.Millimeters, ShowRulers = true, ShowFullPage = false };
    public static ReportPreviewView CreatePreviewSurface(ReportRenderer renderer) => new(renderer);
    public static void NavigatePreview(ReportPreviewView preview)
    { preview.Navigate(ReportPageCommand.Last); preview.FitPage(); _ = preview.Position; _ = preview.ViewportElement; }
    public static ReportPageNavigator CreatePageNavigator(Action<int> navigate)
    {
        var control = new ReportPageNavigator(); control.PageRequested += navigate;
        control.SetPosition(3, 1, resetEditor: true); _ = control.Position; _ = control.ValidationMessage; _ = control.Elements; return control;
    }
    public static ReportPreviewOutlineView CreatePreviewOutline(PreparedOutlineSnapshot snapshot, Action<PreparedBookmark> navigate)
    {
        var control = new ReportPreviewOutlineView(); control.BookmarkRequested += navigate;
        control.SetSnapshot(snapshot); control.FilterText = "Page"; control.SelectBookmark(snapshot.Entries.FirstOrDefault()); return control;
    }
    public static void OpenPreviewOutline(ReportPreviewView preview)
    {
        preview.OutlineWidth = 240; preview.OutlineVisible = true;
        if (preview.OutlineSnapshot?.Entries.FirstOrDefault() is { } bookmark) _ = preview.NavigateBookmark(bookmark);
    }
    public static MatrixDefinitionEditor CreateMatrixEditor(MatrixItem matrix, IEnumerable<DataTable> tables) => new(matrix, tables);
    public static SubreportDefinitionEditor CreateSubreportEditor(SubreportItem subreport, IEnumerable<ReportPage> pages, string owner) => new(subreport, pages, owner);
    public static DataTablePreview CreateDataPreview(DataTable table)
    { var preview = new DataTablePreview(); preview.SetTable(table); return preview; }
    public static ReportComboBox CreateCompactChoice() => new();
    public static PropertySheet CreatePropertySheet()
    {
        var sheet = new PropertySheet { FilterText = "Width", Alphabetical = true, CommitGuard = () => { } };
        using (sheet.DeferRefresh()) { sheet.Category("Position"); sheet.Number("Width", 72, _ => { }, "width"); }
        sheet.ExpandAll(); return sheet;
    }
    public static ReportOutlineView CreateOutline(ReportDocument document)
    { var control = new ReportOutlineView(); control.SetOutline(DesignerOutline.Report(document)); control.SelectKey("page:0"); return control; }
    public static ReportOutlineView CreateTabbedOutline(ReportDocument document, Action<DesignerOutlineEntry> navigate)
    {
        var control = new ReportOutlineView(); control.NavigationRequested += navigate;
        control.SetOutline(DesignerOutline.Data(document), "Data"); control.FilterText = "Data";
        control.SetOutline(DesignerOutline.Report(document), "Report"); control.SelectKey("page:0");
        control.ResetContexts(); return control;
    }
    public static PaneSplitter CreateSplitter(Action<double> resize)
    {
        var control = new PaneSplitter { Minimum = 180, Maximum = 640, Value = 280, Reverse = true };
        control.ValueChanged += resize; control.InteractionStarted += () => { }; control.InteractionCompleted += _ => { }; return control;
    }
}
