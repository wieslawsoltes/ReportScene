# Distinct native double-click groups

Run 38562054850 on 062c174 passed 833 native conformance cases, package
consumers, both Uno builds and seven browser shards. Both the original
compact-pane scenario and the new two-orientation reset/cancellation scenario
passed. The remaining failure was the second Format Painter repeat activation,
not its initial activation, pane cancellation, or a stalled renderer.

The retained browser input trace records the cancel click at 212190.815 ms and
the next dblclick action at 212664.068 ms: less than 474 ms apart. The native
trace places DoubleTapped before the *first* press of that new action, followed
by one suppressed release, then an ordinary click which correctly cancels it.
The test accidentally supplied a native three-click cluster across its API
call boundaries. Playwright's dblclick grouping is not a reset of Uno's native
gesture recognizer.

The pinned Uno implementation defines a 500000-microsecond multi-tap window
and 10-DIP horizontal/vertical tap ranges. It stores the preceding single tap
and consumes that identity when recognizing the next double tap. This matches
the observed sequence; no new application input correction is justified here.

The test keeps the immediate next-single-click cancellation, then starts the
next intentional double-click 12 DIPs to the right, still within the same
69-DIP button. Both clicks in that new pair remain at one physical position.
The earlier cancel cannot be its first tap. No sleep, expanded timeout,
synthetic application event, diagnostic mutation, failed-job retry or removed
assertion is introduced. The test additionally requires the native trace to
end in a recognized double tap with its paired suppressed release/click, and
retains separate initial/rearmed diagnostic artifacts rather than overwriting
the first one. All target, lock, history, undo, saved-content and Escape checks
remain. All 73 scenarios still run once across all eight shards.

The production source tree is unchanged by this test-precondition correction.
Fresh whole-head acceptance is required; the earlier run remains a failure.

Primary pinned source:
https://github.com/unoplatform/uno/blob/da217df2312e9e39676880f9cb4b8d519cc297e0/src/Uno.UI/UI/Input/WinRT/GestureRecognizer.cs
https://github.com/unoplatform/uno/blob/da217df2312e9e39676880f9cb4b8d519cc297e0/src/Uno.UI/UI/Input/WinRT/GestureRecognizer.Gesture.cs
