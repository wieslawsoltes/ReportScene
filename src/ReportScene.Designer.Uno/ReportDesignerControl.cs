using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ReportScene.Controls.Uno;
using ReportScene.Engine;
using ReportScene.Model;
using ReportScene.Rendering.Skia;
using ReportScene.Samples;
using ReportScene.Serialization;
using Windows.System;

namespace ReportScene.Designer.Uno;

/// <summary>An embeddable report designer. All document mutations pass through DesignerSession; preview and exports share the same prepared report.</summary>
public sealed partial class ReportDesignerControl : UserControl, IDisposable
{
    private readonly IReportWorkspaceHost host;
    private readonly ReportRenderer renderer = new();
    private readonly Grid root = new();
    private readonly Grid workspace = new();
    private readonly StackPanel ribbon = new() { Orientation = Orientation.Horizontal, Spacing = 0 };
    private readonly StackPanel pageTabs = new() { Orientation = Orientation.Horizontal, Spacing = 1 };
    private readonly StackPanel treeTabs = new() { Orientation = Orientation.Horizontal, Spacing = 0 };
    private readonly Border sidebar = new();
    private readonly TreeView tree = new() { SelectionMode = TreeViewSelectionMode.Single };
    private readonly PropertySheet properties = new();
    private readonly TextBlock title = ControlTheme.Label("ReportScene", 12, true);
    private readonly TextBlock status = ControlTheme.Label("Ready", 11);
    private readonly TextBlock objectName = ControlTheme.Label("Properties", 12, true);
    private readonly TextBox messages = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 11, BorderThickness = new(0), Padding = new(8, 4, 8, 4), Background = ControlTheme.Brush("#FFFFFF") };
    private readonly Dictionary<string, FrameworkElement> commands = new(StringComparer.Ordinal);
    private readonly Dictionary<TreeViewNode, Action> treeActions = new();
    private readonly DispatcherTimer recoveryTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly RecoverySaveCoordinator recovery = new();
    private readonly DesignerActivityTracker activity;
    private string activeRibbon = "Home", activeTree = "Data", structuralKey = "", clipboard = "";
    private bool refreshingProperties, disposed, previewMode;
    private PreparedReport? prepared;
    private List<SearchHit> searchHits = [];
    private int searchIndex = -1;
    private string searchTerm = "";
    private PreparedSearchSession? searchSession;
    private ReportSearchOptions searchOptions = new();
    private readonly List<Diagnostic> diagnostics = [];
    public DesignerSession Session { get; }
    public ReportDesignView DesignView { get; }
    public ReportPreviewView PreviewView { get; }
    public bool IsPreview => previewMode;
    public PreparedReport? Prepared => prepared;
    public IReadOnlyList<Diagnostic> Diagnostics => diagnostics;
    public event Action? StateChanged;

    public ReportDesignerControl(IReportWorkspaceHost host, ReportDocument? document = null)
    {
        this.host = host;
        Session = new(document ?? SampleReports.Invoice()); activity = new(Session);
        DesignView = new(Session, renderer); PreviewView = new(renderer) { Visibility = Visibility.Collapsed };
        DesignView.EditRequested += item => Run(() => EditAnyObjectAsync(item));
        DesignView.Failed += ShowError; PreviewView.Failed += ShowError; properties.Failed += ShowError;
        DesignView.ViewChanged += NotifyState; PreviewView.ViewChanged += NotifyState;
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        RequestedTheme = ElementTheme.Light; FontSize = 12;
        root.Background = ControlTheme.Brush("#F0F1F2");
        foreach (var height in new[] { new GridLength(29), new GridLength(28), new GridLength(89), new GridLength(1, GridUnitType.Star), new GridLength(0), new GridLength(25) }) root.RowDefinitions.Add(new() { Height = height });
        BuildTitleBar(); BuildTabBar();
        var ribbonScroll = new ScrollViewer { Content = ribbon, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled };
        AddRoot(ControlTheme.Panel(ribbonScroll, "#F6F6F6"), 2);
        BuildWorkspace(); AddRoot(workspace, 3); AddRoot(messages, 4);
        var statusBar = new Grid { Background = ControlTheme.Brush("#276BA2"), Padding = new(9, 1, 9, 1) };
        status.Foreground = ControlTheme.Brush("#FFFFFF"); statusBar.Children.Add(status); AddRoot(statusBar, 5);
        Content = root;
        Session.Changed += SessionChanged; Session.SelectionChanged += SelectedChanged;
        tree.ItemInvoked += (_, args) => { if (args.InvokedItem is TreeViewNode node && treeActions.TryGetValue(node, out var action)) Run(action); };
        recoveryTimer.Tick += async (_, _) =>
        {
            recoveryTimer.Stop(); if (disposed) return;
            try
            {
                await recovery.FlushAsync(() => ReportJson.Serialize(Session.Document), json => host.SaveRecoveryAsync(json));
            }
            catch (Exception e) { if (!disposed) SetStatus("Recovery save failed: " + e.Message); }
        };
        KeyDown += HandleShortcuts;
        BuildRibbon(); RefreshStructure(true); RefreshObjectProperties(); UpdateStatus();
    }
    private void AddRoot(UIElement element, int row) { Grid.SetRow(element, row); root.Children.Add(element); }
    private Button Command(string id, string label, string icon, Action action, bool large = false, bool text = true)
    { var button = ControlTheme.Button(id, label, icon, () => Run(action), large, text); commands[id] = button; return button; }
    private Button AsyncCommand(string id, string label, string icon, Func<Task> action, bool large = false, bool text = true)
        => Command(id, label, icon, () => Run(action), large, text);
    private static void Menu(MenuFlyout menu, string label, Action action) { var item = new MenuFlyoutItem { Text = label }; item.Click += (_, _) => action(); menu.Items.Add(item); }
    private static StackPanel Vertical(params UIElement[] children) { var stack = new StackPanel { Spacing = 1 }; foreach (var child in children) stack.Children.Add(child); return stack; }
    private static StackPanel Horizontal(params UIElement[] children) { var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 }; foreach (var child in children) stack.Children.Add(child); return stack; }
    private void SetPreview(bool enabled)
    {
        previewMode = enabled; DesignView.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible; PreviewView.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed; UpdateStatus(); NotifyState();
    }
    private void PreparePreview()
    {
        var report = Prepare(); prepared = report; searchSession = null; searchHits.Clear(); searchIndex = -1;
        PreviewView.Report = report; SetPreview(true); BuildRibbon(); SetStatus($"Prepared {report.Pages.Count} page(s) in {report.Elapsed.TotalMilliseconds:0} ms · Uno / Skia renderer");
    }
    private PreparedReport Prepare()
    {
        diagnostics.Clear();
        try { var report = new ReportPreparer(renderer.Metrics).Prepare(Session.Document); ShowDiagnostics(report.Diagnostics); return report; }
        catch (ReportPreparationException e) { ShowDiagnostics(e.Diagnostics); throw; }
    }
    private void Validate() { var p = Prepare(); SetStatus($"Validation passed · {p.Pages.Count} prepared page(s) · {p.Pages.Sum(page => page.Items.Count)} drawing objects"); }
    private void Find(string term) => Find(term, false);
    private void Find(string term, bool previous)
    {
        if (prepared is null) return;
        searchSession ??= new(prepared); searchSession.SetQuery(term, searchOptions); searchTerm = term;
        var hit = previous ? searchSession.MovePrevious() : searchSession.MoveNext(); searchIndex = searchSession.CurrentIndex;
        if (hit is null) { PreviewView.Highlight(null); SetStatus("No matches"); return; }
        PreviewView.Highlight(hit);
        if (commands.TryGetValue("preview-page", out var element) && element is TextBox page) page.Text = (PreviewView.PageIndex + 1).ToString();
        SetStatus($"Match {searchIndex + 1} of {searchSession.Matches.Count} · page {hit.Page}");
    }
    private void ApplyStyle(ItemStyle style) => Session.UpdateSelection("Apply style", i => i with { Style = style });
    private void TextAlignment(TextAlign alignment) => Session.UpdateSelection("Text alignment", i => i with { Style = i.Style with { Alignment = alignment } });
    private void ToggleStyle(string key)
    {
        var first = Session.Selection.FirstOrDefault(); if (first is null) return;
        Session.UpdateSelection("Toggle " + key, i => i with { Style = key switch { "bold" => i.Style with { Bold = !first.Style.Bold }, "italic" => i.Style with { Italic = !first.Style.Italic }, _ => i.Style with { Underline = !first.Style.Underline } } });
    }
    private void Insert(string kind)
    {
        if (previewMode) { SetPreview(false); BuildRibbon(); }
        if (Session.SelectedBand is null) throw new InvalidOperationException("Select a band in the report tree before inserting an object.");
        var band = Session.SelectedBand; double top = Math.Max(0, Math.Min(band.Height - 22, 8)); string source = band.DataSource ?? Session.Document.Data.FirstOrDefault()?.Name ?? "Data";
        var table = Session.Document.Data.FirstOrDefault(t => t.Name == source);
        ReportItem item = kind switch
        {
            "text" => new TextItem { Name = "Text", Text = "Text", Bounds = new(8, top, 140, 24) },
            "line" => new ShapeItem { Name = "Line", Shape = ShapeKind.Line, Bounds = new(8, top, 150, 1), Style = new() { BorderWidth = 1, BorderColor = "#000000" } },
            "rectangle" => new ShapeItem { Name = "Rectangle", Bounds = new(8, top, 120, 45), Style = new() { BorderWidth = 1 } },
            "ellipse" => new ShapeItem { Name = "Ellipse", Shape = ShapeKind.Ellipse, Bounds = new(8, top, 100, 45), Style = new() { BorderWidth = 1 } },
            "image" => new ImageItem { Name = "Picture", Bounds = new(8, top, 120, 80) },
            "checkbox" => new CheckBoxItem { Name = "CheckBox", Bounds = new(8, top, 18, 18) },
            "barcode" => new BarcodeItem { Name = "Barcode", Bounds = new(8, top, 160, 50) },
            "chart" => new ChartItem { Name = "Chart", DataSource = source, CategoryColumn = table?.Columns.FirstOrDefault() ?? "Name", ValueExpression = table?.Columns.LastOrDefault() ?? "Value", Bounds = new(8, top, 320, 210) },
            "table" => new TableItem { Name = "Table", DataSource = source, Columns = table?.Columns.Take(5).Select(c => new TableColumn(c, "[" + c + "]")).ToList() ?? [new("Column", "Value")], Bounds = new(8, top, 360, 110) },
            "matrix" => CreateMatrix(source, table, top),
            _ => throw new ArgumentException("Unknown object type.")
        };
        Session.Add(item); DesignView.ScrollToSelection(); DesignView.Focus(FocusState.Programmatic);
    }
    private void SessionChanged(object? sender, EventArgs args)
    {
        var change = activity.Observe(Session);
        if (change.InvalidatePrepared)
        {
            prepared = null; searchSession = null; searchHits.Clear(); searchIndex = -1; PreviewView.Report = null;
            if (previewMode) { SetPreview(false); BuildRibbon(); }
        }
        // Geometry-only drag notifications must not walk and concatenate the whole report tree.
        if (change.RefreshStructure) RefreshStructure();
        if (change.SaveRecovery) { recovery.Request(); recoveryTimer.Stop(); recoveryTimer.Start(); }
        UpdateStatus(); NotifyState();
    }
    private void SelectedChanged(object? sender, EventArgs args)
    {
        if (refreshingProperties) return; refreshingProperties = true;
        DispatcherQueue.TryEnqueue(() => { refreshingProperties = false; if (!disposed) { RefreshObjectProperties(); UpdateStatus(); NotifyState(); } });
    }
    private void RefreshStructure(bool force = false)
    {
        var key = Session.PageIndex + ":" + string.Join('|', Session.Document.Pages.Select(p => p.Name)) + ":" + string.Join('|', DocumentTree.AllBands(Session.Document).Select(b => b.Id + b.Name + string.Join(',', b.Items.Select(i => i.Id + i.Name)))) + ":" + string.Join('|', Session.Document.Data.Select(t => t.Name + t.Rows.Count + string.Join(',', t.Columns)));
        if (!force && key == structuralKey) return; structuralKey = key;
        foreach (var id in commands.Keys.Where(k => k.StartsWith("page-tab-", StringComparison.Ordinal)).ToArray()) commands.Remove(id);
        pageTabs.Children.Clear();
        for (int n = 0; n < Session.Document.Pages.Count; n++)
        {
            int index = n; var button = Command("page-tab-" + n, Session.Document.Pages[n].Name, "", () => { Session.SetPage(index); SetPreview(false); BuildRibbon(); RefreshStructure(true); });
            button.Background = ControlTheme.Brush(n == Session.PageIndex ? "#FFFFFF" : "#E3E6E9"); button.Padding = new(14, 3, 14, 3); pageTabs.Children.Add(button);
        }
        pageTabs.Children.Add(Command("page-tab-add", "+", "", Session.AddPage)); RebuildTree();
    }
    private void RebuildTree()
    {
        tree.RootNodes.Clear(); treeActions.Clear();
        TreeViewNode Node(string text, Action? action = null) { var node = new TreeViewNode { Content = text, IsExpanded = true }; if (action is not null) treeActions[node] = action; return node; }
        if (activeTree == "Data")
        {
            foreach (var table in Session.Document.Data)
            {
                var rootNode = Node(table.Name + $" ({table.Rows.Count} rows)", () => Run(EditDataAsync));
                foreach (var field in table.Columns) rootNode.Children.Add(Node(field, () => { Session.Add(new TextItem { Name = field.Replace(" ", ""), Text = $"[{table.Name}.{field}]", Bounds = new(8, 6, 150, 22) }); }));
                tree.RootNodes.Add(rootNode);
            }
            var parametersNode = Node("Parameters", () => Run(ParametersAsync)); foreach (var p in Session.Document.Parameters) parametersNode.Children.Add(Node(p.Name, () => Session.Add(new TextItem { Name = p.Name, Text = $"[Parameters.{p.Name}]", Bounds = new(8, 6, 160, 22) }))); tree.RootNodes.Add(parametersNode);
            var system = Node("System variables"); foreach (var name in new[] { "PageN", "TotalPages", "RowNumber", "Date", "Now" }) system.Children.Add(Node(name, () => Session.Add(new TextItem { Name = name, Text = "[" + name + "]" }))); tree.RootNodes.Add(system);
        }
        else
        {
            foreach (var page in Session.Document.Pages)
            {
                int index = Session.Document.Pages.IndexOf(page); var p = Node(page.Name, () => Session.SetPage(index));
                foreach (var band in page.Bands.SelectMany(DocumentTree.Descendants))
                {
                    var b = Node(band.Name + " · " + band.Kind, () => { Session.SetPage(index); Session.SelectBand(band.Id); });
                    foreach (var item in band.Items) b.Children.Add(Node(item.Name, () => { if (Session.PageIndex != index) Session.SetPage(index); Session.Select(item.Id); DesignView.ScrollToSelection(); })); p.Children.Add(b);
                }
                tree.RootNodes.Add(p);
            }
        }
    }
    private void UpdateStatus()
    {
        title.Text = Session.Document.Name + (Session.IsDirty ? " *" : "") + " — Report Designer";
        if (commands.TryGetValue("quick-undo", out var undo) && undo is Control u) u.IsEnabled = Session.CanUndo;
        if (commands.TryGetValue("quick-redo", out var redo) && redo is Control r) r.IsEnabled = Session.CanRedo;
        if (commands.TryGetValue("matrix-edit", out var matrix) && matrix is Control m) m.IsEnabled = Session.Selection.OfType<MatrixItem>().Any(i => !i.Locked);
        var count = Session.SelectedIds.Count;
        status.Text = previewMode ? $"Preview · page {PreviewView.PageIndex + 1} of {PreviewView.Report?.Pages.Count ?? 0} · {PreviewView.Zoom:P0} · Uno / Skia" : $"{Session.Page.Name} · {(count == 0 ? Session.SelectedBand?.Name ?? "No selection" : count + " object(s) selected")} · {DesignView.Zoom:P0} · points (1/72 in) · Uno / Skia";
    }
    private void SetStatus(string text) { status.Text = text; NotifyState(); }
    private void ToggleMessages() => root.RowDefinitions[4].Height = new(root.RowDefinitions[4].Height.Value == 0 ? 105 : 0);
    private void ShowDiagnostics(IEnumerable<Diagnostic> source)
    {
        diagnostics.Clear(); diagnostics.AddRange(source);
        messages.Text = diagnostics.Count == 0 ? "No diagnostics." : string.Join('\n', diagnostics.Select(d => $"{d.Severity} {d.Code}: {d.Message}"));
        if (diagnostics.Count > 0) root.RowDefinitions[4].Height = new(105); NotifyState();
    }
    private void ShowError(Exception error)
    {
        if (error is ReportPreparationException preparation) ShowDiagnostics(preparation.Diagnostics);
        else ShowDiagnostics([new("RSUI001", error.Message, ObjectId: error is ReportRenderException rendering ? rendering.ObjectId : null)]);
        SetStatus(error.Message);
    }
    private void Run(Action action) { try { action(); } catch (Exception e) { ShowError(e); } }
    private async void Run(Func<Task> action) { try { await action(); } catch (Exception e) { ShowError(e); } }
    private void NotifyState() => StateChanged?.Invoke();
    private void HandleShortcuts(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        if (e.Key == VirtualKey.F5) { Run(PreparePreview); e.Handled = true; return; }
        if (!ctrl) return;
        if (e.OriginalSource is TextBox && e.Key is VirtualKey.C or VirtualKey.X or VirtualKey.V or VirtualKey.A or VirtualKey.Z or VirtualKey.Y) return;
        switch (e.Key)
        {
            case VirtualKey.S: Run(SaveAsync); break; case VirtualKey.O: Run(OpenAsync); break; case VirtualKey.N: Run(NewAsync); break;
            case VirtualKey.Z: Session.Undo(); break; case VirtualKey.Y: Session.Redo(); break; case VirtualKey.C: Run(CopyAsync); break; case VirtualKey.X: Run(CutAsync); break; case VirtualKey.V: Run(PasteAsync); break; case VirtualKey.A: Session.SelectAll(); break;
            case VirtualKey.P: Run(PrintAsync); break;
            default: return;
        }
        e.Handled = true;
    }
    public async Task RestoreRecoveryAsync()
    {
        var json = await host.LoadRecoveryAsync(); if (string.IsNullOrWhiteSpace(json)) return;
        try { Session.Load(ReportJson.Deserialize(json)); SetStatus("Restored the most recent local workspace."); } catch (Exception e) { SetStatus("Recovery could not be loaded: " + e.Message); }
    }
    public new void Dispose()
    {
        if (disposed) return; disposed = true; recoveryTimer.Stop(); Session.Changed -= SessionChanged; Session.SelectionChanged -= SelectedChanged;
        searchSession = null; prepared = null; PreviewView.Report = null; commands.Clear(); treeActions.Clear(); DesignView.Dispose(); renderer.Dispose();
    }
}
