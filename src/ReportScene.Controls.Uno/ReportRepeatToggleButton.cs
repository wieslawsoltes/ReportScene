using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace ReportScene.Controls.Uno;

/// <summary>
/// Native toggle button with single-click/keyboard activation and a double-tap
/// repeat request. The second pointer release must not undo that repeat request.
/// </summary>
public sealed class ReportRepeatToggleButton : ToggleButton
{
    private bool completingDoubleTap, dispatchingPointer;
    public event Action? RepeatRequested;
    public ReportRepeatToggleButton()
    {
        IsDoubleTapEnabled = true;
        // A template child or class handler may already have handled the gesture.
        AddHandler(DoubleTappedEvent, new DoubleTappedEventHandler((_, e) =>
        {
            if (!IsEnabled) return;
            completingDoubleTap = true;
            RepeatRequested?.Invoke();
            e.Handled = true;
        }), handledEventsToo: true);
    }
    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        completingDoubleTap = false;
        dispatchingPointer = true;
        try { base.OnPointerPressed(e); }
        finally { dispatchingPointer = false; }
    }
    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        dispatchingPointer = true;
        try { base.OnPointerReleased(e); }
        finally { dispatchingPointer = false; }
    }
    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        completingDoubleTap = false;
        base.OnKeyDown(e);
    }
    protected override void OnClick()
    {
        // Suppress only the trailing pointer activation from this double-tap.
        // Keyboard and automation calls outside pointer dispatch remain native.
        if (!completingDoubleTap || !dispatchingPointer) base.OnClick();
    }
}
