using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReportScene.Controls.Uno;
using ReportScene.Export;
using ReportScene.Model;
using Windows.System;

namespace ReportScene.Designer.Uno;

public sealed partial class ReportDesignerControl
{
    private void Group(string title, params UIElement[] children)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var child in children) content.Children.Add(child);
        var group = new Grid { Padding = new(5, 2, 5, 0), BorderThickness = new(0, 0, 1, 0), BorderBrush = ControlTheme.Brush("#D1D5D9"), MinWidth = 55 };
        group.RowDefinitions.Add(new() { Height = new(67) }); group.RowDefinitions.Add(new() { Height = new(17) }); group.Children.Add(content);
        var label = ControlTheme.Label(title, 10); label.Foreground = ControlTheme.Brush("#636E78"); label.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetRow(label, 1); group.Children.Add(label); ribbon.Children.Add(group);
    }
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
                Group("Report", Command("preview", "Preview", "preview", PreparePreview, true), AsyncCommand("save", "Save", "save", SaveAsync, true), AsyncCommand("matrix-edit", "Edit matrix", "table", EditSelectedMatrixAsync, true));
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
                Group("Samples", SamplesChoice()); Group("Help", AsyncCommand("compatibility", "Compatibility", "report", ShowCurrentCompatibilityAsync, true));
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
        foreach (var name in AllSampleNames) choice.Items.Add(name);
        choice.SelectionChanged += (_, _) => { if (choice.SelectedItem is string name) Run(() => LoadAnySampleAsync(name)); }; commands["samples"] = choice; return choice;
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
}
