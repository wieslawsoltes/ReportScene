using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ReportScene.Controls.Uno;
using ReportScene.Export;

namespace ReportScene.Designer.Uno;

public sealed partial class ReportDesignerControl
{
    private bool printDialogOpen;
    private async Task PrintOptionsAsync()
    {
        if (disposed || printDialogOpen) return;
        printDialogOpen = true;
        var registered = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal);
        void Register(string id, FrameworkElement element) { commands[id] = element; registered[id] = element; }
        ReportPrintOptionsEditor? editor = null;
        try
        {
            var revision = Operations.CaptureRevision(); var report = prepared ?? Prepare(); prepared = report;
            int current = previewMode ? PreviewView.PageIndex : 0;
            editor = new ReportPrintOptionsEditor(report.Name, report.Pages.Count, current);
            foreach (var pair in editor.Elements) Register(pair.Key, pair.Value);
            var error = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = ControlTheme.Brush("#A3372E") };
            Register("print-error", error);
            var content = new StackPanel { Spacing = 10, Width = Math.Clamp(ActualWidth - 140, 280, 480) };
            content.Children.Add(editor); content.Children.Add(error);
            var viewport = new ScrollViewer { Content = content, MaxHeight = Math.Clamp(ActualHeight - 220, 190, 630), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var dialog = new ContentDialog { Title = "Print Report", Content = viewport, XamlRoot = XamlRoot,
                PrimaryButtonText = "Print", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
            dialog.Resources["ContentDialogMaxWidth"] = 620d;
            editor.Changed += NotifyState;
            dialog.Opened += (_, _) =>
            {
                var pending = new Stack<DependencyObject>(); pending.Push(dialog);
                while (pending.TryPop(out var node))
                {
                    if (node is Button button)
                    {
                        if (button.Name == "PrimaryButton") Register("print-dialog-print", button);
                        if (button.Name == "CloseButton") Register("print-dialog-cancel", button);
                    }
                    for (int n = 0; n < VisualTreeHelper.GetChildrenCount(node); n++) pending.Push(VisualTreeHelper.GetChild(node, n));
                }
                NotifyState();
            };
            dialog.PrimaryButtonClick += async (_, e) =>
            {
                var deferral = e.GetDeferral();
                try
                {
                    Operations.EnsureCurrent(revision);
                    if (!ReferenceEquals(prepared, report)) throw new InvalidOperationException("The prepared report changed. Cancel and reopen Print Report.");
                    var selection = editor.ReadSelection();
                    var file = new ReportExporter().CreatePrintDocument(report, selection, editor.Reverse);
                    // Request the host within the native button callback. A failure
                    // keeps the validated draft open; completion is not physical print success.
                    await host.PrintAsync(file);
                    if (!disposed) SetStatus(Session.IsCurrent(revision)
                        ? $"Opened {selection.Count} selected page(s) for printing."
                        : "Opened the captured pages for printing; newer changes were not included.");
                }
                catch (OperationCanceledException) { e.Cancel = true; error.Text = "Printing cancelled; the report was not changed."; }
                catch (Exception problem) { e.Cancel = true; error.Text = problem.Message; }
                finally { deferral.Complete(); if (!disposed) NotifyState(); }
            };
            await dialog.ShowAsync();
        }
        finally
        {
            if (editor is not null) editor.Changed -= NotifyState;
            foreach (var pair in registered) if (commands.TryGetValue(pair.Key, out var value) && ReferenceEquals(value, pair.Value)) commands.Remove(pair.Key);
            printDialogOpen = false; if (!disposed) NotifyState();
        }
    }
}
