using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReportScene.Controls.Uno;
using ReportScene.Export;
using ReportScene.Model;
using Windows.System;

namespace ReportScene.Designer.Uno;

public sealed partial class ReportDesignerControl
{
    private ReportPageNavigator? pageNavigator;
    private bool previewNavigationBound;
    private void SyncPreviewNavigator()
    {
        if (disposed || pageNavigator is null) return;
        var position = PreviewView.Position; bool changed = pageNavigator.Position != position;
        pageNavigator.SetPosition(position.Count, position.Index);
        if (commands.TryGetValue("preview-outline", out var control) && control is Button button)
            button.Background = ControlTheme.Brush(PreviewView.OutlineVisible ? "#CDE6F7" : "#00FFFFFF");
        if (previewMode && changed) UpdateStatus();
    }
    private void BuildPreviewRibbon()
    {
        Group("View", Vertical(Command("back-to-design", "Design", "report", () => { SetPreview(false); BuildRibbon(); }),
            Command("preview-outline", "Outline", "report", () => PreviewView.OutlineVisible = !PreviewView.OutlineVisible)));
        if (!previewNavigationBound)
        { PreviewView.ViewChanged += SyncPreviewNavigator; previewNavigationBound = true; }
        var navigator = new ReportPageNavigator();
        // Keep only a weak identity token. Removed controls can remain in the command
        // registry until rebuilt; their callbacks must not retain invalidated report pages.
        var boundReport = PreviewView.Report is { } report ? new WeakReference<PreparedReport>(report) : null;
        pageNavigator = navigator;
        navigator.PageRequested += index => Run(() =>
        {
            if (!previewMode || !ReferenceEquals(pageNavigator, navigator) || boundReport is null
                || !boundReport.TryGetTarget(out var target) || !ReferenceEquals(PreviewView.Report, target)) return;
            PreviewView.PageIndex = index;
        });
        navigator.Failed += ShowError;
        navigator.SetPosition(PreviewView.Position.Count, PreviewView.PageIndex, resetEditor: true);
        foreach (var pair in navigator.Elements) commands[pair.Key] = pair.Value;
        commands["preview-paper"] = PreviewView.CanvasElement;
        commands["preview-viewport"] = PreviewView.ViewportElement;
        Group("Pages", navigator);
        Group("Zoom", ZoomChoice(true), Vertical(Command("preview-fit-width", "Fit width", "zoom", PreviewView.FitWidth), Command("preview-fit-page", "Whole page", "report", PreviewView.FitPage)));
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
        var formats = Command("export-formats", "More formats", "export", () => { }); var menu = new MenuFlyout(); foreach (var format in Enum.GetValues<ExportFormat>()) Menu(menu, format.ToString(), () => Run(() => ExportAsync(format))); formats.Flyout = menu;
        Group("Export", export, Vertical(AsyncCommand("export-options", "Export options…", "settings", ExportOptionsAsync), formats), AsyncCommand("print", "Print", "print", PrintAsync, true));
        SyncPreviewNavigator();
    }
}
