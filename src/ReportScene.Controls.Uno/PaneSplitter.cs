using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;

namespace ReportScene.Controls.Uno;

/// <summary>Pointer/touch and keyboard pane sizing without a platform-specific docking dependency.</summary>
public sealed class PaneSplitter : UserControl
{
    private readonly Border line = new() { Background = ControlTheme.Brush("#D0D0D0"), BorderThickness = new(0) };
    private UIElement? reference;
    private Point start;
    private double value = 280, original;
    private bool dragging;
    private Pointer? pointer;
    public Orientation Orientation { get; set; } = Orientation.Vertical;
    public bool Reverse { get; set; }
    public double Minimum { get; set; } = 120;
    public double Maximum { get; set; } = 640;
    public bool IsDragging => dragging;
    public double Value
    {
        get => value;
        set
        {
            if (!double.IsFinite(value) || !double.IsFinite(Minimum) || !double.IsFinite(Maximum) || Maximum < Minimum) throw new ArgumentOutOfRangeException(nameof(value));
            double next = Math.Clamp(value, Minimum, Maximum); if (this.value == next) return;
            this.value = next; ValueChanged?.Invoke(next);
        }
    }
    public event Action<double>? ValueChanged;
    public event Action? ResetRequested;
    public event Action? InteractionStarted;
    public event Action<bool>? InteractionCompleted;
    public PaneSplitter()
    {
        IsTabStop = true; Content = line;
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        PointerEntered += (_, _) => line.Background = ControlTheme.Brush("#99C9EA");
        PointerExited += (_, _) => { if (!dragging) line.Background = ControlTheme.Brush("#D0D0D0"); };
        GotFocus += (_, _) => line.Background = ControlTheme.Brush("#69B4E8");
        LostFocus += (_, _) => { if (!dragging) line.Background = ControlTheme.Brush("#D0D0D0"); };
        PointerPressed += (_, e) =>
        {
            if (dragging) return;
            var point = e.GetCurrentPoint(this);
            if (point.PointerDevice.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed) return;
            reference = Parent as UIElement ?? this; start = e.GetCurrentPoint(reference).Position; original = Value; pointer = e.Pointer;
            dragging = CapturePointer(e.Pointer); Focus(FocusState.Pointer);
            if (dragging) InteractionStarted?.Invoke(); else pointer = null;
            e.Handled = true;
        };
        PointerMoved += (_, e) =>
        {
            if (!dragging || pointer?.PointerId != e.Pointer.PointerId) return;
            var point = e.GetCurrentPoint(reference).Position; double delta = Orientation == Orientation.Vertical ? point.X - start.X : point.Y - start.Y;
            Value = original + (Reverse ? -delta : delta); e.Handled = true;
        };
        PointerReleased += (_, e) => { if (pointer?.PointerId == e.Pointer.PointerId) End(false); e.Handled = true; };
        PointerCanceled += (_, _) => End(true);
        PointerCaptureLost += (_, _) => { if (dragging) End(false); };
        DoubleTapped += (_, e) => { End(false); ResetRequested?.Invoke(); e.Handled = true; };
        KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape && dragging) { End(true); e.Handled = true; return; }
            double direction = e.Key switch
            {
                VirtualKey.Left when Orientation == Orientation.Vertical => -1, VirtualKey.Right when Orientation == Orientation.Vertical => 1,
                VirtualKey.Up when Orientation == Orientation.Horizontal => -1, VirtualKey.Down when Orientation == Orientation.Horizontal => 1, _ => 0
            };
            if (direction != 0) { Value += direction * (Reverse ? -8 : 8); e.Handled = true; }
            else if (e.Key == VirtualKey.Home) { Value = Minimum; e.Handled = true; }
            else if (e.Key == VirtualKey.End) { Value = Maximum; e.Handled = true; }
        };
    }
    private void End(bool cancel)
    {
        if (!dragging) return;
        // Keep transient-state guards active while restoring the visible value.
        if (cancel) Value = original;
        dragging = false; var captured = pointer; pointer = null;
        if (captured is not null) ReleasePointerCapture(captured);
        line.Background = ControlTheme.Brush("#D0D0D0"); InteractionCompleted?.Invoke(cancel);
    }
}
