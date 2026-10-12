using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using ReportScene.Controls.Uno;
using ReportScene.Model;

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
                var font = new ReportComboBox { Width = 151, Height = 25, MinHeight = 0, FontSize = 11, Padding = new(5, 0, 0, 0), CornerRadius = new(0) };
                foreach (var family in new[] { "Arial", "Calibri", "Times New Roman", "Courier New", "Open Sans" }) font.Items.Add(family);
                font.SelectionChanged += (_, _) => { if (!updatingRibbon && font.SelectedItem is string family) Run(() => Session.UpdateSelection("Font family", i => i with { Style = i.Style with { FontFamily = family } })); };
                var size = new ReportComboBox { Width = 62, Height = 25, MinHeight = 0, FontSize = 11, Padding = new(5, 0, 0, 0), CornerRadius = new(0) };
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
                var snap = new CheckBox { Content = "Snap to grid", IsChecked = Session.SnapToGrid, FontSize = 11, MinHeight = 26 };
                snap.Checked += (_, _) => { Session.SnapToGrid = true; NotifyState(); };
                snap.Unchecked += (_, _) => { Session.SnapToGrid = false; NotifyState(); };
                commands["show-grid"] = ControlTheme.Identify(grid, "show-grid", "Show grid"); commands["snap-grid"] = ControlTheme.Identify(snap, "snap-grid", "Snap to grid");
                var rulers = new CheckBox { Content = "Rulers", IsChecked = DesignView.ShowRulers, FontSize = 11, MinHeight = 26 }; rulers.Checked += (_, _) => DesignView.ShowRulers = true; rulers.Unchecked += (_, _) => DesignView.ShowRulers = false;
                commands["show-rulers"] = ControlTheme.Identify(rulers, "show-rulers", "Show rulers");
                var units = new ReportComboBox { Width = 122, MinHeight = 0, Height = 25, FontSize = 11, Padding = new(5, 0, 0, 0), CornerRadius = new(0), ItemsSource = Enum.GetNames<DesignerUnit>(), SelectedIndex = (int)displayUnit };
                units.SelectionChanged += (_, _) => { if (units.SelectedItem is string name) { displayUnit = Enum.Parse<DesignerUnit>(name); DesignView.Unit = displayUnit; RefreshPropertyEditors(); UpdateStatus(); } };
                commands["display-units"] = ControlTheme.Identify(units, "display-units", "Display units; report geometry is stored in points");
                var fullPage = new CheckBox { Content = "Full paper", IsChecked = DesignView.ShowFullPage, FontSize = 11, MinHeight = 26 };
                fullPage.Checked += (_, _) => DesignView.ShowFullPage = true; fullPage.Unchecked += (_, _) => DesignView.ShowFullPage = false;
                commands["show-full-paper"] = ControlTheme.Identify(fullPage, "show-full-paper", "Show full paper height instead of compact bands");
                Group("Grid", Vertical(grid, snap)); Group("Rulers", Vertical(rulers, fullPage), units); Group("Zoom", ZoomChoice(false), Command("fit-width", "Fit width", "zoom", DesignView.FitWidth, true));
                Group("Panels", Vertical(Command("toggle-tree", "Data / Report Tree", "data", () => { showTree = !showTree; ApplyPaneLayout(); }), Command("toggle-properties", "Properties", "settings", () => { showProperties = !showProperties; ApplyPaneLayout(); })), Vertical(Command("toggle-sidebar", "Tool windows", "table", ToggleSidebar), Command("toggle-messages", "Messages", "report", ToggleMessages)), Command("reset-layout", "Reset layout", "report", ResetPaneLayout, true));
                Group("Samples", SamplesChoice());
                Group("Settings", Vertical(AsyncCommand("designer-options", "Options…", "settings", DesignerOptionsAsync), AsyncCommand("compatibility", "Compatibility", "report", ShowCurrentCompatibilityAsync)));
                break;
        }
        SyncRibbonSelection(); UpdateStatus(); UpdateTabSelection();
    }
    private UIElement ZoomChoice(bool preview)
    {
        var choice = new ReportComboBox { Width = 88, MinHeight = 0, Height = 25, Padding = new(5, 0, 0, 0), FontSize = 11, CornerRadius = new(0) };
        foreach (var n in new[] { 25, 50, 75, 85, 100, 125, 150, 200, 300, 400 }) choice.Items.Add(n + "%");
        choice.SelectedItem = Math.Round((preview ? PreviewView.Zoom : DesignView.Zoom) * 100).ToString(CultureInfo.InvariantCulture) + "%";
        choice.SelectionChanged += (_, _) => { if (choice.SelectedItem is string text && double.TryParse(text.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) { if (preview) PreviewView.Zoom = n / 100; else DesignView.Zoom = n / 100; UpdateStatus(); } };
        commands[preview ? "preview-zoom" : "design-zoom"] = ControlTheme.Identify(choice, preview ? "preview-zoom" : "design-zoom", "Zoom"); return choice;
    }
    private UIElement SamplesChoice()
    {
        var choice = new ReportComboBox { Width = 174, MinHeight = 0, Height = 25, Padding = new(5, 0, 0, 0), FontSize = 11, CornerRadius = new(0), PlaceholderText = "Open sample…" };
        foreach (var name in AllSampleNames) choice.Items.Add(name);
        choice.SelectionChanged += (_, _) => { if (choice.SelectedItem is string name) Run(() => LoadAnySampleAsync(name)); }; commands["samples"] = choice; return choice;
    }
}
