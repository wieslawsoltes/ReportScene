using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReportScene.Designer;

namespace ReportScene.Controls.Uno;

/// <summary>Draft-only native options control. ReadValue validates atomically; loading/defaults never changes a live designer.</summary>
public sealed class DesignerOptionsEditor : UserControl
{
    private readonly Dictionary<string, FrameworkElement> editors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StackPanel> panels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> tabs = new(StringComparer.Ordinal);
    private string selectedTab = "workspace";
    public IReadOnlyDictionary<string, FrameworkElement> Editors => editors;
    public DesignerOptionsEditor(DesignerPreferences value)
    {
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = new(28) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var headings = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        root.Children.Add(headings);
        var body = new Grid { MinWidth = 280, Padding = new(8) }; Grid.SetRow(body, 1); root.Children.Add(body);
        foreach (var (id, label) in new[] { ("workspace", "Workspace"), ("grid", "Grid and rulers"), ("display", "Display") })
        {
            var panel = new StackPanel { Spacing = 4 }; panels.Add(id, panel); body.Children.Add(panel);
            var tab = new Button { Content = label, FontSize = 11, MinHeight = 0, Height = 26, Padding = new(10, 1, 10, 1), CornerRadius = new(0) };
            tab.Click += (_, _) => SelectTab(id); tabs.Add(id, tab); headings.Children.Add(tab); Identify("tab-" + id, tab);
        }
        Toggle("workspace", "show-tree", "Show Data / Report Tree"); Toggle("workspace", "show-properties", "Show Properties");
        Number("workspace", "sidebar-width", "Preferred tool-window width (logical pixels)");
        Number("workspace", "tree-height", "Preferred tree height (logical pixels)");
        Choice("workspace", "explorer-tab", "Initial explorer tab", Enum.GetNames<DesignerExplorerTab>());
        Note("workspace", "Small windows temporarily constrain or hide panes. Your preferred dimensions are restored when space is available.");
        Toggle("grid", "show-grid", "Show grid"); Toggle("grid", "snap-grid", "Snap to grid");
        Number("grid", "grid-size", "Grid spacing (points)"); Toggle("grid", "show-rulers", "Show rulers");
        Choice("grid", "units", "Geometry display units", Enum.GetNames<DesignerUnit>());
        Note("grid", "One point is 1/72 inch. Report geometry and font sizes do not change when you select different display units.");
        Toggle("display", "full-paper", "Show full paper instead of compact bands");
        Toggle("display", "alphabetical", "Sort properties alphabetically");
        Number("display", "design-zoom", "Designer zoom (%)"); Number("display", "preview-zoom", "Preview zoom (%)");
        Note("display", "These are application preferences. They are not stored in report files and do not create undo entries.");
        Content = root; LoadValue(value); SelectTab(selectedTab);
    }
    private void Identify(string id, FrameworkElement control) => editors.Add("options-" + id, ControlTheme.Identify(control, "options-" + id, id.Replace('-', ' ')));
    private void SelectTab(string id)
    {
        selectedTab = id;
        foreach (var pair in panels) pair.Value.Visibility = pair.Key == id ? Visibility.Visible : Visibility.Collapsed;
        foreach (var pair in tabs) pair.Value.Background = ControlTheme.Brush(pair.Key == id ? "#D9EAF5" : "#F1F1F1");
    }
    private void Toggle(string group, string id, string label)
    {
        var control = new CheckBox { Content = label, FontSize = 11, MinHeight = 26, Padding = new(0) };
        Identify(id, control); panels[group].Children.Add(control);
    }
    private void Number(string group, string id, string label)
    {
        var control = new TextBox { FontSize = 11, MinHeight = 0, Height = 27, Padding = new(5, 2, 4, 2), CornerRadius = new(0), MaxLength = 24 };
        Row(group, id, label, control);
    }
    private void Choice(string group, string id, string label, string[] values)
    {
        var control = new ReportComboBox { ItemsSource = values, FontSize = 11, MinHeight = 0, Height = 27, Padding = new(5, 0, 0, 0), CornerRadius = new(0), HorizontalAlignment = HorizontalAlignment.Stretch };
        Row(group, id, label, control);
    }
    private void Row(string group, string id, string label, FrameworkElement control)
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = new(145) });
        var caption = ControlTheme.Label(label, 11); caption.TextWrapping = TextWrapping.Wrap; caption.Margin = new(0, 0, 8, 0);
        row.Children.Add(caption); Grid.SetColumn(control, 1); row.Children.Add(control); Identify(id, control); panels[group].Children.Add(row);
    }
    private void Note(string group, string text) => panels[group].Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new(0, 8, 0, 0), Foreground = ControlTheme.Brush("#606060") });
    private T Control<T>(string id) where T : FrameworkElement => (T)editors["options-" + id];
    public void LoadValue(DesignerPreferences value)
    {
        ArgumentNullException.ThrowIfNull(value); value.Validate();
        void Flag(string id, bool enabled) => Control<CheckBox>(id).IsChecked = enabled;
        void Number(string id, double number) => Control<TextBox>(id).Text = number.ToString("0.###", CultureInfo.InvariantCulture);
        Flag("show-tree", value.ShowTree); Flag("show-properties", value.ShowProperties); Number("sidebar-width", value.SidebarWidth); Number("tree-height", value.TreeHeight);
        Control<ReportComboBox>("explorer-tab").SelectedItem = value.ExplorerTab.ToString();
        Flag("show-grid", value.ShowGrid); Flag("snap-grid", value.SnapToGrid); Number("grid-size", value.GridSize); Flag("show-rulers", value.ShowRulers);
        Control<ReportComboBox>("units").SelectedItem = value.Unit.ToString(); Flag("full-paper", value.ShowFullPaper);
        Flag("alphabetical", value.AlphabeticalProperties); Number("design-zoom", value.DesignZoom * 100); Number("preview-zoom", value.PreviewZoom * 100);
    }
    public void RestoreDefaults() => LoadValue(new());
    public DesignerPreferences ReadValue()
    {
        bool Flag(string id) => Control<CheckBox>(id).IsChecked == true;
        double Number(string id)
        {
            string text = Control<TextBox>(id).Text;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
                throw new FormatException(id.Replace('-', ' ') + " must be a finite number.");
            return value;
        }
        T Choice<T>(string id) where T : struct, Enum => Control<ReportComboBox>(id).SelectedItem is string name && Enum.TryParse<T>(name, out var value) && Enum.IsDefined(value)
            ? value : throw new FormatException("Select a valid " + id + ".");
        return new DesignerPreferences { ShowTree = Flag("show-tree"), ShowProperties = Flag("show-properties"), SidebarWidth = Number("sidebar-width"), TreeHeight = Number("tree-height"),
            ExplorerTab = Choice<DesignerExplorerTab>("explorer-tab"), ShowGrid = Flag("show-grid"), SnapToGrid = Flag("snap-grid"), GridSize = Number("grid-size"),
            ShowRulers = Flag("show-rulers"), Unit = Choice<DesignerUnit>("units"), ShowFullPaper = Flag("full-paper"), AlphabeticalProperties = Flag("alphabetical"),
            DesignZoom = Number("design-zoom") / 100, PreviewZoom = Number("preview-zoom") / 100 }.Validate();
    }
}
