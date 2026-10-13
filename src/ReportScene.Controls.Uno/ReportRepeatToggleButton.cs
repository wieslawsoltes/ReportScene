using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace ReportScene.Controls.Uno;

/// <summary>
/// Native toggle with single-click/keyboard activation and a double-tap repeat
/// request. Hosts use ActivationRequested and RepeatRequested for the paired
/// actions; inherited Click and Command keep their ordinary native semantics.
/// </summary>
public sealed class ReportRepeatToggleButton : ToggleButton
{
    private bool completingDoubleTap, dispatchingPointer;
    public event Action? ActivationRequested;
    public event Action? RepeatRequested;
    public ReportRepeatToggleButton()
    {
        IsDoubleTapEnabled = true;
        Click += (_, _) =>
        {
            if (!completingDoubleTap || !dispatchingPointer) ActivationRequested?.Invoke();
        };
        // A template child or class handler may already have handled the gesture.
        AddHandler(DoubleTappedEvent, new DoubleTappedEventHandler((_, e) =>
        {
            if (!IsEnabled) return;
            completingDoubleTap = true;
            RepeatRequested?.Invoke();
            e.Handled = true;
        }), handledEventsToo: true);
        Unloaded += (_, _) => { completingDoubleTap = false; dispatchingPointer = false; };
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
    protected override void OnToggle()
    {
        // OnClick is not a public extensibility point in the pinned Uno API.
        // Preserve native press/capture/focus handling; suppress only the second
        // pointer toggle and paired activation after a recognized double tap.
        if (!completingDoubleTap || !dispatchingPointer) base.OnToggle();
    }
}
