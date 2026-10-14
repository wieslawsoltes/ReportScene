namespace ReportScene.Designer;

/// <summary>
/// Qualifies a native double-tap reset without allowing it to commit a resize.
/// Native gesture recognition owns timing and hit testing; this class owns only
/// movement, cancellation and event ordering. Coordinates are stable-parent DIPs.
/// </summary>
public sealed class DesignerPaneResetGesture
{
    private bool previousTap, firstTapAtStart, disqualified, resetPending, completedPair;
    public bool IsActive { get; private set; }

    public void Begin()
    {
        if (IsActive) throw new InvalidOperationException("A pane interaction is already active.");
        IsActive = true; disqualified = false; firstTapAtStart = previousTap; completedPair = false;
        // Uno can deliver DoubleTapped before the second PointerPressed. Retain
        // that request, but do not change the pane until an unmoved release.
        if (!firstTapAtStart) resetPending = false;
    }
    public void Move(double deltaX, double deltaY)
    {
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY)) throw new ArgumentOutOfRangeException(nameof(deltaX));
        if (IsActive && (Math.Abs(deltaX) > 3 || Math.Abs(deltaY) > 3)) Interrupt();
    }
    /// <summary>Keyboard input, capture cancellation and unloading invalidate tap eligibility.</summary>
    public void Interrupt()
    {
        previousTap = firstTapAtStart = resetPending = completedPair = false;
        if (IsActive) disqualified = true;
    }
    /// <summary>Call only in response to a platform-recognized double tap.</summary>
    public void RequestReset()
    {
        resetPending = IsActive ? firstTapAtStart && !disqualified : previousTap;
    }
    /// <summary>Returns true only for an uncancelled, unmoved completed reset pair.</summary>
    public bool Complete(bool cancelled)
    {
        if (!IsActive) { if (cancelled) Interrupt(); return false; }
        bool tap = !cancelled && !disqualified;
        IsActive = false; completedPair = tap && firstTapAtStart; previousTap = tap;
        firstTapAtStart = false; disqualified = false;
        if (!completedPair) resetPending = false;
        return TryCompleteReset();
    }
    /// <summary>
    /// Also call after native event dispatch to support platforms that recognize
    /// DoubleTapped after the second release. An active/cancelled drag cannot reset.
    /// </summary>
    public bool TryCompleteReset()
    {
        if (IsActive || !resetPending || !completedPair) return false;
        Interrupt(); return true;
    }
}
