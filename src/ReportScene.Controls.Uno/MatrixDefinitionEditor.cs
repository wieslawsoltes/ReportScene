using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReportScene.Model;

namespace ReportScene.Controls.Uno;

/// <summary>
/// Reusable transactional cross-tab editor. It never mutates the input definition or data tables;
/// the host applies GetDefinition() as one undoable command after validating it.
/// </summary>
public sealed class MatrixDefinitionEditor : UserControl
{
    private readonly MatrixItem original;
    private readonly DataTable[] tables;
    private readonly Dictionary<string, FrameworkElement> editors = new(StringComparer.Ordinal);
    private readonly ComboBox source = new() { Header = "Data source", Width = 290 };
    private readonly ComboBox field = new() { Header = "Field for new entries", Width = 290 };
    private readonly TextBox filter = new() { Header = "Filter expression (optional)", MaxLength = 8192 };
    private readonly Grid pages = new();
    private readonly Dictionary<string, UIElement> tabs = new(StringComparer.Ordinal);
    private readonly List<AxisRow> rowAxis = [], columnAxis = [];
    private readonly List<MeasureRow> measures = [];
    private readonly StackPanel rowPanel = new() { Spacing = 5 }, columnPanel = new() { Spacing = 5 }, measurePanel = new() { Spacing = 7 };
    private readonly TextBox rowHeaderWidth = new() { Header = "Row header width (pt)", Width = 190 };
    private readonly TextBox cellWidth = new() { Header = "Value column width (pt)", Width = 190 };
    private readonly TextBox rowHeight = new() { Header = "Minimum row height (pt)", Width = 190 };
    private readonly TextBox nullLabel = new() { Header = "Null label", Width = 190 };
    private readonly TextBox totalLabel = new() { Header = "Subtotal label", Width = 190 };
    private readonly TextBox grandLabel = new() { Header = "Grand total label", Width = 190 };
    private readonly CheckBox rowTotal = new() { Content = "Row grand total" }, columnTotal = new() { Content = "Column grand total" }, repeat = new() { Content = "Repeat column headers on continuation pages" };
    public IReadOnlyDictionary<string, FrameworkElement> Editors => editors;
    public event Action? EditorsChanged;

    public MatrixDefinitionEditor(MatrixItem definition, IEnumerable<DataTable> dataTables)
    {
        ArgumentNullException.ThrowIfNull(definition); ArgumentNullException.ThrowIfNull(dataTables);
        original = definition; tables = dataTables.ToArray();
        FontSize = 12; HorizontalContentAlignment = HorizontalAlignment.Stretch;
        source.ItemsSource = tables.Select(t => t.Name).ToArray(); source.SelectedItem = definition.DataSource;
        source.SelectionChanged += (_, _) => RefreshFields();
        filter.Text = definition.Filter ?? "";
        Register("source", source); Register("field", field); Register("filter", filter);
        var root = new StackPanel { Spacing = 10, MinWidth = 580 };
        root.Children.Add(Horizontal(source, field)); root.Children.Add(filter);
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (string name in new[] { "Rows", "Columns", "Measures", "Options" })
            navigation.Children.Add(Button("tab-" + name.ToLowerInvariant(), name, () => ShowTab(name)));
        root.Children.Add(navigation);
        AddTab("Rows", AxisTab(false)); AddTab("Columns", AxisTab(true));
        var measureTab = new StackPanel { Spacing = 8 };
        measureTab.Children.Add(ControlTheme.Label("Each measure has its own aggregate, format and percentage mode. Totals aggregate source values.", 11));
        measureTab.Children.Add(Button("measures-add", "Add measure", () =>
        {
            if (measures.Count < 32) AddMeasure(new() { Expression = SelectedField(), Title = SelectedField() });
        }));
        measureTab.Children.Add(measurePanel); AddTab("Measures", measureTab);
        rowHeaderWidth.Text = definition.RowHeaderWidth.ToString("0.###", CultureInfo.InvariantCulture);
        cellWidth.Text = definition.CellWidth.ToString("0.###", CultureInfo.InvariantCulture);
        rowHeight.Text = definition.RowHeight.ToString("0.###", CultureInfo.InvariantCulture);
        nullLabel.Text = definition.NullLabel; totalLabel.Text = definition.TotalLabel; grandLabel.Text = definition.GrandTotalLabel;
        rowTotal.IsChecked = definition.ShowRowGrandTotal; columnTotal.IsChecked = definition.ShowColumnGrandTotal; repeat.IsChecked = definition.RepeatHeaders;
        Register("row-width", rowHeaderWidth); Register("cell-width", cellWidth); Register("row-height", rowHeight);
        Register("null-label", nullLabel); Register("total-label", totalLabel); Register("grand-label", grandLabel);
        Register("row-total", rowTotal); Register("column-total", columnTotal); Register("repeat", repeat);
        var options = new StackPanel { Spacing = 10 };
        options.Children.Add(Horizontal(rowHeaderWidth, cellWidth, rowHeight));
        options.Children.Add(Horizontal(nullLabel, totalLabel, grandLabel));
        options.Children.Add(rowTotal); options.Children.Add(columnTotal); options.Children.Add(repeat);
        options.Children.Add(ControlTheme.Label("Wide matrices continue on additional pages. Row headers repeat on each horizontal tile; whole rows stay together vertically.", 11));
        AddTab("Options", options);
        var scroll = new ScrollViewer { Content = pages, Height = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(scroll); Content = root;
        RefreshFields();
        foreach (var dimension in definition.RowDimensions) AddAxis(false, dimension);
        foreach (var dimension in definition.ColumnDimensions) AddAxis(true, dimension);
        foreach (var measure in definition.Measures) AddMeasure(measure);
        ShowTab("Rows");
    }
    public MatrixItem GetDefinition()
    {
        double Number(TextBox editor) => double.TryParse(editor.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value : throw new FormatException("Matrix dimensions must be numbers expressed in points.");
        var result = original with
        {
            DataSource = source.SelectedItem as string ?? "", Filter = string.IsNullOrWhiteSpace(filter.Text) ? null : filter.Text,
            RowDimensions = rowAxis.Select(ReadAxis).ToList(), ColumnDimensions = columnAxis.Select(ReadAxis).ToList(),
            Measures = measures.Select(row => new MatrixMeasure
            {
                Expression = row.Expression.Text, Title = row.Title.Text, Format = string.IsNullOrWhiteSpace(row.Format.Text) ? null : row.Format.Text,
                Aggregate = Enum.Parse<MatrixAggregate>((string)row.Aggregate.SelectedItem), Display = Enum.Parse<MatrixDisplay>((string)row.Display.SelectedItem)
            }).ToList(),
            RowHeaderWidth = Number(rowHeaderWidth), CellWidth = Number(cellWidth), RowHeight = Number(rowHeight),
            NullLabel = nullLabel.Text, TotalLabel = totalLabel.Text, GrandTotalLabel = grandLabel.Text,
            ShowRowGrandTotal = rowTotal.IsChecked == true, ShowColumnGrandTotal = columnTotal.IsChecked == true, RepeatHeaders = repeat.IsChecked == true
        };
        var errors = MatrixDefinitionValidator.Validate(result);
        if (errors.Count > 0) throw new FormatException(string.Join("\n", errors.Select(e => e.Message)));
        if (!tables.Any(t => t.Name == result.DataSource)) throw new FormatException("Choose a bound matrix data source.");
        return result;
    }
    private static MatrixDimension ReadAxis(AxisRow row) => new()
    { Expression = row.Expression.Text, Title = row.Title.Text, Descending = row.Descending.IsChecked == true, Subtotal = row.Subtotal.IsChecked == true };
    private void RefreshFields()
    {
        var table = tables.FirstOrDefault(t => t.Name == source.SelectedItem as string);
        field.ItemsSource = table?.Columns.ToArray() ?? []; field.SelectedIndex = table?.Columns.Count > 0 ? 0 : -1;
    }
    private string SelectedField() => field.SelectedItem as string ?? "Value";
    private UIElement AxisTab(bool columns)
    {
        string key = columns ? "columns" : "rows";
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(ControlTheme.Label("Levels are ordered outermost first. Leave an axis empty for a single aggregate group.", 11));
        panel.Children.Add(Button(key + "-add", "Add " + (columns ? "column" : "row") + " level", () =>
        {
            var list = columns ? columnAxis : rowAxis;
            if (list.Count < 8) AddAxis(columns, new() { Expression = SelectedField(), Title = SelectedField() });
        }));
        panel.Children.Add(columns ? columnPanel : rowPanel); return panel;
    }
    private void AddTab(string name, UIElement content) { tabs[name] = content; pages.Children.Add(content); }
    private void ShowTab(string name)
    {
        foreach (var tab in tabs) tab.Value.Visibility = tab.Key == name ? Visibility.Visible : Visibility.Collapsed;
        EditorsChanged?.Invoke();
    }
    private void Register(string name, FrameworkElement element)
    { string id = "matrix-" + name; ControlTheme.Identify(element, id, name.Replace('-', ' ')); editors[id] = element; }
    private Button Button(string id, string label, Action action)
    { var button = ControlTheme.Button("matrix-" + id, label, "", action); Register(id, button); return button; }
    private static StackPanel Horizontal(params UIElement[] children)
    { var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 }; foreach (var child in children) panel.Children.Add(child); return panel; }
    private void AddAxis(bool columns, MatrixDimension dimension)
    {
        var row = new AxisRow(); row.Expression.Text = dimension.Expression; row.Title.Text = dimension.Title;
        row.Descending.IsChecked = dimension.Descending; row.Subtotal.IsChecked = dimension.Subtotal;
        var list = columns ? columnAxis : rowAxis; list.Add(row);
        row.Up.Click += (_, _) => { int n = list.IndexOf(row); if (n > 0) { (list[n - 1], list[n]) = (list[n], list[n - 1]); RefreshAxis(columns); } };
        row.Down.Click += (_, _) => { int n = list.IndexOf(row); if (n + 1 < list.Count) { (list[n + 1], list[n]) = (list[n], list[n + 1]); RefreshAxis(columns); } };
        row.Delete.Click += (_, _) => { list.Remove(row); RefreshAxis(columns); };
        RefreshAxis(columns);
    }
    private void RefreshAxis(bool columns)
    {
        var list = columns ? columnAxis : rowAxis; var panel = columns ? columnPanel : rowPanel; string key = columns ? "columns" : "rows";
        foreach (var id in editors.Keys.Where(k => k.StartsWith("matrix-" + key + "-item-", StringComparison.Ordinal)).ToArray()) editors.Remove(id);
        panel.Children.Clear();
        for (int n = 0; n < list.Count; n++)
        {
            var row = list[n]; row.Up.IsEnabled = n > 0; row.Down.IsEnabled = n + 1 < list.Count;
            panel.Children.Add(row.Panel);
            Register(key + "-item-" + n + "-expression", row.Expression); Register(key + "-item-" + n + "-title", row.Title);
            Register(key + "-item-" + n + "-descending", row.Descending); Register(key + "-item-" + n + "-subtotal", row.Subtotal);
            Register(key + "-item-" + n + "-up", row.Up); Register(key + "-item-" + n + "-down", row.Down); Register(key + "-item-" + n + "-delete", row.Delete);
        }
        if (editors.TryGetValue("matrix-" + key + "-add", out var add) && add is Control control) control.IsEnabled = list.Count < 8;
        EditorsChanged?.Invoke();
    }
    private void AddMeasure(MatrixMeasure measure)
    {
        var row = new MeasureRow(); row.Expression.Text = measure.Expression; row.Title.Text = measure.Title; row.Format.Text = measure.Format ?? "";
        row.Aggregate.ItemsSource = Enum.GetNames<MatrixAggregate>(); row.Aggregate.SelectedItem = measure.Aggregate.ToString();
        row.Display.ItemsSource = Enum.GetNames<MatrixDisplay>(); row.Display.SelectedItem = measure.Display.ToString();
        measures.Add(row);
        row.Up.Click += (_, _) => { int n = measures.IndexOf(row); if (n > 0) { (measures[n - 1], measures[n]) = (measures[n], measures[n - 1]); RefreshMeasures(); } };
        row.Down.Click += (_, _) => { int n = measures.IndexOf(row); if (n + 1 < measures.Count) { (measures[n + 1], measures[n]) = (measures[n], measures[n + 1]); RefreshMeasures(); } };
        row.Delete.Click += (_, _) => { if (measures.Count > 1) { measures.Remove(row); RefreshMeasures(); } };
        RefreshMeasures();
    }
    private void RefreshMeasures()
    {
        foreach (var id in editors.Keys.Where(k => k.StartsWith("matrix-measures-item-", StringComparison.Ordinal)).ToArray()) editors.Remove(id);
        measurePanel.Children.Clear();
        for (int n = 0; n < measures.Count; n++)
        {
            var row = measures[n]; row.Up.IsEnabled = n > 0; row.Down.IsEnabled = n + 1 < measures.Count; row.Delete.IsEnabled = measures.Count > 1;
            measurePanel.Children.Add(row.Panel); string id = "measures-item-" + n;
            Register(id + "-expression", row.Expression); Register(id + "-title", row.Title); Register(id + "-format", row.Format);
            Register(id + "-aggregate", row.Aggregate); Register(id + "-display", row.Display);
            Register(id + "-up", row.Up); Register(id + "-down", row.Down); Register(id + "-delete", row.Delete);
        }
        if (editors.TryGetValue("matrix-measures-add", out var add) && add is Control control) control.IsEnabled = measures.Count < 32;
        EditorsChanged?.Invoke();
    }
    private sealed class AxisRow
    {
        public TextBox Expression { get; } = new() { Header = "Expression", Width = 155, MaxLength = 8192 };
        public TextBox Title { get; } = new() { Header = "Title", Width = 120, MaxLength = 1024 };
        public CheckBox Descending { get; } = new() { Content = "Descending", MinWidth = 100 };
        public CheckBox Subtotal { get; } = new() { Content = "Subtotal", MinWidth = 85 };
        public Button Up { get; } = new() { Content = "↑", MinWidth = 28, Padding = new(4) };
        public Button Down { get; } = new() { Content = "↓", MinWidth = 28, Padding = new(4) };
        public Button Delete { get; } = new() { Content = "×", MinWidth = 28, Padding = new(4) };
        public UIElement Panel { get; }
        public AxisRow() { Panel = Horizontal(Expression, Title, Descending, Subtotal, Up, Down, Delete); }
    }
    private sealed class MeasureRow
    {
        public TextBox Expression { get; } = new() { Header = "Expression", Width = 170, MaxLength = 8192 };
        public TextBox Title { get; } = new() { Header = "Title", Width = 130, MaxLength = 1024 };
        public TextBox Format { get; } = new() { Header = "Format", Width = 80, MaxLength = 128 };
        public ComboBox Aggregate { get; } = new() { Header = "Aggregate", Width = 130 };
        public ComboBox Display { get; } = new() { Header = "Display", Width = 195 };
        public Button Up { get; } = new() { Content = "↑", MinWidth = 28, Padding = new(4) };
        public Button Down { get; } = new() { Content = "↓", MinWidth = 28, Padding = new(4) };
        public Button Delete { get; } = new() { Content = "×", MinWidth = 28, Padding = new(4) };
        public UIElement Panel { get; }
        public MeasureRow()
        {
            var panel = new StackPanel { Spacing = 4, BorderBrush = ControlTheme.Brush("#D5DCE3"), BorderThickness = new(0, 0, 0, 1), Padding = new(0, 0, 0, 7) };
            panel.Children.Add(Horizontal(Expression, Title, Format, Up, Down, Delete)); panel.Children.Add(Horizontal(Aggregate, Display)); Panel = panel;
        }
    }
}
