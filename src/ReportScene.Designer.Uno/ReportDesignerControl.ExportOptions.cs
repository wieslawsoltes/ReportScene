using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ReportScene.Controls.Uno;
using ReportScene.Export;

namespace ReportScene.Designer.Uno;

public sealed partial class ReportDesignerControl
{
    private bool exportDialogOpen;
    private async Task ExportOptionsAsync()
    {
        if (exportDialogOpen || disposed) return;
        exportDialogOpen = true;
        var registered = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal);
        void Register(string key, FrameworkElement value) { commands[key] = value; registered[key] = value; }
        try
        {
            var revision = Operations.CaptureRevision(); var document = Session.Document;
            var report = prepared ?? Prepare(); prepared = report;
            int position = previewMode ? PreviewView.PageIndex : 0;
            var pages = new ReportPageSelectionEditor(report.Pages.Count, position);
            foreach (var pair in pages.Elements) Register(pair.Key, pair.Value);
            var formats = new[] { ExportFormat.Pdf, ExportFormat.Svg, ExportFormat.Png, ExportFormat.Html, ExportFormat.Text, ExportFormat.PreparedJson, ExportFormat.Csv };
            var format = new ReportComboBox { ItemsSource = new[] { "PDF document", "SVG vector pages", "PNG images", "HTML document", "Plain text", "Prepared report JSON", "CSV bound data" },
                SelectedIndex = 0, Height = 28, MinHeight = 0, FontSize = 12, Padding = new(6, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
            Register("output-format", format);
            var resolution = new ReportComboBox { ItemsSource = new[] { "72", "96", "144", "150", "300", "600", "1200" }, SelectedIndex = 2,
                Width = 110, Height = 28, MinHeight = 0, FontSize = 12, Padding = new(6, 0, 0, 0) };
            Register("output-resolution", resolution);
            var hint = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = ControlTheme.Brush("#626262") };
            Register("output-format-hint", hint);
            var error = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = ControlTheme.Brush("#A3372E") };
            Register("output-error", error);
            Border Group(string title, UIElement content)
            {
                var label = ControlTheme.Label(title, 12); label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                var section = new StackPanel { Spacing = 8 }; section.Children.Add(label); section.Children.Add(content);
                return new Border { Child = section, Padding = new(12), BorderThickness = new(1), BorderBrush = ControlTheme.Brush("#CDCDCD"), Background = ControlTheme.Brush("#FAFAFA") };
            }
            var resolutionRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            resolutionRow.Children.Add(ControlTheme.Label("Resolution (DPI)", 12)); resolutionRow.Children.Add(resolution);
            var options = new StackPanel { Spacing = 8 }; options.Children.Add(resolutionRow); options.Children.Add(hint);
            var panel = new StackPanel { Spacing = 12, Width = Math.Clamp(ActualWidth - 120, 280, 500) };
            panel.Children.Add(Group("Export format", format)); panel.Children.Add(Group("Page range", pages)); panel.Children.Add(Group("Options", options)); panel.Children.Add(error);
            var scroll = new ScrollViewer { Content = panel, MaxHeight = Math.Clamp(ActualHeight - 220, 200, 590), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var dialog = new ContentDialog { Title = "Export Report", Content = scroll, XamlRoot = XamlRoot,
                PrimaryButtonText = "Export", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
            dialog.Resources["ContentDialogMaxWidth"] = 620d;
            void UpdateFormat()
            {
                if (format.SelectedIndex < 0) return;
                var selected = formats[format.SelectedIndex]; bool raster = selected is ExportFormat.Png or ExportFormat.Pdf;
                pages.AllPagesOnly = selected == ExportFormat.Csv; resolution.IsEnabled = raster;
                hint.Text = selected switch
                {
                    ExportFormat.Png => "One selected page produces a PNG; multiple pages produce a ZIP. Each image is limited to 64 million pixels.",
                    ExportFormat.Pdf => "Vector PDF output. DPI applies to raster content in the PDF backend, not vector page size.",
                    ExportFormat.Svg => "One selected page produces an SVG; multiple pages produce a ZIP. Page numbering is preserved.",
                    ExportFormat.Csv => "Exports bound data tables, not report layout. Multiple tables produce a ZIP archive.",
                    ExportFormat.PreparedJson => "Stores the selected prepared pages with their original physical page numbers.",
                    _ => "Exports selected pages in their original report order. Resolution does not apply to this format."
                };
                error.Text = ""; NotifyState();
            }
            format.SelectionChanged += (_, _) => UpdateFormat();
            UpdateFormat(); ExportFile? file = null;
            dialog.Opened += (_, _) =>
            {
                var pending = new Stack<DependencyObject>(); pending.Push(dialog);
                while (pending.TryPop(out var node))
                {
                    if (node is Button button)
                    {
                        if (button.Name == "PrimaryButton") Register("output-dialog-export", button);
                        if (button.Name == "CloseButton") Register("output-dialog-cancel", button);
                    }
                    for (int n = 0; n < VisualTreeHelper.GetChildrenCount(node); n++) pending.Push(VisualTreeHelper.GetChild(node, n));
                }
                NotifyState();
            };
            dialog.PrimaryButtonClick += (_, e) =>
            {
                try
                {
                    ObjectDisposedException.ThrowIf(disposed, this); Operations.EnsureCurrent(revision);
                    if (!ReferenceEquals(prepared, report)) throw new InvalidOperationException("The prepared report changed. Cancel and reopen Export Report.");
                    if ((uint)format.SelectedIndex >= (uint)formats.Length) throw new FormatException("Choose an export format.");
                    var selected = formats[format.SelectedIndex]; var selection = pages.ReadSelection();
                    double dpi = selected is ExportFormat.Pdf or ExportFormat.Png
                        ? double.Parse((string)resolution.SelectedItem, CultureInfo.InvariantCulture) : 144;
                    // Generate only after successful validation, while the captured revision
                    // is still current. Failed export keeps the draft open and saves nothing.
                    file = new ReportExporter().ExportSelection(report, selected, selection, new(Dpi: dpi), document);
                }
                catch (Exception problem) { file = null; error.Text = problem.Message; e.Cancel = true; NotifyState(); }
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && file is not null)
            {
                ObjectDisposedException.ThrowIf(disposed, this); Operations.EnsureCurrent(revision);
                await host.SaveFileAsync(file);
                if (!disposed) SetStatus(Session.IsCurrent(revision)
                    ? $"Exported {file.Name} · {file.Content.Length:N0} bytes"
                    : $"Exported the captured report as {file.Name}; current report changes were not included.");
            }
        }
        catch (OperationCanceledException) { if (!disposed) SetStatus("Export cancelled; the report was not changed."); }
        finally
        {
            foreach (var pair in registered) if (commands.TryGetValue(pair.Key, out var value) && ReferenceEquals(pair.Value, value)) commands.Remove(pair.Key);
            exportDialogOpen = false; if (!disposed) NotifyState();
        }
    }
}
