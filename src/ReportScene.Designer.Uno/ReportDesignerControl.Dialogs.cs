using System.Globalization;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ReportScene.Controls.Uno;
using ReportScene.Data;
using ReportScene.Model;
using ReportScene.Serialization;

namespace ReportScene.Designer.Uno;

public sealed partial class ReportDesignerControl
{
    private async Task<bool> DialogAsync(string heading, UIElement content, Action? apply = null)
    {
        var target = apply is null ? null : Operations.CaptureTarget();
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = ControlTheme.Brush("#AD362C"), FontSize = 12, Margin = new(0, 8, 0, 0) };
        var panel = new StackPanel { Spacing = 8, Width = Math.Clamp(ActualWidth - 120, 300, 760) };
        panel.Children.Add(new ScrollViewer { Content = content, MaxHeight = Math.Clamp(ActualHeight - 240, 190, 570), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto }); panel.Children.Add(error);
        var dialog = new ContentDialog { Title = heading, Content = panel, XamlRoot = XamlRoot, PrimaryButtonText = apply is null ? "" : "Apply", CloseButtonText = apply is null ? "Close" : "Cancel", DefaultButton = apply is null ? ContentDialogButton.Close : ContentDialogButton.Primary };
        dialog.Resources["ContentDialogMaxWidth"] = 900d;
        dialog.PrimaryButtonClick += (_, e) =>
        {
            try { if (target is not null) Operations.EnsureCurrent(target); apply?.Invoke(); }
            catch (Exception ex) { error.Text = ex.Message; e.Cancel = true; }
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
    private TextBox Editor(string value, double height = 310)
    {
        var editor = new TextBox { Text = value, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas"), FontSize = 12, Height = height, MinWidth = 290, Padding = new(8), MaxLength = ReportJson.MaximumCharacters };
        ScrollViewer.SetHorizontalScrollBarVisibility(editor, ScrollBarVisibility.Auto); ScrollViewer.SetVerticalScrollBarVisibility(editor, ScrollBarVisibility.Auto); commands["dialog-editor"] = ControlTheme.Identify(editor, "dialog-editor", "Document editor"); return editor;
    }
    private async Task ColorAsync(bool foreground)
    {
        var first = Session.Selection.FirstOrDefault(); if (first is null) return;
        var color = new TextBox { Text = foreground ? first.Style.Foreground : first.Style.Fill, Header = "#RRGGBB or #AARRGGBB", Width = 240 };
        var palette = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var value in new[] { "#000000", "#FFFFFF", "#244F75", "#C43E3E", "#2F7D55", "#D5A23A", "#E8EEF4", "#00000000" })
        { var button = new Button { Width = 30, Height = 30, MinWidth = 0, Padding = new(0), Background = ControlTheme.Brush(value), BorderBrush = ControlTheme.Brush("#909090"), BorderThickness = new(1) }; button.Click += (_, _) => color.Text = value; palette.Children.Add(button); }
        await DialogAsync(foreground ? "Text color" : "Fill color", Vertical(color, palette), () => { var value = ValidColor(color.Text); Session.UpdateSelection("Color", i => i with { Style = foreground ? i.Style with { Foreground = value } : i.Style with { Fill = value } }); });
    }
    private async Task EditObjectAsync(ReportItem item)
    {
        Session.Select(item.Id);
        if (item.Locked) throw new InvalidOperationException("The selected object is locked.");
        if (item is ImageItem)
        {
            await Operations.ReadAndApplyAsync(() => host.OpenFileAsync([".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp"]), file =>
            {
                if (file.Content.Length > 16_000_000) throw new FormatException("Embedded images are limited to 16 MB.");
                using var data = SkiaSharp.SKData.CreateCopy(file.Content); using var codec = SkiaSharp.SKCodec.Create(data) ?? throw new FormatException("Unsupported image format.");
                if ((long)codec.Info.Width * codec.Info.Height > 32_000_000) throw new FormatException("Decoded images are limited to 32 megapixels.");
                Session.UpdateSelection("Load image", i => i is ImageItem image ? image with { Data = file.Content } : i);
            }, selectionSensitive: true); return;
        }
        if (item is TextItem text)
        {
            var editor = Editor(text.Text, 250);
            var fields = new ComboBox { PlaceholderText = "Insert a field or expression…", MinWidth = 300, FontSize = 12 };
            foreach (var table in Session.Document.Data) foreach (var field in table.Columns) fields.Items.Add($"[{table.Name}.{field}]");
            foreach (var p in Session.Document.Parameters) fields.Items.Add($"[Parameters.{p.Name}]");
            foreach (var expression in new[] { "[PageN]", "[TotalPages]", "[Date]", "[Sum(Amount)]", "[Count()]", "[IIf(true, 'Yes', 'No')]" }) fields.Items.Add(expression);
            fields.SelectionChanged += (_, _) => { if (fields.SelectedItem is string value) { int position = editor.SelectionStart; editor.Text = editor.Text.Insert(position, value); editor.SelectionStart = position + value.Length; editor.Focus(FocusState.Programmatic); } };
            var format = new TextBox { Text = text.Format ?? "", Header = "Optional format (C2, N2, P1, yyyy-MM-dd for date functions)", FontSize = 12 };
            await DialogAsync("Text editor — " + item.Name, Vertical(fields, editor, format), () => Session.UpdateSelection("Edit text", i => i is TextItem t ? t with { Text = editor.Text, Format = EmptyNull(format.Text) } : i)); return;
        }
        if (item is TableItem tableItem)
        {
            var rows = new DataTable { Columns = ["Title", "Expression", "Width", "Format"], Rows = tableItem.Columns.Select(c => new DataRow { ["Title"] = c.Title, ["Expression"] = c.Expression, ["Width"] = (decimal)c.Width, ["Format"] = c.Format ?? "" }).ToList() };
            var editor = Editor(DataReaders.WriteCsv(rows, false));
            await DialogAsync("Table columns — edit CSV", Vertical(ControlTheme.Label("Columns: Title, Expression, Width (relative), Format", 12), editor), () =>
            {
                var parsed = DataReaders.Csv(editor.Text, options: new(InferNumbers: true));
                var columns = parsed.Rows.Select(r => new TableColumn(r["Title"].AsString(), r["Expression"].AsString(), Positive((double)r["Width"].AsNumber(), "Column width"), EmptyNull(r["Format"].AsString()))).ToList();
                if (columns.Count == 0) throw new FormatException("A table needs at least one column.");
                Session.UpdateSelection("Edit table columns", i => i is TableItem t ? t with { Columns = columns } : i);
            }); return;
        }
        if (item is BarcodeItem code)
        {
            var barcodeText = new TextBox { Header = "Text or expression", Text = code.Text };
            var kind = new ComboBox { Header = "Symbology", ItemsSource = Enum.GetNames<BarcodeKind>(), SelectedItem = code.Symbology.ToString(), HorizontalAlignment = HorizontalAlignment.Stretch };
            var caption = new CheckBox { Content = "Show human-readable text", IsChecked = code.ShowText };
            await DialogAsync("Barcode", Vertical(barcodeText, kind, caption), () =>
            {
                var symbology = Enum.Parse<BarcodeKind>((string)kind.SelectedItem);
                if (!barcodeText.Text.Contains('[')) _ = renderer.EncodeBarcode(symbology, barcodeText.Text);
                Session.UpdateSelection("Edit barcode", i => i is BarcodeItem b ? b with { Text = barcodeText.Text, Symbology = symbology, ShowText = caption.IsChecked == true } : i);
            }); return;
        }
        if (item is ChartItem)
        { SetStatus("Edit chart data source, category, value expression and type in Properties."); return; }
        if (item is CheckBoxItem check)
        {
            var expression = new TextBox { Text = check.Expression, Header = "Boolean expression" };
            await DialogAsync("Check box expression", expression, () => Session.UpdateSelection("Check box expression", i => i is CheckBoxItem c ? c with { Expression = expression.Text } : i)); return;
        }
        SetStatus("Edit shape geometry and appearance in Properties.");
    }
    private async Task PageSetupAsync()
    {
        var page = Session.Page;
        var form = new PropertySheet(); var paper = new ComboBox { ItemsSource = new[] { "A4", "Letter", "A3", "Custom" }, SelectedItem = "Custom", Header = "Paper size" };
        double width = page.Width, height = page.Height, left = page.Margins.Left, top = page.Margins.Top, right = page.Margins.Right, bottom = page.Margins.Bottom, gap = page.ColumnGap; int columns = page.Columns;
        var w = form.Number("Width", width, n => width = n); var h = form.Number("Height", height, n => height = n);
        form.Number("Left margin", left, n => left = n); form.Number("Top margin", top, n => top = n); form.Number("Right margin", right, n => right = n); form.Number("Bottom margin", bottom, n => bottom = n);
        form.Number("Columns", columns, n => columns = checked((int)n)); form.Number("Column gap", gap, n => gap = n);
        paper.SelectionChanged += (_, _) => { if (paper.SelectedItem is string choice && choice != "Custom") { (width, height) = choice switch { "Letter" => (612, 792), "A3" => (841.89, 1190.55), _ => (595.276, 841.89) }; w.Text = width.ToString("0.###", CultureInfo.InvariantCulture); h.Text = height.ToString("0.###", CultureInfo.InvariantCulture); } };
        var landscape = ControlTheme.Button("orientation", "Swap portrait / landscape", "report", () => { (width, height) = (height, width); w.Text = width.ToString("0.###", CultureInfo.InvariantCulture); h.Text = height.ToString("0.###", CultureInfo.InvariantCulture); });
        await DialogAsync("Page setup — all dimensions are points", Vertical(paper, landscape, form), () =>
        {
            var candidate = page with { Width = width, Height = height, Margins = new(left, top, right, bottom), Columns = columns, ColumnGap = gap };
            var copy = ReportJson.Clone(Session.Document); copy.Pages[Session.PageIndex] = candidate;
            var errors = DocumentValidator.Validate(copy); if (errors.Count > 0) throw new FormatException(string.Join('\n', errors.Select(e => e.Message)));
            Session.UpdatePage(candidate);
        });
    }
    private async Task AddBandAsync()
    {
        var kind = new ComboBox { Header = "Band type", ItemsSource = Enum.GetNames<BandKind>(), SelectedItem = BandKind.Data.ToString(), HorizontalAlignment = HorizontalAlignment.Stretch };
        await DialogAsync("Insert report band", kind, () => Session.AddBand(Enum.Parse<BandKind>((string)kind.SelectedItem)));
    }
    private async Task GroupAsync()
    {
        var band = Session.SelectedBand;
        if (band?.Kind != BandKind.Data) throw new InvalidOperationException("Select a data band before adding a group.");
        var data = Session.Document.Data.FirstOrDefault(t => t.Name == band.DataSource);
        var expression = new TextBox { Header = "Group expression", Text = data?.Columns.FirstOrDefault() ?? "Category" };
        var caption = new TextBox { Header = "Header text", Text = "[" + (data?.Columns.FirstOrDefault() ?? "Category") + "]" };
        var total = new TextBox { Header = "Footer text / aggregate expression (optional)", Text = "Records: [Count()]" };
        await DialogAsync("Add data group", Vertical(expression, caption, total), () =>
        {
            var group = new GroupDefinition { Expression = RequireName(expression.Text), Header = new() { Kind = BandKind.GroupHeader, Name = "GroupHeader" + (band.Groups.Count + 1), Height = 30, Items = [new TextItem { Name = "GroupTitle" + (band.Groups.Count + 1), Text = caption.Text, Bounds = new(0, 5, Session.Page.ColumnWidth, 24), Style = new() { FontSize = 13, Bold = true, Foreground = "#244F75" } }] }, Footer = new() { Kind = BandKind.GroupFooter, Name = "GroupFooter" + (band.Groups.Count + 1), Height = total.Text.Length == 0 ? 0 : 28, Items = total.Text.Length == 0 ? [] : [new TextItem { Name = "GroupTotal" + (band.Groups.Count + 1), Text = total.Text, Bounds = new(0, 3, Session.Page.ColumnWidth, 22), Style = new() { Bold = true, Alignment = TextAlign.Right } }] } };
            Session.UpdateBand(band with { Groups = [.. band.Groups, group] });
        });
    }
    private async Task ImportDataAsync()
    {
        var requested = Operations.CaptureRevision();
        var file = await host.OpenFileAsync([".json", ".csv"]); if (file is null) return;
        Operations.EnsureCurrent(requested);
        var name = new TextBox { Header = "Data table name (replaces an existing table with this name)", Text = Session.SelectedBand?.DataSource ?? Path.GetFileNameWithoutExtension(file.Name) };
        var infer = new CheckBox { Content = "Infer numeric CSV values (may remove leading zeros)", IsChecked = false };
        await DialogAsync("Import local data", Vertical(name, infer), () =>
        {
            Operations.EnsureCurrent(requested);
            string text = Encoding.UTF8.GetString(file.Content); string tableName = RequireName(name.Text);
            var table = file.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? DataReaders.Csv(text, tableName, options: new(InferNumbers: infer.IsChecked == true)) : DataReaders.Json(text, tableName);
            Session.ReplaceData(table); SetStatus($"Imported {table.Rows.Count} rows into {table.Name}");
        });
    }
    private async Task EditDataAsync()
    {
        if (Session.Document.Data.Count == 0) { await ImportDataAsync(); return; }
        var source = new ComboBox { Header = "Data table", ItemsSource = Session.Document.Data.Select(t => t.Name).ToArray(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var editor = Editor(DataReaders.WriteCsv(Session.Document.Data[0], false));
        var infer = new CheckBox { Content = "Infer numeric values when applying", IsChecked = true };
        source.SelectionChanged += (_, _) => { if (source.SelectedItem is string name) editor.Text = DataReaders.WriteCsv(Session.Document.Data.Single(t => t.Name == name), false); };
        await DialogAsync("Edit bound data — CSV", Vertical(source, editor, infer), () => Session.ReplaceData(DataReaders.Csv(editor.Text, (string)source.SelectedItem, options: new(InferNumbers: infer.IsChecked == true))));
    }
    private sealed record ParameterRow(Grid Panel, TextBox Name, ComboBox Kind, TextBox Value, TextBox Description, CheckBox Required);
    private async Task ParametersAsync()
    {
        var list = new StackPanel { Spacing = 7 }; var editors = new List<ParameterRow>();
        void Add(ReportParameter parameter)
        {
            var grid = new Grid(); foreach (var width in new[] { 125d, 88d, 190d, 180d, 36d, 35d }) grid.ColumnDefinitions.Add(new() { Width = new(width) });
            var name = new TextBox { Text = parameter.Name, PlaceholderText = "Name", FontSize = 11, Margin = new(0, 0, 4, 0) };
            var kind = new ComboBox { ItemsSource = new[] { "String", "Number", "Boolean", "Null" }, SelectedItem = parameter.DefaultValue.Kind.ToString(), FontSize = 11, Margin = new(0, 0, 4, 0) };
            var value = new TextBox { Text = parameter.DefaultValue.AsString(), PlaceholderText = "Default value", FontSize = 11, Margin = new(0, 0, 4, 0) };
            var description = new TextBox { Text = parameter.Description, PlaceholderText = "Description", FontSize = 11, Margin = new(0, 0, 4, 0) };
            var required = new CheckBox { IsChecked = parameter.Required, MinWidth = 0 }; ToolTipService.SetToolTip(required, "Required parameter");
            var row = new ParameterRow(grid, name, kind, value, description, required);
            var remove = ControlTheme.Button("remove-parameter", "Remove", "delete", () => { list.Children.Remove(grid); editors.Remove(row); }, text: false);
            UIElement[] cells = [name, kind, value, description, required, remove]; for (int n = 0; n < cells.Length; n++) { Grid.SetColumn(cells[n], n); grid.Children.Add(cells[n]); }
            editors.Add(row); list.Children.Add(grid);
        }
        foreach (var parameter in Session.Document.Parameters) Add(parameter);
        var button = ControlTheme.Button("add-parameter", "Add parameter", "new", () => Add(new("Parameter" + (editors.Count + 1), "")));
        await DialogAsync("Report parameters — name / type / value / description / required", Vertical(button, list), () =>
        {
            var parameters = editors.Select(e => new ReportParameter(RequireName(e.Name.Text), (string)e.Kind.SelectedItem switch
            { "Number" => Value.From(decimal.Parse(e.Value.Text, CultureInfo.InvariantCulture)), "Boolean" => Value.From(bool.Parse(e.Value.Text)), "Null" => Value.Null, _ => Value.From(e.Value.Text) }, e.Description.Text, e.Required.IsChecked == true)).ToList();
            if (parameters.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != parameters.Count) throw new FormatException("Parameter names must be unique.");
            Session.UpdateParameters(parameters);
        });
    }
    private async Task EditNativeAsync()
    {
        var editor = Editor(ReportJson.Serialize(Session.Document), 400);
        await DialogAsync("Native report document — JSON", editor, () => Load(ReportJson.Deserialize(editor.Text)));
    }
    private Task ShowCompatibilityAsync() => ShowCurrentCompatibilityAsync();
}
