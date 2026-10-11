using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReportScene.Controls.Uno;

namespace ReportScene.Designer.Uno;

public sealed partial class ReportDesignerControl
{
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
        foreach (var name in AllSampleNames) { var item = new MenuFlyoutItem { Text = name }; item.Click += (_, _) => Run(() => LoadAnySampleAsync(name)); samples.Items.Add(item); }
        menu.Items.Add(samples); menu.Items.Add(new MenuFlyoutSeparator()); Menu(menu, "Print report…", () => Run(PrintAsync)); Menu(menu, "Compatibility and diagnostics", () => Run(ShowCurrentCompatibilityAsync));
        file.Flyout = menu; tabs.Children.Add(file);
        foreach (var name in new[] { "Home", "Report", "Layout", "View" }) tabs.Children.Add(Command("tab-" + name.ToLowerInvariant(), name, "", () => { activeRibbon = name; if (previewMode) SetPreview(false); BuildRibbon(); }));
        tabs.Children.Add(Command("tab-preview", "Preview", "", PreparePreview)); AddRoot(tabs, 1);
    }
    private void BuildWorkspace()
    {
        workspace.ColumnDefinitions.Add(new() { Width = new(39) }); workspace.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); workspace.ColumnDefinitions.Add(new() { Width = new(302) });
        var tools = new StackPanel { Spacing = 1, Padding = new(3, 4, 3, 4), Background = ControlTheme.Brush("#E5E8EB") };
        tools.Children.Add(Command("tool-select", "Select objects", "pointer", () => { DesignView.Focus(FocusState.Programmatic); }, text: false));
        foreach (var (kind, label, icon) in new[] { ("text", "Text", "text"), ("line", "Line", "line"), ("rectangle", "Rectangle", "rectangle"), ("ellipse", "Ellipse", "ellipse"), ("image", "Picture", "image"), ("checkbox", "Check box", "checkbox"), ("barcode", "Barcode", "barcode"), ("chart", "Chart", "chart"), ("table", "Table", "table"), ("matrix", "Matrix", "table") })
            tools.Children.Add(Command("insert-" + kind, "Insert " + label, icon, () => Insert(kind), text: false));
        tools.Children.Add(Command("insert-subreport", "Insert subreport and page", "report", InsertSubreport, text: false));
        tools.Children.Add(AsyncCommand("subreport-edit", "Edit selected subreport", "settings", EditSelectedSubreportAsync, text: false));
        tools.Children.Add(Command("subreport-open", "Open subreport page", "open", OpenSelectedSubreport, text: false));
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
            var menu = new MenuFlyout(); Menu(menu, "Edit object…", () => { if (Session.Selection.FirstOrDefault() is { } item) Run(() => EditAnyObjectAsync(item)); });
            Menu(menu, "Open subreport page", OpenSelectedSubreport); Menu(menu, "Cut", () => Run(CutAsync)); Menu(menu, "Copy", () => Run(CopyAsync)); Menu(menu, "Paste", () => Run(PasteAsync)); Menu(menu, "Delete", Session.DeleteSelection);
            menu.Items.Add(new MenuFlyoutSeparator()); Menu(menu, "Bring to front", () => Session.Reorder(true)); Menu(menu, "Send to back", () => Session.Reorder(false)); menu.ShowAt(DesignView, e.GetPosition(DesignView)); e.Handled = true;
        };
    }
}
