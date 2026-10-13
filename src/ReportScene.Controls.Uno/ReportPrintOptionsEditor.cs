using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReportScene.Model;

namespace ReportScene.Controls.Uno;

/// <summary>A native print draft over page counts only; it retains no report content or printer device.</summary>
public sealed class ReportPrintOptionsEditor : UserControl
{
    private readonly ReportPageSelectionEditor pages;
    private readonly CheckBox reverse = new() { Content = "Reverse page order", FontSize = 12, MinHeight = 28 };
    private readonly Dictionary<string, FrameworkElement> elements = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, FrameworkElement> Elements => elements;
    public bool Reverse { get => reverse.IsChecked == true; set => reverse.IsChecked = value; }
    public ReportPageSelection ReadSelection() => pages.ReadSelection();
    public event Action? Changed;

    public ReportPrintOptionsEditor(string reportName, int pageCount, int currentPageIndex)
    {
        ArgumentNullException.ThrowIfNull(reportName);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        pages = new ReportPageSelectionEditor(pageCount, currentPageIndex);
        foreach (var pair in pages.Elements) elements[pair.Key] = pair.Value;
        var title = new TextBlock { Text = reportName, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        elements["print-report-name"] = ControlTheme.Identify(title, "print-report-name", "Report to print");
        elements["print-reverse"] = ControlTheme.Identify(reverse, "print-reverse", "Print selected pages in reverse order");
        var destination = new TextBlock { Text = "System print dialog", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        var help = new TextBlock { Text = "Choose the printer, copies, paper and duplex options in the system dialog. Leave its page range set to All: this document already contains only the pages selected here.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = ControlTheme.Brush("#626262") };
        elements["print-destination"] = ControlTheme.Identify(destination, "print-destination", "Printing destination");
        var system = new StackPanel { Spacing = 6 }; system.Children.Add(destination); system.Children.Add(help);
        Border Section(string label, UIElement content)
        {
            var heading = ControlTheme.Label(label, 12, true); var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(heading); panel.Children.Add(content);
            return new Border { Child = panel, Padding = new(12), BorderThickness = new(1), BorderBrush = ControlTheme.Brush("#CDCDCD"), Background = ControlTheme.Brush("#FAFAFA") };
        }
        var selection = new StackPanel { Spacing = 6 }; selection.Children.Add(pages); selection.Children.Add(reverse);
        var root = new StackPanel { Spacing = 12 }; root.Children.Add(Section("Document", title));
        root.Children.Add(Section("Page range", selection)); root.Children.Add(Section("Printer", system)); Content = root;
        pages.Changed += () => Changed?.Invoke(); reverse.Checked += (_, _) => Changed?.Invoke(); reverse.Unchecked += (_, _) => Changed?.Invoke();
    }
}
