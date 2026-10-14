using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using ReportScene.Controls.Uno;
using ReportScene.Model;

namespace ReportScene.Designer.Uno;

public sealed partial class ReportDesignerControl
{
    // Instance-owned brushes are reused across selection/save/zoom notifications.
    // They are not mutable process-wide theme resources shared by unrelated hosts.
    private readonly SolidColorBrush formattingActiveBrush = ControlTheme.Brush("#CDE6F7");
    private readonly SolidColorBrush formattingMixedBrush = ControlTheme.Brush("#E3E3E3");
    private readonly SolidColorBrush formattingInactiveBrush = ControlTheme.Brush("#00FFFFFF");
    private ReportBorderWidthSelector? borderWidthChoice;
    private ToggleButton AppearanceButton(string id, string name, string iconName, Action action, bool showText = false)
    {
        var icon = new ReportIcon { Icon = iconName, Width = 17, Height = 17 };
        var button = new ToggleButton { Content = showText ? Horizontal(icon, ControlTheme.Label(name, 11)) : icon,
            Width = showText ? 76 : 28, Height = showText ? 28 : 25, MinWidth = 0, MinHeight = 0,
            Padding = new(3, 1, 3, 1), BorderThickness = new(0), CornerRadius = new(0), IsThreeState = false };
        commands[id] = ControlTheme.Identify(button, id, name); ToolTipService.SetToolTip(button, name);
        button.Click += (_, _) => { if (!updatingRibbon) Run(() => { try { action(); } finally { SyncRibbonSelection(); } }); };
        return button;
    }
    private UIElement BorderWidthChoice()
    {
        var selector = new ReportBorderWidthSelector(); borderWidthChoice = selector;
        commands["border-width"] = ControlTheme.Identify(selector.Editor, "border-width", "Whole-object border width in points");
        ToolTipService.SetToolTip(selector.Editor, "Border width in points; None removes the whole-object border.");
        selector.WidthRequested += value =>
        {
            if (disposed || updatingRibbon || previewMode || !ReferenceEquals(borderWidthChoice, selector)) return;
            Run(() => Session.SetSelectionBorderWidth(value));
        };
        return Vertical(ControlTheme.Label("Width (pt)", 10), selector);
    }
    private void SyncAppearance(ReportItem[] selected, bool editable)
    {
        var first = selected.FirstOrDefault();
        if (borderWidthChoice is { } selector)
        {
            double? common = first is not null && selected.All(item => item.Style.BorderWidth == first.Style.BorderWidth) ? first.Style.BorderWidth : null;
            selector.Editor.IsEnabled = editable; selector.SetWidth(common, first is not null && common is null);
        }
        if (commands.TryGetValue("border", out var borderElement) && borderElement is ToggleButton border)
        {
            bool? present = first is null ? false : selected.All(item => (item.Style.BorderWidth > 0) == (first.Style.BorderWidth > 0)) ? first.Style.BorderWidth > 0 : null;
            border.IsEnabled = editable; border.IsChecked = present;
            border.Background = present == true ? formattingActiveBrush : present is null ? formattingMixedBrush : formattingInactiveBrush;
        }
        foreach (var (id, alignment) in VerticalAlignmentChoices)
            if (commands.TryGetValue(id, out var element) && element is ToggleButton button)
            {
                bool active = first is not null && selected.All(item => item.Style.VerticalAlignment == alignment);
                button.IsEnabled = editable; button.IsChecked = active;
                button.Background = active ? formattingActiveBrush : formattingInactiveBrush;
            }
    }
    private static readonly (string Id, VerticalAlign Alignment)[] VerticalAlignmentChoices =
        [("vertical-top", VerticalAlign.Top), ("vertical-center", VerticalAlign.Center), ("vertical-bottom", VerticalAlign.Bottom)];
    private static readonly (string Id, string Name, Func<ItemStyle, bool> Value)[] TextEmphasisChoices =
        [("bold", "Bold", style => style.Bold), ("italic", "Italic", style => style.Italic), ("underline", "Underline", style => style.Underline)];
    private static readonly (string Id, TextAlign Alignment)[] HorizontalAlignmentChoices =
        [("text-left", TextAlign.Left), ("text-center", TextAlign.Center), ("text-right", TextAlign.Right)];
    private static readonly string[] ColorChoiceIds = ["fill-color", "text-color", "border-color"];
}
