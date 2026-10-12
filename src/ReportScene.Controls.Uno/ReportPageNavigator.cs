using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReportScene.Model;
using Windows.System;

namespace ReportScene.Controls.Uno;

/// <summary>Native, reusable ordinal page navigation. The host supplies position and handles requests; the control never prepares a report.</summary>
public sealed class ReportPageNavigator : UserControl
{
    private readonly Dictionary<string, FrameworkElement> elements = new(StringComparer.Ordinal);
    private readonly TextBox number = new() { Width = 58, Height = 25, MinHeight = 0, Padding = new(4, 1, 3, 1), FontSize = 11, CornerRadius = new(0), MaxLength = 32 };
    private readonly TextBlock count = ControlTheme.Label("of 0", 11);
    private readonly TextBlock feedback = new() { FontSize = 10, MinHeight = 14, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = ControlTheme.Brush("#A3372E") };
    private readonly Button first, previous, next, last;
    private ReportPagePosition position;
    public ReportPagePosition Position => position;
    public string ValidationMessage => feedback.Text;
    public IReadOnlyDictionary<string, FrameworkElement> Elements => elements;
    public event Action<int>? PageRequested;
    public event Action<Exception>? Failed;

    public ReportPageNavigator()
    {
        first = Button("first-page", "First page (Ctrl+Home on preview)", "first", ReportPageCommand.First);
        previous = Button("previous-page", "Previous page (Page Up on preview)", "previous", ReportPageCommand.Previous);
        next = Button("next-page", "Next page (Page Down on preview)", "next", ReportPageCommand.Next);
        last = Button("last-page", "Last page (Ctrl+End on preview)", "last", ReportPageCommand.Last);
        count.Margin = new(4, 0, 3, 0);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var element in new UIElement[] { first, previous, number, count, next, last }) row.Children.Add(element);
        var root = new StackPanel { Spacing = 2 }; root.Children.Add(row); root.Children.Add(feedback); Content = root;
        elements["preview-page"] = ControlTheme.Identify(number, "preview-page", "Page position in the displayed report; Enter navigates and Escape restores");
        elements["preview-page-count"] = ControlTheme.Identify(count, "preview-page-count", "Displayed page count");
        elements["preview-page-error"] = ControlTheme.Identify(feedback, "preview-page-error", "Page entry validation");
        number.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape) { ResetEditor(); e.Handled = true; }
            else if (e.Key == VirtualKey.Enter)
            {
                if (!position.TrySelect(number.Text, out var requested))
                { feedback.Text = position.HasPages ? $"Enter a page from 1 to {position.Count}." : "No pages are available."; ToolTipService.SetToolTip(number, feedback.Text); }
                else Request(requested);
                e.Handled = true;
            }
        };
        SetPosition(0, 0, resetEditor: true);
    }
    private Button Button(string id, string title, string icon, ReportPageCommand command)
    {
        var button = ControlTheme.Button(id, title, icon, () => Request(position.Move(command)), text: false);
        button.Width = 27; button.Height = 25; button.Padding = new(4, 2, 4, 2);
        elements[id] = button; return button;
    }
    /// <summary>Same-position view notifications preserve an unfinished entry. Report replacement can explicitly reset it.</summary>
    public void SetPosition(int pageCount, int pageIndex, bool resetEditor = false)
    {
        var nextPosition = new ReportPagePosition(pageCount, pageIndex);
        bool changed = nextPosition != position; position = nextPosition;
        first.IsEnabled = previous.IsEnabled = position.CanPrevious;
        next.IsEnabled = last.IsEnabled = position.CanNext; number.IsEnabled = position.HasPages;
        string label = "of " + position.Count.ToString(CultureInfo.InvariantCulture); if (count.Text != label) count.Text = label;
        if (changed || resetEditor) ResetEditor();
    }
    private void ResetEditor()
    {
        number.Text = position.PageNumber.ToString(CultureInfo.InvariantCulture);
        feedback.Text = ""; ToolTipService.SetToolTip(number, null);
    }
    private void Request(ReportPagePosition requested)
    {
        try
        {
            if (requested.Index != position.Index && requested.HasPages) PageRequested?.Invoke(requested.Index);
            ResetEditor();
        }
        catch (Exception error) { feedback.Text = error.Message; Failed?.Invoke(error); }
    }
}
