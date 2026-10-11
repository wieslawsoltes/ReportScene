using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReportScene.Designer;

namespace ReportScene.Controls.Uno;

/// <summary>Reusable searchable native tree. Stable keys preserve nodes, selection and expansion across report edits.</summary>
public sealed class ReportOutlineView : UserControl
{
    private readonly TreeView tree = new() { SelectionMode = TreeViewSelectionMode.Single, FontSize = 11 };
    private readonly TextBox filter = new() { PlaceholderText = "Search…", FontSize = 11, MinHeight = 0, Height = 24, Padding = new(5, 1, 3, 1), MaxLength = 1024, CornerRadius = new(0) };
    private readonly Dictionary<string, TreeViewNode> nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<TreeViewNode, string> keys = new();
    private readonly Dictionary<string, bool> expansion = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FrameworkElement> controls = new(StringComparer.Ordinal);
    private DesignerOutline outline = new([]);
    private IReadOnlyList<DesignerOutlineEntry> projection = [];
    private bool filtering;
    private string? selectedKey;
    public long CreatedNodeCount { get; private set; }
    public long UpdateCount { get; private set; }
    public string FilterText => filter.Text;
    public int EntryCount => outline.Entries.Count;
    public int ProjectedCount => projection.Count;
    public int VisibleRowCount
    {
        get { int count = 0; var pending = new Stack<TreeViewNode>(tree.RootNodes); while (pending.TryPop(out var node)) { count++; if (node.IsExpanded) foreach (var child in node.Children) pending.Push(child); } return count; }
    }
    public event Action<DesignerOutlineEntry>? Invoked;
    public event Action? ViewChanged;
    public ReportOutlineView()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        var root = new Grid { Background = ControlTheme.Brush("#FFFFFF") };
        root.RowDefinitions.Add(new() { Height = new(25) }); root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var toolbar = new Grid { Background = ControlTheme.Brush("#F3F3F3") };
        toolbar.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); toolbar.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); toolbar.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); toolbar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        Button Button(string id, string text, string name, Action action)
        { var button = new Button { Content = text, FontSize = 12, Width = 25, Height = 24, MinWidth = 0, MinHeight = 0, Padding = new(0), CornerRadius = new(0), BorderThickness = new(0), Background = ControlTheme.Brush("#00FFFFFF") }; ControlTheme.Identify(button, id, name); button.Click += (_, _) => action(); controls[id] = button; return button; }
        toolbar.Children.Add(Button("outline-expand", "+", "Expand all", () => SetExpanded(true)));
        var collapse = Button("outline-collapse", "−", "Collapse all", () => SetExpanded(false)); Grid.SetColumn(collapse, 1); toolbar.Children.Add(collapse);
        Grid.SetColumn(filter, 2); toolbar.Children.Add(filter); controls["outline-filter"] = ControlTheme.Identify(filter, "outline-filter", "Search report or data tree");
        var clear = Button("outline-clear", "×", "Clear tree search", () => filter.Text = ""); Grid.SetColumn(clear, 3); toolbar.Children.Add(clear);
        root.Children.Add(toolbar); Grid.SetRow(tree, 1); root.Children.Add(tree); Content = root;
        var style = new Style(typeof(TreeViewItem));
        style.Setters.Add(new Setter(MinHeightProperty, 22d)); style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(1, 0, 1, 0))); style.Setters.Add(new Setter(Control.FontSizeProperty, 11d)); tree.ItemContainerStyle = style;
        filter.TextChanged += (_, _) => ApplyProjection();
        tree.ItemInvoked += (_, args) => { if (args.InvokedItem is TreeViewNode node && keys.TryGetValue(node, out var key) && outline.Find(key) is { } entry) { selectedKey = key; Invoked?.Invoke(entry); } };
        tree.Expanding += (_, _) => ViewChanged?.Invoke(); tree.Collapsed += (_, _) => ViewChanged?.Invoke();
    }
    public bool SetOutline(DesignerOutline value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (outline.HasSameEntries(value)) return false;
        outline = value; UpdateCount++; ApplyProjection(); return true;
    }
    public void SelectKey(string? key)
    {
        selectedKey = key;
        tree.SelectedNode = key is not null && nodes.TryGetValue(key, out var node) && projection.Any(e => e.Key == key) ? node : null;
    }
    public IEnumerable<KeyValuePair<string, FrameworkElement>> Elements()
    {
        foreach (var entry in controls) yield return entry;
        foreach (var entry in projection)
            if (nodes.TryGetValue(entry.Key, out var node) && tree.ContainerFromNode(node) is FrameworkElement container)
                yield return new("outline-" + entry.Key, container);
    }
    private void CaptureExpansion()
    { if (!filtering) foreach (var entry in nodes) expansion[entry.Key] = entry.Value.IsExpanded; }
    private void SetExpanded(bool value)
    {
        foreach (var node in nodes.Values) node.IsExpanded = value;
        if (!filtering) foreach (var key in nodes.Keys) expansion[key] = value;
        ViewChanged?.Invoke();
    }
    private void ApplyProjection()
    {
        CaptureExpansion(); projection = outline.Filter(filter.Text); filtering = !string.IsNullOrWhiteSpace(filter.Text);
        var desired = new Dictionary<TreeViewNode, List<TreeViewNode>>(); var roots = new List<TreeViewNode>();
        foreach (var entry in outline.Entries)
        {
            if (!nodes.TryGetValue(entry.Key, out var node))
            { node = new TreeViewNode(); nodes.Add(entry.Key, node); keys.Add(node, entry.Key); CreatedNodeCount++; }
            if (!Equals(node.Content, entry.Label)) node.Content = entry.Label;
            desired[node] = [];
        }
        foreach (var entry in projection)
        {
            var node = nodes[entry.Key];
            if (entry.ParentKey is null) roots.Add(node); else desired[nodes[entry.ParentKey]].Add(node);
        }
        // Detach every changed child collection before reparenting existing nodes.
        var changes = new List<(IList<TreeViewNode> Current, List<TreeViewNode> Desired)>();
        if (!tree.RootNodes.SequenceEqual(roots)) changes.Add((tree.RootNodes, roots));
        foreach (var node in nodes.Values)
        {
            var children = desired.GetValueOrDefault(node) ?? [];
            if (!node.Children.SequenceEqual(children)) changes.Add((node.Children, children));
        }
        foreach (var change in changes) change.Current.Clear();
        foreach (var change in changes) foreach (var node in change.Desired) change.Current.Add(node);
        foreach (var key in nodes.Keys.Where(k => outline.Find(k) is null).ToArray()) { keys.Remove(nodes[key]); nodes.Remove(key); expansion.Remove(key); }
        foreach (var entry in nodes) entry.Value.IsExpanded = filtering || expansion.GetValueOrDefault(entry.Key, true);
        SelectKey(selectedKey); ViewChanged?.Invoke();
    }
}
