using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using ReportScene.Controls.Uno;

namespace ReportScene.Designer.Uno;

public sealed partial class ReportDesignerControl
{
    private ToggleButton FormatPainterButton()
    {
        var label = new TextBlock { Text = "Format\nPainter", FontSize = 11, TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center };
        var icon = new ReportIcon { Icon = "format-painter", Width = 23, Height = 23, HorizontalAlignment = HorizontalAlignment.Center };
        var button = new ReportRepeatToggleButton { Content = Vertical(icon, label), Width = 69, Height = 61, MinWidth = 0, MinHeight = 0,
            Padding = new(3, 2, 3, 2), BorderThickness = new(0), CornerRadius = new(0) };
        commands["format-painter"] = ControlTheme.Identify(button, "format-painter", "Format Painter");
        ToolTipService.SetToolTip(button, "Copy appearance from one selected object. Click an object to apply; double-click this tool to paint multiple objects. Escape cancels.");
        button.Click += (_, _) => Run(() =>
        {
            if (DesignView.FormatPainter.IsActive) DesignView.FormatPainter.Cancel();
            else DesignView.FormatPainter.Begin();
            SyncFormatPainterButton();
        });
        button.RepeatRequested += () => Run(() => { DesignView.FormatPainter.Begin(repeat: true); SyncFormatPainterButton(); });
        return button;
    }
    private void SyncFormatPainterButton(bool? singleSelection = null)
    {
        if (!commands.TryGetValue("format-painter", out var element) || element is not ToggleButton button) return;
        bool active = DesignView.FormatPainter.IsActive;
        button.IsEnabled = !previewMode && !Session.HasActiveDrag && (active || (singleSelection ?? Session.Selection.Take(2).Count() == 1));
        button.IsChecked = active; button.Background = ControlTheme.Brush(active ? "#CDE6F7" : "#00FFFFFF");
    }
    private void FormatPainterChanged()
    {
        if (disposed) return;
        SyncFormatPainterButton(); UpdateStatus(); NotifyState();
    }
}
