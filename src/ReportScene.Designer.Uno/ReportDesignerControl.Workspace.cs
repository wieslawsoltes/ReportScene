using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReportScene.Controls.Uno;

namespace ReportScene.Designer.Uno;

public sealed partial class ReportDesignerControl
{
    private readonly Grid rightPane = new(), treePane = new(), propertyPane = new();
    private readonly PaneSplitter sideSplitter = new() { Minimum = 180, Maximum = 640, Reverse = true, Width = 4 };
    private readonly PaneSplitter treeSplitter = new() { Minimum = 96, Maximum = 480, Orientation = Orientation.Horizontal, Height = 4 };
    private readonly TextBlock propertyHelpTitle = ControlTheme.Label("Properties", 11, true);
    private readonly TextBlock propertyHelp = new() { Text = "Select a property to see its description.", FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox propertyFilter = new() { PlaceholderText = "Filter properties…", FontSize = 11, MinHeight = 0, Height = 24, Padding = new(5, 1, 3, 1), CornerRadius = new(0), MaxLength = 256 };
    private bool showTree = true, showProperties = true, adjustingPanes;
    private double sidebarWidth = 280, treeHeight = 210, sidebarBeforeDrag = 280, treeBeforeDrag = 210;
    private DesignerPaneLayout visiblePaneLayout;
    private DesignerUnit displayUnit = DesignerUnit.Points;
    private void BuildTitleBar()
    {
        var bar = new Grid { Background = ControlTheme.Brush("#E9E9E9"), Padding = new(4, 0, 7, 0) };
        bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); bar.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var quick = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        quick.Children.Add(AsyncCommand("quick-save", "Save report", "save", SaveAsync, text: false)); quick.Children.Add(Command("quick-undo", "Undo", "undo", Session.Undo, text: false)); quick.Children.Add(Command("quick-redo", "Redo", "redo", Session.Redo, text: false));
        quick.Children.Add(AsyncCommand("quick-options", "Designer options", "settings", DesignerOptionsAsync, text: false));
        foreach (var button in quick.Children.OfType<Button>()) { button.Height = 25; button.Padding = new(5, 1, 5, 1); }
        bar.Children.Add(quick); title.HorizontalAlignment = HorizontalAlignment.Center; title.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(title, 1); bar.Children.Add(title);
        var brand = ControlTheme.Label("ReportScene", 10); Grid.SetColumn(brand, 2); bar.Children.Add(brand); AddRoot(bar, 0);
    }
    private void BuildTabBar()
    {
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, Background = ControlTheme.Brush("#F0F0F0") };
        var file = Command("file-menu", "File", "", () => { }); file.Foreground = ControlTheme.Brush("#FFFFFF"); file.Background = ControlTheme.Brush("#2777AD"); file.Width = 56;
        if (file.Content is StackPanel body) foreach (var label in body.Children.OfType<TextBlock>()) label.Foreground = ControlTheme.Brush("#FFFFFF");
        var menu = new MenuFlyout();
        Menu(menu, "New blank report", () => Run(NewAsync)); Menu(menu, "Open report…", () => Run(OpenAsync)); Menu(menu, "Save report", () => Run(SaveAsync)); Menu(menu, "Save FRX subset…", () => Run(SaveFrxAsync));
        menu.Items.Add(new MenuFlyoutSeparator());
        var samples = new MenuFlyoutSubItem { Text = "Open sample" };
        foreach (var name in AllSampleNames) { var item = new MenuFlyoutItem { Text = name }; item.Click += (_, _) => Run(() => LoadAnySampleAsync(name)); samples.Items.Add(item); }
        menu.Items.Add(samples); menu.Items.Add(new MenuFlyoutSeparator()); Menu(menu, "Print report…", () => Run(PrintAsync));
        Menu(menu, "Designer options…", () => Run(DesignerOptionsAsync)); Menu(menu, "Compatibility and diagnostics", () => Run(ShowCurrentCompatibilityAsync));
        file.Flyout = menu; tabs.Children.Add(file);
        foreach (var name in new[] { "Home", "Report", "Layout", "View" }) tabs.Children.Add(Command("tab-" + name.ToLowerInvariant(), name, "", () => { activeRibbon = name; if (previewMode) SetPreview(false); BuildRibbon(); UpdateTabSelection(); }));
        tabs.Children.Add(Command("tab-preview", "Preview", "", PreparePreview));
        foreach (var button in tabs.Children.OfType<Button>()) { button.Height = 25; button.Padding = new(12, 1, 12, 1); }
        AddRoot(tabs, 1);
    }
    private Grid PaneCaption(string id, string text, Action close)
    {
        var header = new Grid { Background = ControlTheme.Brush("#ECECEC"), BorderBrush = ControlTheme.Brush("#CBCBCB"), BorderThickness = new(0, 0, 0, 1) };
        header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var label = ControlTheme.Label(text, 11, true); label.Margin = new(6, 0, 0, 0); header.Children.Add(label);
        var button = Command(id, "Close " + text, "delete", close, text: false); button.Width = 23; button.Height = 21; button.Padding = new(5); Grid.SetColumn(button, 1); header.Children.Add(button);
        return header;
    }
    private void BuildWorkspace()
    {
        workspace.ColumnDefinitions.Add(new() { Width = new(33) }); workspace.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        workspace.ColumnDefinitions.Add(new() { Width = new(4) }); workspace.ColumnDefinitions.Add(new() { Width = new(sidebarWidth) });
        var tools = new StackPanel { Spacing = 1, Padding = new(1, 3, 1, 3), Background = ControlTheme.Brush("#F0F0F0") };
        tools.Children.Add(Command("tool-select", "Select objects", "pointer", () => DesignView.Focus(FocusState.Programmatic), text: false));
        foreach (var (kind, label, icon) in new[] { ("text", "Text", "text"), ("line", "Line", "line"), ("rectangle", "Rectangle", "rectangle"), ("ellipse", "Ellipse", "ellipse"), ("image", "Picture", "image"), ("checkbox", "Check box", "checkbox"), ("barcode", "Barcode", "barcode"), ("chart", "Chart", "chart"), ("table", "Table", "table"), ("matrix", "Matrix", "table") })
            tools.Children.Add(Command("insert-" + kind, "Insert " + label, icon, () => Insert(kind), text: false));
        tools.Children.Add(Command("insert-subreport", "Insert subreport and page", "report", InsertSubreport, text: false));
        tools.Children.Add(AsyncCommand("subreport-edit", "Edit selected subreport", "settings", EditSelectedSubreportAsync, text: false));
        tools.Children.Add(Command("subreport-open", "Open subreport page", "open", OpenSelectedSubreport, text: false));
        tools.Children.Add(Command("insert-band", "Insert band", "band", () => Run(AddBandAsync), text: false));
        foreach (var button in tools.Children.OfType<Button>()) { button.Width = 30; button.Height = 25; button.Padding = new(5, 3, 5, 3); }
        workspace.Children.Add(new ScrollViewer { Content = tools, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var center = new Grid(); center.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); center.RowDefinitions.Add(new() { Height = new(25) });
        center.Children.Add(DesignView); center.Children.Add(PreviewView);
        var tabs = new ScrollViewer { Content = pageTabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = ControlTheme.Brush("#E9E9E9") }; Grid.SetRow(tabs, 1); center.Children.Add(tabs);
        Grid.SetColumn(center, 1); workspace.Children.Add(center);
        sideSplitter.Value = preferredSidebarWidth;
        sideSplitter.ValueChanged += value => { if (adjustingPanes) return; preferredSidebarWidth = value; ApplyPaneLayout(); };
        sideSplitter.InteractionStarted += () => { sidebarBeforeDrag = preferredSidebarWidth; preferenceTimer.Stop(); };
        sideSplitter.InteractionCompleted += cancelled => { if (cancelled) preferredSidebarWidth = sidebarBeforeDrag; ApplyPaneLayout(); SchedulePreferenceSave(); };
        sideSplitter.ResetRequested += ResetPaneLayout;
        commands["sidebar-splitter"] = ControlTheme.Identify(sideSplitter, "sidebar-splitter", "Resize tool windows; Left or Right changes width; double-click resets layout"); Grid.SetColumn(sideSplitter, 2); workspace.Children.Add(sideSplitter);
        rightPane.RowDefinitions.Add(new() { Height = new(treeHeight) }); rightPane.RowDefinitions.Add(new() { Height = new(4) }); rightPane.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        treePane.RowDefinitions.Add(new() { Height = new(22) }); treePane.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); treePane.RowDefinitions.Add(new() { Height = new(24) });
        treePane.Children.Add(PaneCaption("close-tree", "Data / Report Tree", () => { showTree = false; ApplyPaneLayout(); }));
        Grid.SetRow(tree, 1); treePane.Children.Add(tree);
        treeTabs.Children.Add(Command("tree-data", "Data", "data", () => { activeTree = "Data"; RebuildTree(); })); treeTabs.Children.Add(Command("tree-report", "Report Tree", "report", () => { activeTree = "Report"; RebuildTree(); }));
        foreach (var button in treeTabs.Children.OfType<Button>()) { button.Height = 24; button.Padding = new(7, 1, 7, 1); }
        Grid.SetRow(treeTabs, 2); treePane.Children.Add(treeTabs); rightPane.Children.Add(treePane);
        treeSplitter.Value = preferredTreeHeight;
        treeSplitter.ValueChanged += value => { if (adjustingPanes) return; preferredTreeHeight = value; ApplyPaneLayout(); };
        treeSplitter.InteractionStarted += () => { treeBeforeDrag = preferredTreeHeight; preferenceTimer.Stop(); };
        treeSplitter.InteractionCompleted += cancelled => { if (cancelled) preferredTreeHeight = treeBeforeDrag; ApplyPaneLayout(); SchedulePreferenceSave(); };
        treeSplitter.ResetRequested += ResetPaneLayout;
        commands["tree-splitter"] = ControlTheme.Identify(treeSplitter, "tree-splitter", "Resize tree and property panes; Up or Down changes height; double-click resets layout"); Grid.SetRow(treeSplitter, 1); rightPane.Children.Add(treeSplitter);
        BuildPropertyPane(); Grid.SetRow(propertyPane, 2); rightPane.Children.Add(propertyPane);
        sidebar.Child = rightPane; sidebar.BorderThickness = new(1, 0, 0, 0); sidebar.BorderBrush = ControlTheme.Brush("#BFC0C2"); Grid.SetColumn(sidebar, 3); workspace.Children.Add(sidebar);
        workspace.SizeChanged += (_, _) => ConstrainPanes(); ApplyPaneLayout(); InitializePreferenceTracking();
        DesignView.RightTapped += (_, e) =>
        {
            var menu = new MenuFlyout(); Menu(menu, "Edit object…", () => { if (Session.Selection.FirstOrDefault() is { } item) Run(() => EditAnyObjectAsync(item)); });
            Menu(menu, "Open subreport page", OpenSelectedSubreport); Menu(menu, "Cut", () => Run(CutAsync)); Menu(menu, "Copy", () => Run(CopyAsync)); Menu(menu, "Paste", () => Run(PasteAsync)); Menu(menu, "Delete", Session.DeleteSelection);
            menu.Items.Add(new MenuFlyoutSeparator()); Menu(menu, "Bring to front", () => Session.Reorder(true)); Menu(menu, "Send to back", () => Session.Reorder(false));
            Menu(menu, "Lock objects", () => Session.SetSelectionLocked(true)); Menu(menu, "Unlock objects", () => Session.SetSelectionLocked(false));
            menu.ShowAt(DesignView, e.GetPosition(DesignView)); e.Handled = true;
        };
    }
    private void BuildPropertyPane()
    {
        foreach (var height in new[] { new GridLength(22), new GridLength(24), new GridLength(24), new GridLength(24), new GridLength(1, GridUnitType.Star), new GridLength(48) }) propertyPane.RowDefinitions.Add(new() { Height = height });
        propertyPane.Children.Add(PaneCaption("close-properties", "Properties", () => { showProperties = false; ApplyPaneLayout(); }));
        commands["object-selector"] = ControlTheme.Identify(objectSelector, "object-selector", "Selected report object"); Grid.SetRow(objectSelector, 1); propertyPane.Children.Add(objectSelector);
        objectSelector.SelectionChanged += (_, _) => { if (!selectingObject && objectSelector.SelectedIndex >= 0 && objectSelector.SelectedIndex < reportOutline.Entries.Count) Run(() => InvokeOutline(reportOutline.Entries[objectSelector.SelectedIndex])); };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Background = ControlTheme.Brush("#F3F3F3") };
        void Button(string id, string label, Action action) { var button = Command(id, label, "", action); button.Height = 23; button.Padding = new(5, 0, 5, 0); toolbar.Children.Add(button); }
        Button("properties-categorized", "Categories", () => properties.Alphabetical = false); Button("properties-alphabetical", "A–Z", () => properties.Alphabetical = true);
        Button("properties-expand", "+", properties.ExpandAll); Button("properties-collapse", "−", properties.CollapseAll); Button("properties-clear", "×", () => propertyFilter.Text = "");
        Grid.SetRow(toolbar, 2); propertyPane.Children.Add(toolbar);
        commands["property-filter"] = ControlTheme.Identify(propertyFilter, "property-filter", "Filter property names or categories");
        propertyFilter.TextChanged += (_, _) => properties.FilterText = propertyFilter.Text; Grid.SetRow(propertyFilter, 3); propertyPane.Children.Add(propertyFilter);
        var propertyScroll = new ScrollViewer { Content = properties, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = ControlTheme.Brush("#FFFFFF") }; Grid.SetRow(propertyScroll, 4); propertyPane.Children.Add(propertyScroll);
        var description = new StackPanel { Spacing = 1, Margin = new(6, 3, 6, 3) }; description.Children.Add(propertyHelpTitle); description.Children.Add(propertyHelp);
        var help = new Border { Child = description, Background = ControlTheme.Brush("#F3F3F3"), BorderBrush = ControlTheme.Brush("#C8C8C8"), BorderThickness = new(0, 1, 0, 0) }; Grid.SetRow(help, 5); propertyPane.Children.Add(help);
        properties.DescriptionChanged += (name, text) => { propertyHelpTitle.Text = name; propertyHelp.Text = text; ToolTipService.SetToolTip(help, text); };
    }
    private void ApplyPaneLayout()
    {
        if (adjustingPanes) return;
        adjustingPanes = true;
        try
        {
            var preferred = new DesignerPreferences { SidebarWidth = preferredSidebarWidth, TreeHeight = preferredTreeHeight, ShowTree = showTree, ShowProperties = showProperties };
            visiblePaneLayout = DesignerPaneLayout.Calculate(preferred, workspace.ActualWidth, workspace.ActualHeight);
            var layout = visiblePaneLayout;
            sidebarWidth = layout.SidebarWidth; treeHeight = layout.TreeHeight;
            sideSplitter.Maximum = layout.MaximumSidebarWidth; sideSplitter.Value = Math.Min(preferredSidebarWidth, layout.MaximumSidebarWidth);
            treeSplitter.Maximum = layout.MaximumTreeHeight; treeSplitter.Value = Math.Min(preferredTreeHeight, layout.MaximumTreeHeight);
            sidebar.Visibility = layout.HasSidebar ? Visibility.Visible : Visibility.Collapsed; sideSplitter.Visibility = sidebar.Visibility;
            workspace.ColumnDefinitions[2].Width = new(layout.HasSidebar ? 4 : 0); workspace.ColumnDefinitions[3].Width = new(sidebarWidth);
            treePane.Visibility = layout.ShowTree ? Visibility.Visible : Visibility.Collapsed;
            propertyPane.Visibility = layout.ShowProperties ? Visibility.Visible : Visibility.Collapsed;
            treeSplitter.Visibility = layout.HasInnerDivider ? Visibility.Visible : Visibility.Collapsed;
            rightPane.RowDefinitions[0].Height = !layout.ShowTree ? new(0) : layout.ShowProperties ? new(treeHeight) : new(1, GridUnitType.Star);
            rightPane.RowDefinitions[1].Height = new(layout.HasInnerDivider ? 4 : 0);
            rightPane.RowDefinitions[2].Height = layout.ShowProperties ? new(1, GridUnitType.Star) : new(0);
        }
        finally { adjustingPanes = false; }
        NotifyState();
    }
    private void ConstrainPanes() => ApplyPaneLayout();
    private void ToggleSidebar() { bool show = !(showTree || showProperties); showTree = showProperties = show; ApplyPaneLayout(); }
    private void ResetPaneLayout()
    { showTree = showProperties = true; preferredSidebarWidth = 280; preferredTreeHeight = 210; ApplyPaneLayout(); }
}
