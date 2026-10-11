using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReportScene.Model;

namespace ReportScene.Controls.Uno;

/// <summary>Reusable transactional editor for subreport targets, printing mode and caller-scoped parameters.</summary>
public sealed class SubreportDefinitionEditor : UserControl
{
    private readonly SubreportItem original;
    private readonly ComboBox target = new() { Header = "Target report page", Width = 430 };
    private readonly CheckBox onParent = new() { Content = "Print on parent band" };
    private readonly CheckBox shift = new() { Content = "Shift following objects when the subreport grows or shrinks" };
    private readonly CheckBox grow = new() { Content = "Can grow" }, shrink = new() { Content = "Can shrink" };
    private readonly StackPanel rowsPanel = new() { Spacing = 6 };
    private readonly List<(StackPanel Panel, TextBox Name, TextBox Expression, Button Remove)> bindings = [];
    private readonly Dictionary<string, FrameworkElement> editors = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, FrameworkElement> Editors => editors;
    public event Action? EditorsChanged;
    public SubreportDefinitionEditor(SubreportItem item, IEnumerable<ReportPage> pages, string ownerPageName)
    {
        ArgumentNullException.ThrowIfNull(item); ArgumentNullException.ThrowIfNull(pages); original = item;
        target.ItemsSource = pages.Where(p => !p.Name.Equals(ownerPageName, StringComparison.OrdinalIgnoreCase)).Select(p => p.Name).ToArray(); target.SelectedItem = item.PageName;
        onParent.IsChecked = item.PrintOnParent; shift.IsChecked = item.ShiftFollowing; grow.IsChecked = item.CanGrow; shrink.IsChecked = item.CanShrink;
        Register("target", target); Register("parent", onParent); Register("shift", shift); Register("grow", grow); Register("shrink", shrink);
        var root = new StackPanel { Spacing = 8, MinWidth = 540 };
        root.Children.Add(target); root.Children.Add(onParent); root.Children.Add(shift);
        var sizes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 }; sizes.Children.Add(grow); sizes.Children.Add(shrink); root.Children.Add(sizes);
        root.Children.Add(new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Text = "Band flow: place the subreport at the lower edge of its band. Parallel references paginate independently. Put following objects in a Child band.\nPrint on parent: compose objects into the containing band; enable band splitting for long content." });
        root.Children.Add(ControlTheme.Label("Parameter bindings — evaluated in the caller's row scope", 12, true));
        var add = ControlTheme.Button("subreport-add-binding", "Add parameter", "new", () => { if (bindings.Count < 256) AddBinding(new("Parameter" + (bindings.Count + 1), "0")); });
        Register("add-binding", add); root.Children.Add(add); root.Children.Add(rowsPanel);
        Content = root;
        foreach (var binding in item.ParameterBindings) AddBinding(binding);
        onParent.Checked += (_, _) => UpdateMode(); onParent.Unchecked += (_, _) => UpdateMode(); UpdateMode();
    }
    private void UpdateMode() { shift.IsEnabled = grow.IsEnabled = shrink.IsEnabled = onParent.IsChecked == true; }
    private void Register(string name, FrameworkElement element)
    { string id = "subreport-" + name; editors[id] = ControlTheme.Identify(element, id, name); }
    private void AddBinding(SubreportParameter binding)
    {
        var name = new TextBox { Text = binding.Name, Header = "Name", Width = 150, MaxLength = 256 };
        var expression = new TextBox { Text = binding.Expression, Header = "Caller expression", Width = 340, MaxLength = 8192 };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        var remove = new Button { Content = "×", MinWidth = 30, Padding = new(5), VerticalAlignment = VerticalAlignment.Bottom };
        var entry = (panel, name, expression, remove); bindings.Add(entry);
        remove.Click += (_, _) => { bindings.Remove(entry); Refresh(); };
        panel.Children.Add(name); panel.Children.Add(expression); panel.Children.Add(remove); Refresh();
    }
    private void Refresh()
    {
        foreach (var key in editors.Keys.Where(k => k.StartsWith("subreport-binding-", StringComparison.Ordinal)).ToArray()) editors.Remove(key);
        rowsPanel.Children.Clear();
        for (int n = 0; n < bindings.Count; n++)
        {
            var row = bindings[n]; rowsPanel.Children.Add(row.Panel);
            Register("binding-" + n + "-name", row.Name); Register("binding-" + n + "-expression", row.Expression); Register("binding-" + n + "-remove", row.Remove);
        }
        EditorsChanged?.Invoke();
    }
    public SubreportItem GetDefinition()
    {
        if (target.SelectedItem is not string page) throw new FormatException("Choose a target report page.");
        var values = bindings.Select(b => new SubreportParameter(b.Name.Text.Trim(), b.Expression.Text.Trim())).ToList();
        if (values.Any(v => string.IsNullOrWhiteSpace(v.Name) || string.IsNullOrWhiteSpace(v.Expression)) || values.Select(v => v.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count)
            throw new FormatException("Each parameter needs a unique name and a caller expression.");
        return original with { PageName = page, PrintOnParent = onParent.IsChecked == true, ShiftFollowing = shift.IsChecked == true, CanGrow = grow.IsChecked == true, CanShrink = shrink.IsChecked == true, ParameterBindings = values };
    }
}
