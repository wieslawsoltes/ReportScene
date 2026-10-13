using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReportScene.Model;

namespace ReportScene.Controls.Uno;

/// <summary>Reusable native All/Current/Pages editor over displayed page ordinals. It performs no export or report mutation.</summary>
public sealed class ReportPageSelectionEditor : UserControl
{
    private readonly RadioButton all, current, custom;
    private readonly TextBox numbers = new() { PlaceholderText = "1, 3-5, 8", FontSize = 12, MinHeight = 28, Height = 28,
        Padding = new(6, 2, 6, 2), MaxLength = ReportPageSelection.MaximumExpressionLength, CornerRadius = new(0) };
    private readonly TextBlock summary = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = ControlTheme.Brush("#626262") };
    private readonly Dictionary<string, FrameworkElement> elements = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, FrameworkElement> Elements => elements;
    public int PageCount { get; }
    public int CurrentPageIndex { get; }
    private bool allOnly;
    private PageSelectionMode savedMode;
    /// <summary>Data-only exporters disable page filtering without destroying a user's draft for other formats.</summary>
    public bool AllPagesOnly
    {
        get => allOnly;
        set
        {
            if (allOnly == value) return;
            if (value) savedMode = Mode;
            allOnly = value;
            (value || savedMode == PageSelectionMode.All ? all : savedMode == PageSelectionMode.Current ? current : custom).IsChecked = true;
            UpdateState();
        }
    }
    public PageSelectionMode Mode => allOnly || all.IsChecked == true ? PageSelectionMode.All : current.IsChecked == true ? PageSelectionMode.Current : PageSelectionMode.Custom;
    public event Action? Changed;
    public ReportPageSelectionEditor(int pageCount, int currentPageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageCount);
        if (pageCount > 0 && (uint)currentPageIndex >= (uint)pageCount) throw new ArgumentOutOfRangeException(nameof(currentPageIndex));
        PageCount = pageCount; CurrentPageIndex = currentPageIndex;
        string group = "report-page-selection-" + Guid.NewGuid().ToString("N");
        RadioButton Radio(string id, string label)
        {
            var button = new RadioButton { Content = label, GroupName = group, FontSize = 12, MinHeight = 0, Height = 28, Padding = new(0) };
            elements[id] = ControlTheme.Identify(button, id, label); return button;
        }
        all = Radio("output-pages-all", "All pages"); current = Radio("output-pages-current", $"Current page ({(pageCount == 0 ? 0 : currentPageIndex + 1)})");
        custom = Radio("output-pages-custom", "Pages:"); all.IsChecked = true;
        var rangeRow = new Grid { ColumnSpacing = 6 };
        rangeRow.ColumnDefinitions.Add(new() { Width = new(92) }); rangeRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        rangeRow.Children.Add(custom); Grid.SetColumn(numbers, 1); rangeRow.Children.Add(numbers);
        var panel = new StackPanel { Spacing = 4 }; panel.Children.Add(all); panel.Children.Add(current); panel.Children.Add(rangeRow); panel.Children.Add(summary); Content = panel;
        elements["output-page-numbers"] = ControlTheme.Identify(numbers, "output-page-numbers", "Displayed page numbers and ranges, separated by commas");
        elements["output-page-summary"] = ControlTheme.Identify(summary, "output-page-summary", "Page selection summary");
        foreach (var radio in new[] { all, current, custom }) radio.Checked += (_, _) => UpdateState();
        numbers.TextChanged += (_, _) => { Changed?.Invoke(); };
        UpdateState();
    }
    private void UpdateState()
    {
        all.IsEnabled = !allOnly; current.IsEnabled = !allOnly && PageCount > 0; custom.IsEnabled = !allOnly && PageCount > 0;
        numbers.IsEnabled = !allOnly && PageCount > 0 && custom.IsChecked == true;
        summary.Text = allOnly ? "CSV exports all bound table rows; page selection does not apply."
            : $"{PageCount} prepared page(s). Ranges use displayed positions, not printed page labels. Duplicates are exported once, in report order.";
        Changed?.Invoke();
    }
    public ReportPageSelection ReadSelection() => ReportPageSelection.Create(PageCount, Mode, CurrentPageIndex, numbers.Text);
}
