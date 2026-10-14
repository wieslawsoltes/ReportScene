using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReportScene.Controls.Uno;
using ReportScene.Engine;
using ReportScene.Model;
using ReportScene.Rendering.Skia;
using ReportScene.Samples;
using ReportScene.Serialization;

namespace ReportScene.Designer.Uno;

/// <summary>An embeddable report designer. Document mutations use DesignerSession; preview and exports share prepared output.</summary>
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
    private readonly ReportOutlineView tree = new();
    private readonly PropertySheet properties = new();
    private readonly TextBlock title = ControlTheme.Label("ReportScene", 11);
    private readonly TextBlock status = ControlTheme.Label("Ready", 11);
    private readonly TextBlock objectName = ControlTheme.Label("Properties", 11);
    private readonly TextBox messages = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 11, BorderThickness = new(0), Padding = new(8, 4, 8, 4), Background = ControlTheme.Brush("#FFFFFF") };
    private readonly Dictionary<string, FrameworkElement> commands = new(StringComparer.Ordinal);
    private readonly DispatcherTimer recoveryTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly RecoverySaveCoordinator recovery = new();
    private readonly DesignerActivityTracker activity;
    private string activeRibbon = "Home", activeTree = "Data";
    private bool refreshingProperties, disposed, previewMode;
    private long statusGeneration;
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
        DesignView.ViewChanged += ViewChanged; PreviewView.ViewChanged += ViewChanged;
        DesignView.InteractionCompleted += () => SelectedChanged(this, EventArgs.Empty);
        DesignView.FormatPainter.StateChanged += FormatPainterChanged;
        tree.Invoked += entry => Run(() => InvokeOutline(entry)); tree.ViewChanged += NotifyState;
        properties.ViewChanged += NotifyState;
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        RequestedTheme = ElementTheme.Light; FontSize = 11;
        root.Background = ControlTheme.Brush("#F3F3F3");
        foreach (var height in new[] { new GridLength(27), new GridLength(25), new GridLength(86), new GridLength(1, GridUnitType.Star), new GridLength(0), new GridLength(24) }) root.RowDefinitions.Add(new() { Height = height });
        BuildTitleBar(); BuildTabBar();
        var ribbonScroll = new ScrollViewer { Content = ribbon, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled };
        AddRoot(ControlTheme.Panel(ribbonScroll, "#FAFAFA"), 2);
        BuildWorkspace(); AddRoot(workspace, 3); AddRoot(messages, 4); BuildStatusBar();
        Content = root;
        Session.Changed += SessionChanged; Session.SelectionChanged += SelectedChanged;
        recoveryTimer.Tick += async (_, _) =>
        {
            recoveryTimer.Stop(); if (disposed) return;
            try
            {
                await recovery.FlushWhenReadyAsync(() => !disposed && !Session.HasActiveDrag,
                    () => ReportJson.Serialize(Session.Document), json => host.SaveRecoveryAsync(json));
                if (!disposed && recovery.HasPending) recoveryTimer.Start();
            }
            catch (Exception e) { if (!disposed) SetStatus("Recovery save failed: " + e.Message); }
        };
        KeyDown += HandleShortcuts;
        BuildRibbon(); RefreshStructure(true); RefreshPropertyEditors(); UpdateStatus();
    }
    private void ViewChanged() { SyncStatusZoom(); NotifyState(); }
    private void AddRoot(UIElement element, int row) { Grid.SetRow(element, row); root.Children.Add(element); }
    private Button Command(string id, string label, string icon, Action action, bool large = false, bool text = true)
    {
        var button = ControlTheme.Button(id, label, icon, () => Run(action), large, text);
        if (large) button.Height = 61;
        commands[id] = button; return button;
    }
    private Button AsyncCommand(string id, string label, string icon, Func<Task> action, bool large = false, bool text = true)
        => Command(id, label, icon, () => Run(action), large, text);
    private static void Menu(MenuFlyout menu, string label, Action action) { var item = new MenuFlyoutItem { Text = label }; item.Click += (_, _) => action(); menu.Items.Add(item); }
    private static StackPanel Vertical(params UIElement[] children) { var stack = new StackPanel { Spacing = 1 }; foreach (var child in children) stack.Children.Add(child); return stack; }
    private static StackPanel Horizontal(params UIElement[] children) { var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 }; foreach (var child in children) stack.Children.Add(child); return stack; }
    private void SetPreview(bool enabled)
    {
        if (enabled) DesignView.FormatPainter.Cancel();
        previewMode = enabled; DesignView.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible; PreviewView.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        UpdateStatus(); UpdateTabSelection(); NotifyState();
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
        var editable = Session.Selection.Where(i => !i.Locked).ToArray(); if (editable.Length == 0) return;
        // A mixed selection becomes uniformly enabled; only an all-enabled selection turns off.
        bool enabled = !editable.All(i => key switch { "bold" => i.Style.Bold, "italic" => i.Style.Italic, _ => i.Style.Underline });
        Session.UpdateSelection("Toggle " + key, i => i with { Style = key switch { "bold" => i.Style with { Bold = enabled }, "italic" => i.Style with { Italic = enabled }, _ => i.Style with { Underline = enabled } } });
    }
    private void Insert(string kind)
    {
        if (previewMode) { SetPreview(false); BuildRibbon(); }
        if (Session.SelectedBand is null) throw new InvalidOperationException("Select a band in the report tree before inserting an object.");
        var band = Session.SelectedBand; double top = Math.Max(0, Math.Min(band.Height - 22, 8)); string source = band.DataSource ?? Session.Document.Data.FirstOrDefault()?.Name ?? "Data";
        var table = Session.Document.Data.FirstOrDefault(t => t.Name.Equals(source, StringComparison.OrdinalIgnoreCase));
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
            prepared = null; searchSession = null; searchHits.Clear(); searchIndex = -1;
            if (PreviewView.Report is not null) PreviewView.Report = null;
            if (previewMode) { SetPreview(false); BuildRibbon(); }
        }
        if (change.RefreshStructure) RefreshStructure();
        if (change.SaveRecovery) { recovery.Request(); recoveryTimer.Stop(); recoveryTimer.Start(); }
        UpdateStatus(); NotifyState();
    }
    private void RefreshPropertyEditors()
    {
        if (Session.HasActiveDrag) return;
        var target = Operations.CaptureTarget();
        properties.CommitGuard = () => Operations.EnsureCurrent(target);
        using var deferred = properties.DeferRefresh();
        RefreshObjectProperties(); SyncObjectSelection(); SyncRibbonSelection();
    }
    private void SelectedChanged(object? sender, EventArgs args)
    {
        if (refreshingProperties) return; refreshingProperties = true;
        long queuedGeneration = statusGeneration;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            refreshingProperties = false; if (disposed) return;
            RefreshPropertyEditors();
            // An older queued property refresh must not erase a newer save outcome or warning.
            UpdateStatus(preserveMessage: statusGeneration != queuedGeneration); NotifyState();
        })) refreshingProperties = false;
    }
    private void UpdateStatus() => UpdateStatus(preserveMessage: false);
    private void UpdateStatus(bool preserveMessage)
    {
        title.Text = Session.Document.Name + (Session.IsDirty ? " *" : "") + " — Report Designer";
        if (commands.TryGetValue("quick-undo", out var undo) && undo is Control u) u.IsEnabled = Session.CanUndo;
        if (commands.TryGetValue("quick-redo", out var redo) && redo is Control r) r.IsEnabled = Session.CanRedo;
        if (commands.TryGetValue("matrix-edit", out var matrix) && matrix is Control m) m.IsEnabled = Session.Selection.OfType<MatrixItem>().Any(i => !i.Locked);
        SyncStatusZoom();
        if (preserveMessage) return;
        if (DesignView.FormatPainter.IsActive)
        {
            status.Text = $"Format Painter · {DesignView.FormatPainter.SourceName} · click an unlocked object · Escape to cancel"
                + (DesignView.FormatPainter.IsRepeating ? " · multiple objects" : "");
            return;
        }
        var count = Session.SelectedIds.Count;
        status.Text = previewMode ? $"Preview · page {PreviewView.PageIndex + 1} of {PreviewView.Report?.Pages.Count ?? 0} · Uno / Skia" : $"{Session.Page.Name} · {(count == 0 ? Session.SelectedBand?.Name ?? "No selection" : count + " object(s) selected")} · {DesignerGeometry.UnitLabel(displayUnit)} · Uno / Skia";
    }
    private void SetStatus(string text) { statusGeneration++; status.Text = text; NotifyState(); }
    private void ToggleMessages() => root.RowDefinitions[4].Height = new(root.RowDefinitions[4].Height.Value == 0 ? 105 : 0);
    private void ShowDiagnostics(IEnumerable<Diagnostic> source)
    {
        diagnostics.Clear(); diagnostics.AddRange(source);
        messages.Text = diagnostics.Count == 0 ? "No diagnostics." : string.Join('\n', diagnostics.Select(d => $"{d.Severity} {d.Code}: {d.Message}"));
        if (diagnostics.Count > 0) root.RowDefinitions[4].Height = new(105); NotifyState();
    }
    private void ShowError(Exception error)
    {
        if (disposed) return;
        if (error is ReportPreparationException preparation) ShowDiagnostics(preparation.Diagnostics);
        else ShowDiagnostics([new("RSUI001", error.Message, ObjectId: error is ReportRenderException rendering ? rendering.ObjectId : null)]);
        SetStatus(error.Message);
    }
}
