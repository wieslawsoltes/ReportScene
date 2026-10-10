using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ReportScene.Controls.Uno;
using ReportScene.Model;
using ReportScene.Samples;

namespace ReportScene.Designer.Uno;

public sealed partial class ReportDesignerControl
{
    private static IEnumerable<string> AllSampleNames => SampleReports.Names.Concat(["Matrix sales"]);
    private async Task LoadAnySampleAsync(string name)
    {
        if (name == "Matrix sales") { if (await DiscardAsync()) Load(MatrixSampleReports.Sales()); }
        else await LoadSampleAsync(name);
    }
    private async Task EditAnyObjectAsync(ReportItem item)
    {
        if (item is not MatrixItem matrix) { await EditObjectAsync(item); return; }
        Session.Select(item.Id);
        if (item.Locked) throw new InvalidOperationException("The selected object is locked.");
        var editor = new MatrixDefinitionEditor(matrix, Session.Document.Data);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = ControlTheme.Brush("#AD362C"), FontSize = 12 };
        var content = new StackPanel { Spacing = 8, Width = Math.Clamp(ActualWidth - 120, 300, 820) };
        content.Children.Add(new ScrollViewer { Content = editor, MaxHeight = Math.Clamp(ActualHeight - 240, 190, 600), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto });
        content.Children.Add(error);
        var dialog = new ContentDialog { Title = "Matrix — " + matrix.Name, Content = content, XamlRoot = XamlRoot, PrimaryButtonText = "Apply", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        dialog.Resources["ContentDialogMaxWidth"] = 960d;
        var registered = new HashSet<string>(StringComparer.Ordinal);
        void RegisterEditors()
        {
            foreach (var key in registered) commands.Remove(key);
            registered.Clear();
            void Register(string id, FrameworkElement element) { commands[id] = element; registered.Add(id); }
            foreach (var entry in editor.Editors) Register(entry.Key, entry.Value);
            // Read-only diagnostic bounds for actual template buttons. Tests still send real input.
            var pending = new Stack<DependencyObject>(); pending.Push(dialog);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (node is Button button)
                {
                    if (button.Name == "PrimaryButton" || button.Content is string text && text == "Apply") Register("matrix-dialog-apply", button);
                    if (button.Name == "CloseButton" || button.Content is string label && label == "Cancel") Register("matrix-dialog-cancel", button);
                }
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) pending.Push(VisualTreeHelper.GetChild(node, i));
            }
            NotifyState();
        }
        dialog.Opened += (_, _) => RegisterEditors();
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                var updated = editor.GetDefinition();
                Session.UpdateSelection("Edit matrix", i => i.Id == matrix.Id ? updated : i);
            }
            catch (Exception e) { error.Text = e.Message; args.Cancel = true; }
        };
        RegisterEditors(); editor.EditorsChanged += RegisterEditors;
        try { await dialog.ShowAsync(); }
        finally
        {
            editor.EditorsChanged -= RegisterEditors;
            foreach (var key in registered) commands.Remove(key);
            UpdateStatus(); NotifyState();
        }
    }
    private Task EditSelectedMatrixAsync()
    {
        var item = Session.Selection.OfType<MatrixItem>().FirstOrDefault();
        return item is null ? Task.CompletedTask : EditAnyObjectAsync(item);
    }
    private MatrixItem CreateMatrix(string source, DataTable? table, double top)
    {
        if (table is null || table.Columns.Count == 0) throw new InvalidOperationException("Bind a data table before inserting a matrix.");
        string Field(string column) => "[" + source + "." + column + "]";
        var numeric = table.Columns.FirstOrDefault(c => table.Rows.Any(r => r.TryGetValue(c, out var v) && v.Kind == ValueKind.Number));
        var categories = table.Columns.Where(c => c != numeric).Take(2).ToArray();
        double width = Math.Min(480, Session.Page.ColumnWidth - 8);
        if (width < 100) throw new InvalidOperationException("The page is too narrow for a matrix. Increase the printable width.");
        return new()
        {
            Name = "Matrix", DataSource = source, Bounds = new(8, top, width, 144),
            RowHeaderWidth = Math.Min(120, width * .4), CellWidth = Math.Min(90, width * .3),
            RowDimensions = categories.Length > 0 ? [new() { Expression = Field(categories[0]), Title = categories[0] }] : [],
            ColumnDimensions = categories.Length > 1 ? [new() { Expression = Field(categories[1]), Title = categories[1] }] : [],
            Measures = [new() { Expression = numeric is null ? "" : Field(numeric), Title = numeric ?? "Records", Aggregate = numeric is null ? MatrixAggregate.CountRows : MatrixAggregate.Sum }]
        };
    }
    private void RefreshObjectProperties()
    {
        RefreshProperties();
        if (Session.Selection.FirstOrDefault() is not MatrixItem matrix) return;
        void Edit(string name, Func<MatrixItem, MatrixItem> change)
            => Session.UpdateSelection(name, i => i is MatrixItem m ? change(m) : i);
        properties.Category("Matrix data");
        properties.Choice("Data source", Session.Document.Data.Select(t => t.Name), matrix.DataSource, value => Edit("Matrix source", m => m with { DataSource = value }), "matrix-source");
        properties.Text("Filter", matrix.Filter ?? "", value => Edit("Matrix filter", m => m with { Filter = EmptyNull(value) }), "matrix-filter");
        properties.ReadOnly("Rows / columns", $"{matrix.RowDimensions.Count} / {matrix.ColumnDimensions.Count} · double-click to edit");
        properties.ReadOnly("Measures", matrix.Measures.Count.ToString());
        properties.Category("Matrix layout");
        properties.Number("Row header width", matrix.RowHeaderWidth, value => Edit("Matrix row width", m => m with { RowHeaderWidth = Positive(value, "Row header width") }), "matrix-row-width");
        properties.Number("Value column width", matrix.CellWidth, value => Edit("Matrix cell width", m => m with { CellWidth = Positive(value, "Value column width") }), "matrix-cell-width");
        properties.Number("Minimum row height", matrix.RowHeight, value => Edit("Matrix row height", m => m with { RowHeight = Positive(value, "Row height") }), "matrix-row-height");
        properties.Boolean("Row grand total", matrix.ShowRowGrandTotal, value => Edit("Matrix row total", m => m with { ShowRowGrandTotal = value }), "matrix-row-total");
        properties.Boolean("Column grand total", matrix.ShowColumnGrandTotal, value => Edit("Matrix column total", m => m with { ShowColumnGrandTotal = value }), "matrix-column-total");
        properties.Boolean("Repeat headers", matrix.RepeatHeaders, value => Edit("Matrix headers", m => m with { RepeatHeaders = value }), "matrix-repeat");
    }
    private Task ShowCurrentCompatibilityAsync() => DialogAsync("ReportScene compatibility", new TextBlock
    {
        TextWrapping = TextWrapping.Wrap,
        Text = "Implemented: band reports, groups and aggregates, master/detail, parameters, tables, charts, six barcode types, images, shapes and matrix/cross-tab reports. Matrices support typed hierarchical axes, subtotals, grand totals, multiple measures, percentages, source filters, group/master-detail scoping, horizontal page tiling and repeated column headers. The designer uses reusable Uno controls and the shared Skia rendering pipeline.\n\nThis is not full FastReport .NET parity. Remaining work includes general subreports, rich-text/HTML report objects, script/event execution, report inheritance, dialog pages, native FPX, the full data-provider and export-format catalog, advanced matrix custom cells/events and complete original designer visual and behavioral parity. FRX import/export only accepts the documented supported subset and reports unsupported features."
    });
}
