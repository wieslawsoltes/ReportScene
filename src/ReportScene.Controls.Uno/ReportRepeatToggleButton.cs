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
    private bool completingDoubleTap;
    private readonly Queue<string> inputTrace = new(16);
    /// <summary>Last sixteen native input stages; read-only diagnostics, not an input API.</summary>
    public string InputTrace => string.Join(">", inputTrace);
    private void Trace(string stage)
    {
        if (inputTrace.Count == 16) inputTrace.Dequeue();
        inputTrace.Enqueue(stage);
    }
    public event Action? ActivationRequested;
    public event Action? RepeatRequested;
    public ReportRepeatToggleButton()
    {
        IsDoubleTapEnabled = true;
        Click += (_, _) =>
        {
            Trace(completingDoubleTap ? "click-suppressed" : "click-activate");
            if (completingDoubleTap) completingDoubleTap = false;
            else ActivationRequested?.Invoke();
        };
        // A template child or class handler may already have handled the gesture.
        AddHandler(DoubleTappedEvent, new DoubleTappedEventHandler((_, e) =>
        {
            if (!IsEnabled) return;
            Trace("double-tap");
            completingDoubleTap = true;
            RepeatRequested?.Invoke();
            e.Handled = true;
        }), handledEventsToo: true);
        Unloaded += (_, _) => { Trace("unloaded"); completingDoubleTap = false; };
    }
    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        Trace("press");
        // Uno recognizes a double tap on the second DOWN, before this override
        // runs (verified by InputTrace). Its paired release/click still needs
        // the suppression set by that recognition; do not clear it here.
        base.OnPointerPressed(e);
    }
    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        Trace("release");
        try { base.OnPointerReleased(e); }
        finally { completingDoubleTap = false; } // Also retire a cancelled release.
    }
    protected override void OnPointerCanceled(PointerRoutedEventArgs e)
    {
        Trace("cancel");
        completingDoubleTap = false;
        base.OnPointerCanceled(e);
    }
    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        Trace("key");
        completingDoubleTap = false;
        base.OnKeyDown(e);
    }
    protected override void OnToggle()
    {
        // The paired action must not depend on Click being nested inside this
        // override's pointer dispatch. Consume suppression in the paired Click;
        // the next press/key starts an independent activation.
        Trace(completingDoubleTap ? "toggle-suppressed" : "toggle");
        if (!completingDoubleTap) base.OnToggle();
    }
}
