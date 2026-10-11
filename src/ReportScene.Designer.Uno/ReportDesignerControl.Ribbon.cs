using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using ReportScene.Controls.Uno;
using ReportScene.Export;
using ReportScene.Model;
using Windows.System;

namespace ReportScene.Designer.Uno;

public sealed partial class ReportDesignerControl
{
    private bool updatingRibbon;
    private void Group(string title, params UIElement[] children)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var child in children) content.Children.Add(child);
        var group = new Grid { Padding = new(5, 1, 5, 0), BorderThickness = new(0, 0, 1, 0), BorderBrush = ControlTheme.Brush("#D7D7D7"), MinWidth = 48 };
        group.RowDefinitions.Add(new() { Height = new(63) }); group.RowDefinitions.Add(new() { Height = new(17) }); group.Children.Add(content);
        var label = ControlTheme.Label(title, 10); label.Foreground = ControlTheme.Brush("#606060"); label.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetRow(label, 1); group.Children.Add(label); ribbon.Children.Add(group);
    }
    private ToggleButton FormatButton(string id, string name, string glyph)
    {
        var label = new TextBlock { Text = glyph, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            FontWeight = id == "bold" ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
            FontStyle = id == "italic" ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
            TextDecorations = id == "underline" ? Windows.UI.Text.TextDecorations.Underline : Windows.UI.Text.TextDecorations.None };
        var button = new ToggleButton { Content = label, Width = 28, Height = 25, MinWidth = 0, MinHeight = 0, Padding = new(1), BorderThickness = new(0), CornerRadius = new(0), IsThreeState = false };
        commands[id] = ControlTheme.Identify(button, id, name); ToolTipService.SetToolTip(button, name);
        button.Click += (_, _) => { if (!updatingRibbon) Run(() => { try { ToggleStyle(id); } finally { SyncRibbonSelection(); } }); };
        return button;
    }
    private void SyncRibbonSelection()
    {
        if (updatingRibbon || Session.HasActiveDrag) return;
        var selected = Session.Selection.ToArray(); bool editable = selected.Any(i => !i.Locked); var first = selected.FirstOrDefault();
        updatingRibbon = true;
        try
        {
            void Choice(string id, string? value)
            {
                if (!commands.TryGetValue(id, out var element) || element is not ComboBox choice || !choice.IsLoaded && activeRibbon != "Home") return;
                choice.IsEnabled = editable;
                if (value is not null && !choice.Items.Contains(value))
                {
                    // Keep custom selected values visible without accumulating an unbounded font list.
                    if (choice.Items.Count > 32) choice.Items.RemoveAt(choice.Items.Count - 1);
                    choice.Items.Add(value);
                }
                choice.SelectedItem = value; choice.PlaceholderText = selected.Length > 1 ? "Mixed" : "";
            }
            Choice("font-family", first is not null && selected.All(i => i.Style.FontFamily == first.Style.FontFamily) ? first.Style.FontFamily : null);
            Choice("font-size", first is not null && selected.All(i => i.Style.FontSize == first.Style.FontSize) ? first.Style.FontSize.ToString("0.###", CultureInfo.InvariantCulture) : null);
            foreach (var (id, name, predicate) in new (string, string, Func<ItemStyle, bool>)[] { ("bold", "Bold", s => s.Bold), ("italic", "Italic", s => s.Italic), ("underline", "Underline", s => s.Underline) })
            {
                if (!commands.TryGetValue(id, out var element) || element is not ToggleButton button) continue;
                bool? value = first is null ? false : selected.All(i => predicate(i.Style) == predicate(first.Style)) ? predicate(first.Style) : null;
                button.IsEnabled = editable; button.IsChecked = value;
                button.Background = ControlTheme.Brush(value == true ? "#CDE6F7" : value is null ? "#E3E3E3" : "#00FFFFFF");
                ToolTipService.SetToolTip(button, name + (value is null ? " (mixed selection)" : ""));
            }
            foreach (var (id, align) in new[] { ("text-left", TextAlign.Left), ("text-center", TextAlign.Center), ("text-right", TextAlign.Right) })
                if (commands.TryGetValue(id, out var element) && element is Button button)
                { button.IsEnabled = editable; button.Background = ControlTheme.Brush(first is not null && selected.All(i => i.Style.Alignment == align) ? "#CDE6F7" : "#00FFFFFF"); }
        }
        finally { updatingRibbon = false; }
    }
    private void BuildRibbon()
    {
        ribbon.Children.Clear();
        if (previewMode) { BuildPreviewRibbon(); UpdateStatus(); UpdateTabSelection(); return; }
        switch (activeRibbon)
        {
            case "Home":
                Group("Clipboard", AsyncCommand("paste", "Paste", "paste", PasteAsync, true), Vertical(AsyncCommand("cut", "Cut", "cut", CutAsync), AsyncCommand("copy", "Copy", "copy", CopyAsync)));
                var font = new ComboBox { Width = 151, Height = 25, MinHeight = 0, FontSize = 11, Padding = new(5, 0, 22, 0), CornerRadius = new(0) };
                foreach (var family in new[] { "Arial", "Calibri", "Times New Roman", "Courier New", "Open Sans" }) font.Items.Add(family);
                font.SelectionChanged += (_, _) => { if (!updatingRibbon && font.SelectedItem is string family) Run(() => Session.UpdateSelection("Font family", i => i with { Style = i.Style with { FontFamily = family } })); };
                var size = new ComboBox { Width = 62, Height = 25, MinHeight = 0, FontSize = 11, Padding = new(5, 0, 20, 0), CornerRadius = new(0) };
                foreach (var value in new[] { "8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36", "48" }) size.Items.Add(value);
                size.SelectionChanged += (_, _) => { if (!updatingRibbon && size.SelectedItem is string text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) Run(() => Session.UpdateSelection("Font size", i => i with { Style = i.Style with { FontSize = n } })); };
                commands["font-family"] = ControlTheme.Identify(font, "font-family", "Font family"); commands["font-size"] = ControlTheme.Identify(size, "font-size", "Font size in points");
                Group("Text", Vertical(Horizontal(font, size), Horizontal(FormatButton("bold", "Bold", "B"), FormatButton("italic", "Italic", "I"), FormatButton("underline", "Underline", "U"),
                    Command("text-left", "Align text left", "left", () => TextAlignment(TextAlign.Left), text: false), Command("text-center", "Center text", "center", () => TextAlignment(TextAlign.Center), text: false), Command("text-right", "Align text right", "right", () => TextAlignment(TextAlign.Right), text: false))));
                Group("Paragraph", Vertical(Command("vertical-top", "Top", "left", () => Session.UpdateSelection("Vertical alignment", i => i with { Style = i.Style with { VerticalAlignment = Model.VerticalAlign.Top } })), Command("vertical-center", "Middle", "center", () => Session.UpdateSelection("Vertical alignment", i => i with { Style = i.Style with { VerticalAlignment = Model.VerticalAlign.Center } }))), Command("vertical-bottom", "Bottom", "right", () => Session.UpdateSelection("Vertical alignment", i => i with { Style = i.Style with { VerticalAlignment = Model.VerticalAlign.Bottom } })));
                Group("Border & fill", Vertical(AsyncCommand("fill-color", "Fill color", "shape", () => ColorAsync(false)), AsyncCommand("text-color", "Text color", "text", () => ColorAsync(true))), Command("border", "Border", "rectangle", () => Session.UpdateSelection("Toggle border", i => i with { Style = i.Style with { BorderWidth = i.Style.BorderWidth > 0 ? 0 : 1 } }), true));
                Group("Styles", Vertical(Command("style-heading", "Heading", "text", () => ApplyStyle(new() { FontSize = 16, Bold = true, Foreground = "#244F75" })), Command("style-body", "Body", "text", () => ApplyStyle(new() { FontSize = 10 }))));
                Group("Report", Command("preview", "Preview", "preview", PreparePreview, true), AsyncCommand("save", "Save", "save", SaveAsync, true), AsyncCommand("matrix-edit", "Edit matrix", "table", EditSelectedMatrixAsync, true));
                break;
            case "Report":
                Group("Prepare", Command("preview-report", "Preview", "preview", PreparePreview, true), Command("validate-report", "Validate", "checkbox", Validate, true));
                Group("Data", AsyncCommand("import-data", "Import data", "data", ImportDataWithXmlAsync, true), Vertical(AsyncCommand("parameters", "Parameters…", "settings", ParametersAsync), AsyncCommand("data-editor", "Data editor…", "table", EditDataAsync)));
                Group("Report pages", AsyncCommand("page-setup", "Page setup", "settings", PageSetupAsync, true), Vertical(Command("new-page", "New page", "new", Session.AddPage), AsyncCommand("add-band", "Add band…", "band", AddBandAsync)));
                Group("Bands", Vertical(AsyncCommand("group-wizard", "Group data…", "band", GroupAsync), Command("delete-band", "Delete band", "delete", Session.DeleteBand)));
                Group("Document", AsyncCommand("native-editor", "Edit JSON", "report", EditNativeAsync, true), AsyncCommand("open", "Open", "open", OpenAsync, true));
                break;
            case "Layout":
                Group("Align objects", Vertical(Command("align-left", "Align left", "left", () => Session.Align(AlignmentCommand.Left)), Command("align-right", "Align right", "right", () => Session.Align(AlignmentCommand.Right))), Vertical(Command("align-top", "Align top", "left", () => Session.Align(AlignmentCommand.Top)), Command("align-bottom", "Align bottom", "right", () => Session.Align(AlignmentCommand.Bottom))), Vertical(Command("align-center", "Center", "center", () => Session.Align(AlignmentCommand.Center)), Command("align-middle", "Middle", "center", () => Session.Align(AlignmentCommand.Middle))));
                Group("Size & spacing", Vertical(Command("same-width", "Same width", "rectangle", () => Session.Align(AlignmentCommand.SameWidth)), Command("same-height", "Same height", "rectangle", () => Session.Align(AlignmentCommand.SameHeight))), Vertical(Command("space-horizontal", "Horizontal spacing", "table", () => Session.Align(AlignmentCommand.DistributeHorizontal)), Command("space-vertical", "Vertical spacing", "table", () => Session.Align(AlignmentCommand.DistributeVertical))));
                Group("Order", Command("bring-front", "Bring to front", "front", () => Session.Reorder(true), true), Command("send-back", "Send to back", "back", () => Session.Reorder(false), true));
                Group("Selection", Vertical(Command("select-all", "Select all", "pointer", Session.SelectAll), Command("delete", "Delete", "delete", Session.DeleteSelection)), Vertical(Command("lock-objects", "Lock", "settings", () => Session.SetSelectionLocked(true)), Command("unlock-objects", "Unlock", "settings", () => Session.SetSelectionLocked(false))));
                break;
            case "View":
                var grid = new CheckBox { Content = "Show grid", IsChecked = DesignView.ShowGrid, FontSize = 11, MinHeight = 26 }; grid.Checked += (_, _) => DesignView.ShowGrid = true; grid.Unchecked += (_, _) => DesignView.ShowGrid = false;
                var snap = new CheckBox { Content = "Snap to grid", IsChecked = Session.SnapToGrid, FontSize = 11, MinHeight = 26 }; snap.Checked += (_, _) => Session.SnapToGrid = true; snap.Unchecked += (_, _) => Session.SnapToGrid = false;
                commands["show-grid"] = ControlTheme.Identify(grid, "show-grid", "Show grid"); commands["snap-grid"] = ControlTheme.Identify(snap, "snap-grid", "Snap to grid");
                var rulers = new CheckBox { Content = "Rulers", IsChecked = DesignView.ShowRulers, FontSize = 11, MinHeight = 26 }; rulers.Checked += (_, _) => DesignView.ShowRulers = true; rulers.Unchecked += (_, _) => DesignView.ShowRulers = false;
                commands["show-rulers"] = ControlTheme.Identify(rulers, "show-rulers", "Show rulers");
                var units = new ComboBox { Width = 122, MinHeight = 0, Height = 25, FontSize = 11, Padding = new(5, 0, 22, 0), CornerRadius = new(0), ItemsSource = Enum.GetNames<DesignerUnit>(), SelectedIndex = (int)displayUnit };
                units.SelectionChanged += (_, _) => { if (units.SelectedItem is string name) { displayUnit = Enum.Parse<DesignerUnit>(name); DesignView.Unit = displayUnit; RefreshPropertyEditors(); UpdateStatus(); } };
                commands["display-units"] = ControlTheme.Identify(units, "display-units", "Display units; report geometry is stored in points");
                var fullPage = new CheckBox { Content = "Full paper", IsChecked = DesignView.ShowFullPage, FontSize = 11, MinHeight = 26 };
                fullPage.Checked += (_, _) => DesignView.ShowFullPage = true; fullPage.Unchecked += (_, _) => DesignView.ShowFullPage = false;
                commands["show-full-paper"] = ControlTheme.Identify(fullPage, "show-full-paper", "Show full paper height instead of compact bands");
                Group("Grid", Vertical(grid, snap)); Group("Rulers", Vertical(rulers, fullPage), units); Group("Zoom", ZoomChoice(false), Command("fit-width", "Fit width", "zoom", DesignView.FitWidth, true));
                Group("Panels", Vertical(Command("toggle-tree", "Data / Report Tree", "data", () => { showTree = !showTree; ApplyPaneLayout(); }), Command("toggle-properties", "Properties", "settings", () => { showProperties = !showProperties; ApplyPaneLayout(); })), Vertical(Command("toggle-sidebar", "Tool windows", "table", ToggleSidebar), Command("toggle-messages", "Messages", "report", ToggleMessages)), Command("reset-layout", "Reset layout", "report", ResetPaneLayout, true));
                Group("Samples", SamplesChoice()); Group("Help", AsyncCommand("compatibility", "Compatibility", "report", ShowCurrentCompatibilityAsync, true));
                break;
        }
        SyncRibbonSelection(); UpdateStatus(); UpdateTabSelection();
    }
    private UIElement ZoomChoice(bool preview)
    {
        var choice = new ComboBox { Width = 88, MinHeight = 0, Height = 25, Padding = new(5, 0, 22, 0), FontSize = 11, CornerRadius = new(0) };
        foreach (var n in new[] { 25, 50, 75, 85, 100, 125, 150, 200, 300, 400 }) choice.Items.Add(n + "%");
        choice.SelectedItem = Math.Round((preview ? PreviewView.Zoom : DesignView.Zoom) * 100).ToString(CultureInfo.InvariantCulture) + "%";
        choice.SelectionChanged += (_, _) => { if (choice.SelectedItem is string text && double.TryParse(text.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) { if (preview) PreviewView.Zoom = n / 100; else DesignView.Zoom = n / 100; UpdateStatus(); } };
        commands[preview ? "preview-zoom" : "design-zoom"] = ControlTheme.Identify(choice, preview ? "preview-zoom" : "design-zoom", "Zoom"); return choice;
    }
    private UIElement SamplesChoice()
    {
        var choice = new ComboBox { Width = 174, MinHeight = 0, Height = 25, Padding = new(5, 0, 22, 0), FontSize = 11, CornerRadius = new(0), PlaceholderText = "Open sample…" };
        foreach (var name in AllSampleNames) choice.Items.Add(name);
        choice.SelectionChanged += (_, _) => { if (choice.SelectedItem is string name) Run(() => LoadAnySampleAsync(name)); }; commands["samples"] = choice; return choice;
    }
    private void BuildPreviewRibbon()
    {
        Group("Designer", Command("back-to-design", "Design", "report", () => { SetPreview(false); BuildRibbon(); }, true));
        var page = new TextBox { Text = (PreviewView.PageIndex + 1).ToString(), Width = 40, MinHeight = 26, Height = 26, Padding = new(5, 2, 3, 2), FontSize = 11 };
        page.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter && int.TryParse(page.Text, out var n)) { PreviewView.PageIndex = n - 1; page.Text = (PreviewView.PageIndex + 1).ToString(); UpdateStatus(); e.Handled = true; } }; commands["preview-page"] = page;
        Group("Pages", Vertical(Horizontal(Command("previous-page", "Previous page", "previous", () => { PreviewView.PageIndex--; page.Text = (PreviewView.PageIndex + 1).ToString(); UpdateStatus(); }, text: false), page, Command("next-page", "Next page", "next", () => { PreviewView.PageIndex++; page.Text = (PreviewView.PageIndex + 1).ToString(); UpdateStatus(); }, text: false)), ControlTheme.Label("of " + (prepared?.Pages.Count ?? 0) + " pages", 10)));
        Group("Zoom", ZoomChoice(true), Command("preview-fit-width", "Fit width", "zoom", PreviewView.FitWidth, true));
        var search = new TextBox { PlaceholderText = "Find in report", Text = searchTerm, Width = 180, MinHeight = 28, Height = 28, FontSize = 11, Padding = new(6, 2, 4, 2) };
        search.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Enter) return;
            bool previous = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
            Find(search.Text, previous); e.Handled = true;
        };
        commands["preview-search"] = ControlTheme.Identify(search, "preview-search", "Find in report");
        var matchCase = new CheckBox { Content = "Match case", IsChecked = searchOptions.MatchCase, FontSize = 11, MinHeight = 26 };
        var wholeWord = new CheckBox { Content = "Whole word", IsChecked = searchOptions.WholeWord, FontSize = 11, MinHeight = 26 };
        void OptionsChanged()
        { searchOptions = new(matchCase.IsChecked == true, wholeWord.IsChecked == true); if (!string.IsNullOrEmpty(search.Text)) Find(search.Text); }
        matchCase.Checked += (_, _) => OptionsChanged(); matchCase.Unchecked += (_, _) => OptionsChanged();
        wholeWord.Checked += (_, _) => OptionsChanged(); wholeWord.Unchecked += (_, _) => OptionsChanged();
        commands["find-match-case"] = ControlTheme.Identify(matchCase, "find-match-case", "Match case");
        commands["find-whole-word"] = ControlTheme.Identify(wholeWord, "find-whole-word", "Whole word");
        Group("Find", Vertical(search, Horizontal(Command("find-previous", "Previous", "previous", () => Find(search.Text, true)), Command("find-next", "Next", "next", () => Find(search.Text)))), Vertical(matchCase, wholeWord));
        var export = AsyncCommand("export-pdf", "PDF", "export", () => ExportAsync(ExportFormat.Pdf), true);
        var formats = Command("export-formats", "More formats", "export", () => { }, true); var menu = new MenuFlyout(); foreach (var format in Enum.GetValues<ExportFormat>()) Menu(menu, format.ToString(), () => Run(() => ExportAsync(format))); formats.Flyout = menu;
        Group("Export", export, formats, AsyncCommand("print", "Print", "print", PrintAsync, true));
    }
}
