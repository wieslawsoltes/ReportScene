using System.Globalization;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ReportScene.Controls.Uno;
using ReportScene.Data;
using ReportScene.Engine;
using ReportScene.Export;
using ReportScene.Model;
using ReportScene.Rendering.Skia;
using ReportScene.Samples;
using ReportScene.Serialization;
using Windows.Foundation;
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
    private string activeRibbon = "Home", activeTree = "Data", structuralKey = "", clipboard = "";
    private bool refreshingProperties, disposed, previewMode, savingRecovery;
    private PreparedReport? prepared;
    private List<SearchHit> searchHits = [];
    private int searchIndex = -1;
    private string searchTerm = "";
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
        Session = new(document ?? SampleReports.Invoice());
        DesignView = new(Session, renderer); PreviewView = new(renderer) { Visibility = Visibility.Collapsed };
        DesignView.EditRequested += item => Run(() => EditObjectAsync(item));
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
        recoveryTimer.Tick += async (_, _) => { recoveryTimer.Stop(); if (savingRecovery) return; savingRecovery = true; try { await host.SaveRecoveryAsync(ReportJson.Serialize(Session.Document)); } catch (Exception e) { SetStatus("Recovery save failed: " + e.Message); } finally { savingRecovery = false; } };
        KeyDown += HandleShortcuts;
        BuildRibbon(); RefreshStructure(true); RefreshProperties(); UpdateStatus();
    }
    private void AddRoot(UIElement element, int row) { Grid.SetRow(element, row); root.Children.Add(element); }
    private Button Command(string id, string label, string icon, Action action, bool large = false, bool text = true)
    { var button = ControlTheme.Button(id, label, icon, () => Run(action), large, text); commands[id] = button; return button; }
    private Button AsyncCommand(string id, string label, string icon, Func<Task> action, bool large = false, bool text = true)
        => Command(id, label, icon, () => Run(action), large, text);
    private void BuildTitleBar()
    {
        var bar = new Grid { Background = ControlTheme.Brush("#E4E8EC"), Padding = new(5, 0, 8, 0) };
        bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); bar.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var quick = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        quick.Children.Add(AsyncCommand("quick-save", "Save report", "save", SaveAsync, text: false)); quick.Children.Add(Command("quick-undo", "Undo", "undo", Session.Undo, text: false)); quick.Children.Add(Command("quick-redo", "Redo", "redo", Session.Redo, text: false));
        bar.Children.Add(quick); title.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumn(title, 1); bar.Children.Add(title);
        var brand = ControlTheme.Label("ReportScene", 11, true); Grid.SetColumn(brand, 2); bar.Children.Add(brand); AddRoot(bar, 0);
    }
    private void BuildTabBar()
    {
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, Background = ControlTheme.Brush("#E4E8EC") };
        var file = Command("file-menu", "File", "", () => { }); file.Foreground = ControlTheme.Brush("#FFFFFF"); file.Background = ControlTheme.Brush("#276BA2"); file.Width = 62;
        var menu = new MenuFlyout();
        Menu(menu, "New blank report", () => Run(NewAsync)); Menu(menu, "Open report…", () => Run(OpenAsync)); Menu(menu, "Save report", () => Run(SaveAsync)); Menu(menu, "Save FRX subset…", () => Run(SaveFrxAsync));
        menu.Items.Add(new MenuFlyoutSeparator());
        var samples = new MenuFlyoutSubItem { Text = "Open sample" };
        foreach (var name in SampleReports.Names) { var item = new MenuFlyoutItem { Text = name }; item.Click += (_, _) => Run(() => LoadSampleAsync(name)); samples.Items.Add(item); }
        menu.Items.Add(samples); menu.Items.Add(new MenuFlyoutSeparator()); Menu(menu, "Print report…", () => Run(PrintAsync)); Menu(menu, "Compatibility and diagnostics", () => Run(ShowCompatibilityAsync));
        file.Flyout = menu; tabs.Children.Add(file);
        foreach (var name in new[] { "Home", "Report", "Layout", "View" }) tabs.Children.Add(Command("tab-" + name.ToLowerInvariant(), name, "", () => { activeRibbon = name; if (previewMode) SetPreview(false); BuildRibbon(); }));
        tabs.Children.Add(Command("tab-preview", "Preview", "", PreparePreview)); AddRoot(tabs, 1);
    }
    private static void Menu(MenuFlyout menu, string label, Action action) { var item = new MenuFlyoutItem { Text = label }; item.Click += (_, _) => action(); menu.Items.Add(item); }
    private void BuildWorkspace()
    {
        workspace.ColumnDefinitions.Add(new() { Width = new(39) }); workspace.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); workspace.ColumnDefinitions.Add(new() { Width = new(302) });
        var tools = new StackPanel { Spacing = 1, Padding = new(3, 4, 3, 4), Background = ControlTheme.Brush("#E5E8EB") };
        tools.Children.Add(Command("tool-select", "Select objects", "pointer", () => { DesignView.Focus(FocusState.Programmatic); }, text: false));
        foreach (var (kind, label, icon) in new[] { ("text", "Text", "text"), ("line", "Line", "line"), ("rectangle", "Rectangle", "rectangle"), ("ellipse", "Ellipse", "ellipse"), ("image", "Picture", "image"), ("checkbox", "Check box", "checkbox"), ("barcode", "Barcode", "barcode"), ("chart", "Chart", "chart"), ("table", "Table", "table") })
            tools.Children.Add(Command("insert-" + kind, "Insert " + label, icon, () => Insert(kind), text: false));
        tools.Children.Add(Command("insert-band", "Insert band", "band", () => Run(AddBandAsync), text: false)); workspace.Children.Add(tools);
        var center = new Grid(); center.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); center.RowDefinitions.Add(new() { Height = new(28) });
        center.Children.Add(DesignView); center.Children.Add(PreviewView);
        var tabs = new ScrollViewer { Content = pageTabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = ControlTheme.Brush("#E3E6E9") }; Grid.SetRow(tabs, 1); center.Children.Add(tabs);
        Grid.SetColumn(center, 1); workspace.Children.Add(center);
        var right = new Grid(); right.RowDefinitions.Add(new() { Height = new(30) }); right.RowDefinitions.Add(new() { Height = new(210) }); right.RowDefinitions.Add(new() { Height = new(30) }); right.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        treeTabs.Children.Add(Command("tree-data", "Data", "data", () => { activeTree = "Data"; RebuildTree(); })); treeTabs.Children.Add(Command("tree-report", "Report tree", "report", () => { activeTree = "Report"; RebuildTree(); })); right.Children.Add(ControlTheme.Panel(treeTabs, "#E7EAED"));
        Grid.SetRow(tree, 1); right.Children.Add(tree);
        var propertyHeader = ControlTheme.Panel(objectName, "#E7EAED", new(8, 4, 4, 4)); Grid.SetRow(propertyHeader, 2); right.Children.Add(propertyHeader);
        var propertyScroll = new ScrollViewer { Content = properties, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = ControlTheme.Brush("#FFFFFF") }; Grid.SetRow(propertyScroll, 3); right.Children.Add(propertyScroll);
        sidebar.Child = right; sidebar.BorderThickness = new(1, 0, 0, 0); sidebar.BorderBrush = ControlTheme.Brush("#BFC7CF"); Grid.SetColumn(sidebar, 2); workspace.Children.Add(sidebar);
        DesignView.RightTapped += (_, e) =>
        {
            var menu = new MenuFlyout(); Menu(menu, "Edit object…", () => { if (Session.Selection.FirstOrDefault() is { } item) Run(() => EditObjectAsync(item)); }); Menu(menu, "Cut", () => Run(CutAsync)); Menu(menu, "Copy", () => Run(CopyAsync)); Menu(menu, "Paste", () => Run(PasteAsync)); Menu(menu, "Delete", Session.DeleteSelection); menu.Items.Add(new MenuFlyoutSeparator()); Menu(menu, "Bring to front", () => Session.Reorder(true)); Menu(menu, "Send to back", () => Session.Reorder(false)); menu.ShowAt(DesignView, e.GetPosition(DesignView)); e.Handled = true;
        };
    }
    private void Group(string title, params UIElement[] children)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var child in children) content.Children.Add(child);
        var group = new Grid { Padding = new(5, 2, 5, 0), BorderThickness = new(0, 0, 1, 0), BorderBrush = ControlTheme.Brush("#D1D5D9"), MinWidth = 55 };
        group.RowDefinitions.Add(new() { Height = new(67) }); group.RowDefinitions.Add(new() { Height = new(17) }); group.Children.Add(content);
        var label = ControlTheme.Label(title, 10); label.Foreground = ControlTheme.Brush("#636E78"); label.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetRow(label, 1); group.Children.Add(label); ribbon.Children.Add(group);
    }
    private static StackPanel Vertical(params UIElement[] children) { var stack = new StackPanel { Spacing = 1 }; foreach (var child in children) stack.Children.Add(child); return stack; }
    private static StackPanel Horizontal(params UIElement[] children) { var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 }; foreach (var child in children) stack.Children.Add(child); return stack; }
    private void BuildRibbon()
    {
        ribbon.Children.Clear();
        if (previewMode) { BuildPreviewRibbon(); UpdateStatus(); return; }
        switch (activeRibbon)
        {
            case "Home":
                Group("Clipboard", AsyncCommand("paste", "Paste", "paste", PasteAsync, true), Vertical(AsyncCommand("cut", "Cut", "cut", CutAsync), AsyncCommand("copy", "Copy", "copy", CopyAsync)));
                var font = new ComboBox { Width = 154, Height = 28, MinHeight = 0, FontSize = 11, Padding = new(6, 2, 25, 2), ItemsSource = new[] { "Arial", "Calibri", "Times New Roman", "Courier New", "Open Sans" }, SelectedIndex = 0 };
                font.SelectionChanged += (_, _) => { if (font.SelectedItem is string family) Run(() => Session.UpdateSelection("Font family", i => i with { Style = i.Style with { FontFamily = family } })); };
                var size = new ComboBox { Width = 66, Height = 28, MinHeight = 0, FontSize = 11, Padding = new(5, 2, 20, 2), ItemsSource = new[] { "8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36", "48" }, SelectedIndex = 2 };
                size.SelectionChanged += (_, _) => { if (size.SelectedItem is string text && double.TryParse(text, out var n)) Run(() => Session.UpdateSelection("Font size", i => i with { Style = i.Style with { FontSize = n } })); };
                commands["font-family"] = ControlTheme.Identify(font, "font-family", "Font family"); commands["font-size"] = ControlTheme.Identify(size, "font-size", "Font size");
                Group("Text", Vertical(Horizontal(font, size), Horizontal(Command("bold", "Bold", "", () => ToggleStyle("bold")), Command("italic", "Italic", "", () => ToggleStyle("italic")), Command("underline", "Underline", "", () => ToggleStyle("underline")))));
                Group("Alignment", Vertical(Horizontal(Command("text-left", "Left", "left", () => TextAlignment(TextAlign.Left), text: false), Command("text-center", "Center", "center", () => TextAlignment(TextAlign.Center), text: false), Command("text-right", "Right", "right", () => TextAlignment(TextAlign.Right), text: false)), Command("vertical-center", "Middle", "center", () => Session.UpdateSelection("Vertical alignment", i => i with { Style = i.Style with { VerticalAlignment = Model.VerticalAlign.Center } }))));
                Group("Border & fill", Vertical(AsyncCommand("fill-color", "Fill color", "shape", () => ColorAsync(false)), AsyncCommand("text-color", "Text color", "text", () => ColorAsync(true))), Command("border", "Border", "rectangle", () => Session.UpdateSelection("Toggle border", i => i with { Style = i.Style with { BorderWidth = i.Style.BorderWidth > 0 ? 0 : 1 } }), true));
                Group("Styles", Vertical(Command("style-heading", "Heading", "text", () => ApplyStyle(new() { FontSize = 16, Bold = true, Foreground = "#244F75" })), Command("style-body", "Body", "text", () => ApplyStyle(new() { FontSize = 10 }))));
                Group("Report", Command("preview", "Preview", "preview", PreparePreview, true), AsyncCommand("save", "Save", "save", SaveAsync, true));
                break;
            case "Report":
                Group("Prepare", Command("preview-report", "Preview", "preview", PreparePreview, true), Command("validate-report", "Validate", "checkbox", Validate, true));
                Group("Data", AsyncCommand("import-data", "Import data", "data", ImportDataAsync, true), Vertical(AsyncCommand("parameters", "Parameters…", "settings", ParametersAsync), AsyncCommand("data-editor", "Data editor…", "table", EditDataAsync)));
                Group("Report pages", AsyncCommand("page-setup", "Page setup", "settings", PageSetupAsync, true), Vertical(Command("new-page", "New page", "new", Session.AddPage), AsyncCommand("add-band", "Add band…", "band", AddBandAsync)));
                Group("Bands", Vertical(AsyncCommand("group-wizard", "Group data…", "band", GroupAsync), Command("delete-band", "Delete band", "delete", Session.DeleteBand)));
                Group("Document", AsyncCommand("native-editor", "Edit JSON", "report", EditNativeAsync, true), AsyncCommand("open", "Open", "open", OpenAsync, true));
                break;
            case "Layout":
                Group("Align objects", Vertical(Command("align-left", "Align left", "left", () => Session.Align(AlignmentCommand.Left)), Command("align-right", "Align right", "right", () => Session.Align(AlignmentCommand.Right))), Vertical(Command("align-top", "Align top", "left", () => Session.Align(AlignmentCommand.Top)), Command("align-bottom", "Align bottom", "right", () => Session.Align(AlignmentCommand.Bottom))), Vertical(Command("align-center", "Center", "center", () => Session.Align(AlignmentCommand.Center)), Command("align-middle", "Middle", "center", () => Session.Align(AlignmentCommand.Middle))));
                Group("Size & spacing", Vertical(Command("same-width", "Same width", "rectangle", () => Session.Align(AlignmentCommand.SameWidth)), Command("same-height", "Same height", "rectangle", () => Session.Align(AlignmentCommand.SameHeight))), Vertical(Command("space-horizontal", "Horizontal spacing", "table", () => Session.Align(AlignmentCommand.DistributeHorizontal)), Command("space-vertical", "Vertical spacing", "table", () => Session.Align(AlignmentCommand.DistributeVertical))));
                Group("Order", Command("bring-front", "Bring to front", "front", () => Session.Reorder(true), true), Command("send-back", "Send to back", "back", () => Session.Reorder(false), true));
                Group("Selection", Vertical(Command("select-all", "Select all", "pointer", Session.SelectAll), Command("delete", "Delete", "delete", Session.DeleteSelection)));
                break;
            case "View":
                var grid = new CheckBox { Content = "Show grid", IsChecked = DesignView.ShowGrid, FontSize = 11, MinHeight = 28 }; grid.Checked += (_, _) => DesignView.ShowGrid = true; grid.Unchecked += (_, _) => DesignView.ShowGrid = false;
                var snap = new CheckBox { Content = "Snap to grid", IsChecked = Session.SnapToGrid, FontSize = 11, MinHeight = 28 }; snap.Checked += (_, _) => Session.SnapToGrid = true; snap.Unchecked += (_, _) => Session.SnapToGrid = false;
                Group("Grid", Vertical(grid, snap)); Group("Zoom", ZoomChoice(false), Command("fit-width", "Fit width", "zoom", DesignView.FitWidth, true));
                Group("Panels", Vertical(Command("toggle-sidebar", "Data and properties", "table", () => { bool visible = sidebar.Visibility == Visibility.Visible; sidebar.Visibility = visible ? Visibility.Collapsed : Visibility.Visible; workspace.ColumnDefinitions[2].Width = new(visible ? 0 : 302); }), Command("toggle-messages", "Messages", "report", ToggleMessages)));
                Group("Samples", SamplesChoice()); Group("Help", AsyncCommand("compatibility", "Compatibility", "report", ShowCompatibilityAsync, true));
                break;
        }
        UpdateStatus();
    }
    private UIElement ZoomChoice(bool preview)
    {
        var choice = new ComboBox { Width = 91, MinHeight = 28, Height = 28, Padding = new(6, 2, 25, 2), FontSize = 11 };
        foreach (var n in new[] { 25, 50, 75, 85, 100, 125, 150, 200, 300, 400 }) choice.Items.Add(n + "%");
        choice.SelectedItem = Math.Round((preview ? PreviewView.Zoom : DesignView.Zoom) * 100).ToString(CultureInfo.InvariantCulture) + "%";
        choice.SelectionChanged += (_, _) => { if (choice.SelectedItem is string text && double.TryParse(text.TrimEnd('%'), out var n)) { if (preview) PreviewView.Zoom = n / 100; else DesignView.Zoom = n / 100; UpdateStatus(); } };
        commands[preview ? "preview-zoom" : "design-zoom"] = choice; return choice;
    }
    private UIElement SamplesChoice()
    {
        var choice = new ComboBox { Width = 182, MinHeight = 28, Height = 28, Padding = new(6, 2, 25, 2), FontSize = 11, PlaceholderText = "Open sample…" };
        foreach (var name in SampleReports.Names) choice.Items.Add(name);
        choice.SelectionChanged += (_, _) => { if (choice.SelectedItem is string name) Run(() => LoadSampleAsync(name)); }; commands["samples"] = choice; return choice;
    }
    private void BuildPreviewRibbon()
    {
        Group("Designer", Command("back-to-design", "Design", "report", () => { SetPreview(false); BuildRibbon(); }, true));
        var page = new TextBox { Text = (PreviewView.PageIndex + 1).ToString(), Width = 40, MinHeight = 26, Height = 26, Padding = new(5, 2, 3, 2), FontSize = 11 };
        page.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter && int.TryParse(page.Text, out var n)) { PreviewView.PageIndex = n - 1; UpdateStatus(); e.Handled = true; } }; commands["preview-page"] = page;
        Group("Pages", Vertical(Horizontal(Command("previous-page", "Previous page", "previous", () => { PreviewView.PageIndex--; page.Text = (PreviewView.PageIndex + 1).ToString(); UpdateStatus(); }, text: false), page, Command("next-page", "Next page", "next", () => { PreviewView.PageIndex++; page.Text = (PreviewView.PageIndex + 1).ToString(); UpdateStatus(); }, text: false)), ControlTheme.Label("of " + (prepared?.Pages.Count ?? 0) + " pages", 10)));
        Group("Zoom", ZoomChoice(true), Command("preview-fit-width", "Fit width", "zoom", PreviewView.FitWidth, true));
        var search = new TextBox { PlaceholderText = "Find in report", Text = searchTerm, Width = 167, MinHeight = 28, Height = 28, FontSize = 11, Padding = new(6, 2, 4, 2) };
        search.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { Find(search.Text); e.Handled = true; } }; commands["preview-search"] = ControlTheme.Identify(search, "preview-search", "Find in report");
        Group("Find", Vertical(search, Command("find-next", "Find next", "preview", () => Find(search.Text))));
        var export = AsyncCommand("export-pdf", "PDF", "export", () => ExportAsync(ExportFormat.Pdf), true);
        var formats = Command("export-formats", "More formats", "export", () => { }, true); var menu = new MenuFlyout(); foreach (var format in Enum.GetValues<ExportFormat>()) Menu(menu, format.ToString(), () => Run(() => ExportAsync(format))); formats.Flyout = menu;
        Group("Export", export, formats, AsyncCommand("print", "Print", "print", PrintAsync, true));
    }
    private void SetPreview(bool enabled)
    {
        previewMode = enabled; DesignView.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible; PreviewView.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed; UpdateStatus(); NotifyState();
    }
    private void PreparePreview()
    {
        var report = Prepare(); prepared = report; PreviewView.Report = report; SetPreview(true); BuildRibbon(); SetStatus($"Prepared {report.Pages.Count} page(s) in {report.Elapsed.TotalMilliseconds:0} ms · Uno / Skia renderer");
    }
    private PreparedReport Prepare()
    {
        diagnostics.Clear();
        try { var report = new ReportPreparer(renderer.Metrics).Prepare(Session.Document); ShowDiagnostics(report.Diagnostics); return report; }
        catch (ReportPreparationException e) { ShowDiagnostics(e.Diagnostics); throw; }
    }
    private void Validate() { var p = Prepare(); SetStatus($"Validation passed · {p.Pages.Count} prepared page(s) · {p.Pages.Sum(page => page.Items.Count)} drawing objects"); }
    private void Find(string term)
    {
        if (prepared is null) return;
        if (term != searchTerm) { searchTerm = term; searchHits = prepared.Search(term).ToList(); searchIndex = -1; }
        if (searchHits.Count == 0) { PreviewView.Highlight(null); SetStatus("No matches"); return; }
        searchIndex = (searchIndex + 1) % searchHits.Count; PreviewView.Highlight(searchHits[searchIndex]); SetStatus($"Match {searchIndex + 1} of {searchHits.Count} · page {searchHits[searchIndex].Page}");
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
            _ => throw new ArgumentException("Unknown object type.")
        };
        Session.Add(item); DesignView.ScrollToSelection(); DesignView.Focus(FocusState.Programmatic);
    }
    private void SessionChanged(object? sender, EventArgs args)
    {
        prepared = null; RefreshStructure(); UpdateStatus(); recoveryTimer.Stop(); recoveryTimer.Start(); NotifyState();
    }
    private void SelectedChanged(object? sender, EventArgs args)
    {
        if (refreshingProperties) return; refreshingProperties = true;
        DispatcherQueue.TryEnqueue(() => { refreshingProperties = false; if (!disposed) { RefreshProperties(); UpdateStatus(); NotifyState(); } });
    }
    private void RefreshStructure(bool force = false)
    {
        var key = string.Join('|', Session.Document.Pages.Select(p => p.Name)) + ":" + string.Join('|', DocumentTree.AllBands(Session.Document).Select(b => b.Id + b.Name + string.Join(',', b.Items.Select(i => i.Id + i.Name)))) + ":" + string.Join('|', Session.Document.Data.Select(t => t.Name + t.Rows.Count + string.Join(',', t.Columns)));
        if (!force && key == structuralKey) return; structuralKey = key;
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
        foreach (var id in new[] { "quick-undo" }) if (commands.TryGetValue(id, out var undo) && undo is Control u) u.IsEnabled = Session.CanUndo;
        foreach (var id in new[] { "quick-redo" }) if (commands.TryGetValue(id, out var redo) && redo is Control r) r.IsEnabled = Session.CanRedo;
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
    public void Dispose()
    {
        if (disposed) return; disposed = true; recoveryTimer.Stop(); Session.Changed -= SessionChanged; Session.SelectionChanged -= SelectedChanged; DesignView.Dispose(); renderer.Dispose();
    }
}
