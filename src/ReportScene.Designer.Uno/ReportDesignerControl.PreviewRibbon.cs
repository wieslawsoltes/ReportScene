using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReportScene.Controls.Uno;
using ReportScene.Export;
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
        if (previewMode && changed) UpdateStatus();
    }
    private void BuildPreviewRibbon()
    {
        Group("Designer", Command("back-to-design", "Design", "report", () => { SetPreview(false); BuildRibbon(); }, true));
        if (!previewNavigationBound)
        { PreviewView.ViewChanged += SyncPreviewNavigator; previewNavigationBound = true; }
        var navigator = new ReportPageNavigator(); var boundReport = PreviewView.Report; pageNavigator = navigator;
        navigator.PageRequested += index => Run(() =>
        {
            // A late event from a removed ribbon must not navigate another prepared report.
            if (!previewMode || !ReferenceEquals(pageNavigator, navigator) || !ReferenceEquals(PreviewView.Report, boundReport)) return;
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
        var formats = Command("export-formats", "More formats", "export", () => { }, true); var menu = new MenuFlyout(); foreach (var format in Enum.GetValues<ExportFormat>()) Menu(menu, format.ToString(), () => Run(() => ExportAsync(format))); formats.Flyout = menu;
        Group("Export", export, formats, AsyncCommand("print", "Print", "print", PrintAsync, true));
    }
}
