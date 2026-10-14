using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReportScene.Controls.Uno;

namespace ReportScene.Designer.Uno;

public sealed partial class ReportDesignerControl
{
    private readonly TextBlock statusZoomText = ControlTheme.Label("85%", 11);
    private readonly Slider statusZoom = new() { Minimum = .1, Maximum = 4, Value = .85, StepFrequency = .05, SmallChange = .05, LargeChange = .25, Width = 100, Height = 22, MinHeight = 0 };
    private bool updatingStatusZoom;
    private void BuildStatusBar()
    {
        var bar = new Grid { Background = ControlTheme.Brush("#2777AD"), Padding = new(7, 0, 4, 0) };
        bar.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        status.Foreground = ControlTheme.Brush("#FFFFFF"); status.TextTrimming = TextTrimming.CharacterEllipsis; bar.Children.Add(status);
        var zoom = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        void Button(string id, string label, Action action)
        {
            var button = Command(id, label, "", action); button.Height = 22; button.Padding = new(5, 0, 5, 0);
            if (button.Content is StackPanel body) foreach (var text in body.Children.OfType<TextBlock>()) text.Foreground = ControlTheme.Brush("#FFFFFF"); zoom.Children.Add(button);
        }
        statusZoomText.Foreground = ControlTheme.Brush("#FFFFFF"); statusZoomText.Width = 42; statusZoomText.TextAlignment = Microsoft.UI.Xaml.TextAlignment.Right; zoom.Children.Add(statusZoomText);
        Button("status-zoom-out", "−", () => ChangeZoom((previewMode ? PreviewView.Zoom : DesignView.Zoom) - .1));
        commands["status-zoom"] = ControlTheme.Identify(statusZoom, "status-zoom", "Report zoom"); zoom.Children.Add(statusZoom);
        statusZoom.ValueChanged += (_, _) => { if (!updatingStatusZoom) ChangeZoom(statusZoom.Value); };
        Button("status-zoom-in", "+", () => ChangeZoom((previewMode ? PreviewView.Zoom : DesignView.Zoom) + .1));
        Button("status-fit", "Fit", () => { if (previewMode) PreviewView.FitWidth(); else DesignView.FitWidth(); });
        Grid.SetColumn(zoom, 1); bar.Children.Add(zoom); AddRoot(bar, 5);
    }
    private void ChangeZoom(double value) { if (previewMode) PreviewView.Zoom = value; else DesignView.Zoom = value; SyncStatusZoom(); }
    private void SyncStatusZoom()
    {
        if (DesignView is null || PreviewView is null) return;
        double value = previewMode ? PreviewView.Zoom : DesignView.Zoom;
        updatingStatusZoom = true;
        try
        {
            statusZoom.Value = value; statusZoomText.Text = value.ToString("P0");
            designZoomChoice?.SetZoom(DesignView.Zoom); previewZoomChoice?.SetZoom(PreviewView.Zoom);
        }
        finally { updatingStatusZoom = false; }
    }
}
