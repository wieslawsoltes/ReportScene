using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ReportScene.Controls.Uno;

public static class ControlTheme
{
    public static SolidColorBrush Brush(string hex)
    {
        var color = Rendering.Skia.ReportRenderer.Color(hex);
        return new(Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue));
    }
    public static TextBlock Label(string text, double size = 12, bool bold = false) => new()
    { Text = text, FontSize = size, FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal, Foreground = Brush("#252A30"), VerticalAlignment = VerticalAlignment.Center };
    public static Border Panel(UIElement child, string color = "#F4F4F4", Thickness? padding = null) => new()
    { Child = child, Background = Brush(color), BorderBrush = Brush("#C8CCD0"), BorderThickness = new(1), Padding = padding ?? new(0) };
    public static T Identify<T>(T element, string id, string name) where T : DependencyObject
    { AutomationProperties.SetAutomationId(element, id); AutomationProperties.SetName(element, name); return element; }
    public static Button Button(string id, string title, string icon, Action action, bool large = false, bool text = true)
    {
        var body = new StackPanel { Orientation = large ? Orientation.Vertical : Orientation.Horizontal, Spacing = large ? 4 : 5, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        if (icon.Length > 0) body.Children.Add(new ReportIcon { Icon = icon, Width = large ? 29 : 17, Height = large ? 29 : 17 });
        if (text) body.Children.Add(Label(title, 11));
        var button = Identify(new Button { Content = body, Padding = large ? new(9, 6, 9, 4) : new(7, 3, 7, 3), MinWidth = 0, MinHeight = 0, Height = large ? 65 : 28, BorderThickness = new(0), CornerRadius = new(1), Background = Brush("#00FFFFFF") }, id, title);
        ToolTipService.SetToolTip(button, title); button.Click += (_, _) => action(); return button;
    }
}
