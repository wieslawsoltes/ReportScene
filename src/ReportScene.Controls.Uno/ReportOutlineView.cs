using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReportScene.Designer;
using Windows.System;

namespace ReportScene.Controls.Uno;

/// <summary>Searchable native trees with independent named contexts and indexed selection lookup.</summary>
public sealed class ReportOutlineView : UserControl
{
    private sealed class View(string key)
    {
        public string Key = key, Filter = "";
        public string? SelectedKey;
        public bool Filtering;
        // Native-only drag reordering would mutate nodes without changing the report.
        public readonly TreeView Tree = new() { SelectionMode = TreeViewSelectionMode.Single, FontSize = 11, CanDragItems = false, CanReorderItems = false, AllowDrop = false };
        public DesignerOutline Outline = new([]);
        public DesignerOutlineProjection Projection = new(new DesignerOutline([]));
        public readonly Dictionary<string, TreeViewNode> Nodes = new(StringComparer.Ordinal);
        public readonly Dictionary<TreeViewNode, string> Keys = new();
        public readonly Dictionary<string, bool> Expansion = new(StringComparer.Ordinal);
    }
    public const int MaximumContexts = 8;
    private readonly Grid treeHost = new();
    private readonly TextBox filter = new() { PlaceholderText = "Search…", FontSize = 11, MinHeight = 0, Height = 24, Padding = new(5, 1, 3, 1), MaxLength = 1024, CornerRadius = new(0) };
    private readonly Dictionary<string, View> contexts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FrameworkElement> controls = new(StringComparer.Ordinal);
    private View active;
    private int synchronizing;
    public long CreatedNodeCount { get; private set; }
    public long UpdateCount { get; private set; }
    public string ContextKey => active.Key;
    public int ContextCount => contexts.Count;
    public string FilterText
    {
        get => filter.Text;
        set { ArgumentNullException.ThrowIfNull(value); if (value.Length > 1024) throw new ArgumentException("Outline search is limited to 1024 characters.", nameof(value)); filter.Text = value; }
    }
    public int EntryCount => active.Outline.Entries.Count;
    public int ProjectedCount => active.Projection.Count;
    public int VisibleRowCount
    {
        get { int count = 0; var pending = new Stack<TreeViewNode>(active.Tree.RootNodes); while (pending.TryPop(out var node)) { count++; if (node.IsExpanded) foreach (var child in node.Children) pending.Push(child); } return count; }
    }
    public event Action<DesignerOutlineEntry>? Invoked;
    /// <summary>User-driven selection of a report page/band/object, including native keyboard navigation.</summary>
    public event Action<DesignerOutlineEntry>? NavigationRequested;
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
        Grid.SetColumn(filter, 2); toolbar.Children.Add(filter); controls["outline-filter"] = ControlTheme.Identify(filter, "outline-filter", "Search report or data tree; Escape clears search");
        var clear = Button("outline-clear", "×", "Clear tree search", () => filter.Text = ""); Grid.SetColumn(clear, 3); toolbar.Children.Add(clear);
        root.Children.Add(toolbar); Grid.SetRow(treeHost, 1); root.Children.Add(treeHost); Content = root;
        active = CreateView("default");
        filter.TextChanged += (_, _) =>
        {
            // Native TextChanged may be delivered after a guarded programmatic switch.
            // Equal text must not rebuild that context or overwrite its live expansion.
            if (synchronizing == 0 && active.Filter != filter.Text) { active.Filter = filter.Text; ApplyProjection(active); }
        };
        filter.KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) { filter.Text = ""; e.Handled = true; } };
    }
    private View CreateView(string key)
    {
        var view = new View(key);
        var style = new Style(typeof(TreeViewItem));
        style.Setters.Add(new Setter(MinHeightProperty, 22d)); style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(1, 0, 1, 0))); style.Setters.Add(new Setter(Control.FontSizeProperty, 11d)); view.Tree.ItemContainerStyle = style;
        view.Tree.ItemInvoked += (_, args) =>
        {
            if (synchronizing != 0 || !ReferenceEquals(active, view)) return;
            if (args.InvokedItem is TreeViewNode node && view.Keys.TryGetValue(node, out var id) && view.Outline.Find(id) is { } entry)
            { view.SelectedKey = id; Invoked?.Invoke(entry); }
        };
        view.Tree.SelectionChanged += (_, args) =>
        {
            if (synchronizing != 0 || !ReferenceEquals(active, view)) return;
            if (args.AddedItems.LastOrDefault() is TreeViewNode node && view.Keys.TryGetValue(node, out var id) && view.Outline.Find(id) is { } entry)
            {
                view.SelectedKey = id;
                // Arrowing through the data tree must never insert report objects.
                if (entry.PageIndex >= 0) NavigationRequested?.Invoke(entry);
            }
        };
        view.Tree.Expanding += (_, _) => { if (synchronizing == 0 && ReferenceEquals(view, active)) ViewChanged?.Invoke(); };
        view.Tree.Collapsed += (_, _) => { if (synchronizing == 0 && ReferenceEquals(view, active)) ViewChanged?.Invoke(); };
        contexts.Add(key, view); treeHost.Children.Add(view.Tree); return view;
    }
    public bool SetOutline(DesignerOutline value) => SetOutline(value, active.Key);
    /// <summary>Switch named contexts without destroying their native nodes, search, selection or expansion.</summary>
    public bool SetOutline(DesignerOutline value, string contextKey)
    {
        ArgumentNullException.ThrowIfNull(value); ArgumentException.ThrowIfNullOrWhiteSpace(contextKey);
        if (contextKey.Length > 128) throw new ArgumentException("Context keys are limited to 128 characters.", nameof(contextKey));
        if (!contexts.TryGetValue(contextKey, out var view))
        {
            if (contexts.Count == 1 && active.Key == "default" && active.Outline.Entries.Count == 0)
            { contexts.Remove(active.Key); active.Key = contextKey; contexts.Add(contextKey, active); view = active; }
            else
            {
                if (contexts.Count >= MaximumContexts) throw new InvalidOperationException("Reset outline contexts before exceeding the eight-context limit.");
                view = CreateView(contextKey); view.Tree.Visibility = Visibility.Collapsed;
            }
        }
        bool switched = !ReferenceEquals(view, active);
        if (switched)
        {
            CaptureExpansion(active); active.Filter = filter.Text;
            synchronizing++;
            try { active.Tree.Visibility = Visibility.Collapsed; active = view; filter.Text = active.Filter; active.Tree.Visibility = Visibility.Visible; }
            finally { synchronizing--; }
        }
        bool changed = !ReferenceEquals(view.Outline, value) && !view.Outline.HasSameEntries(value);
        if (changed)
        {
            view.Outline = value; UpdateCount++;
            if (view.SelectedKey is { } key && value.Find(key) is null) view.SelectedKey = null;
            ApplyProjection(view);
        }
        else if (switched) { SelectKey(view.SelectedKey); ViewChanged?.Invoke(); }
        return changed || switched;
    }
    /// <summary>Release all previous contexts when a host opens another document. Does not reset lifetime work counters.</summary>
    public void ResetContexts()
    {
        synchronizing++;
        try { treeHost.Children.Clear(); contexts.Clear(); filter.Text = ""; active = CreateView("default"); }
        finally { synchronizing--; }
        ViewChanged?.Invoke();
    }
    public bool FocusSearch() { bool focused = filter.Focus(FocusState.Programmatic); if (focused) filter.SelectAll(); return focused; }
    public void SelectKey(string? key)
    {
        active.SelectedKey = key;
        var node = key is not null && active.Projection.ContainsKey(key) && active.Nodes.TryGetValue(key, out var found) ? found : null;
        if (ReferenceEquals(active.Tree.SelectedNode, node)) return;
        synchronizing++;
        try { active.Tree.SelectedNode = node; }
        finally { synchronizing--; }
    }
    public IEnumerable<KeyValuePair<string, FrameworkElement>> Elements()
    {
        foreach (var entry in controls) yield return entry;
        foreach (var entry in active.Projection)
            if (active.Nodes.TryGetValue(entry.Key, out var node) && active.Tree.ContainerFromNode(node) is FrameworkElement container)
                yield return new("outline-" + entry.Key, container);
    }
    private static void CaptureExpansion(View view)
    { if (!view.Filtering) foreach (var entry in view.Nodes) view.Expansion[entry.Key] = entry.Value.IsExpanded; }
    private void SetExpanded(bool value)
    {
        synchronizing++;
        try
        {
            foreach (var node in active.Nodes.Values) node.IsExpanded = value;
            if (!active.Filtering) foreach (var key in active.Nodes.Keys) active.Expansion[key] = value;
        }
        finally { synchronizing--; }
        ViewChanged?.Invoke();
    }
    private void ApplyProjection(View view)
    {
        var projection = new DesignerOutlineProjection(view.Outline, view.Filter);
        CaptureExpansion(view); view.Projection = projection; view.Filtering = projection.Query.Length != 0;
        synchronizing++;
        try
        {
            var desired = new Dictionary<TreeViewNode, List<TreeViewNode>>(); var roots = new List<TreeViewNode>();
            foreach (var entry in view.Outline.Entries)
            {
                if (!view.Nodes.TryGetValue(entry.Key, out var node))
                { node = new TreeViewNode(); view.Nodes.Add(entry.Key, node); view.Keys.Add(node, entry.Key); CreatedNodeCount++; }
                if (!Equals(node.Content, entry.Label))
                {
                    node.Content = entry.Label;
                    if (view.Tree.ContainerFromNode(node) is TreeViewItem row) row.Content = entry.Label;
                }
                desired[node] = [];
            }
            foreach (var entry in projection)
            {
                var node = view.Nodes[entry.Key];
                if (entry.ParentKey is null) roots.Add(node); else desired[view.Nodes[entry.ParentKey]].Add(node);
            }
            var changes = new List<(IList<TreeViewNode> Current, List<TreeViewNode> Desired)>();
            if (!view.Tree.RootNodes.SequenceEqual(roots)) changes.Add((view.Tree.RootNodes, roots));
            foreach (var node in view.Nodes.Values)
            {
                var children = desired.GetValueOrDefault(node) ?? [];
                if (!node.Children.SequenceEqual(children)) changes.Add((node.Children, children));
            }
            foreach (var change in changes) change.Current.Clear();
            foreach (var change in changes) foreach (var node in change.Desired) change.Current.Add(node);
            foreach (var key in view.Nodes.Keys.Where(k => view.Outline.Find(k) is null).ToArray()) { view.Keys.Remove(view.Nodes[key]); view.Nodes.Remove(key); view.Expansion.Remove(key); }
            foreach (var entry in view.Nodes) entry.Value.IsExpanded = view.Filtering || view.Expansion.GetValueOrDefault(entry.Key, true);
            SelectKey(view.SelectedKey);
        }
        finally { synchronizing--; }
        ViewChanged?.Invoke();
    }
}
