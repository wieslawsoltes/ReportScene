# Format Painter input and recording integration

Run 38454738589 did not pass. The native repeat-mode scenario in shard 4
observed False after an actual double-click and never reached target painting.
The toolbar had independent Click (toggle/cancel) and DoubleTapped (repeat)
actions on the same native button. ReportRepeatToggleButton now observes handled
double-tap gestures and prevents the trailing pointer Click from cancelling the
repeat request. A subsequent ordinary press and keyboard activation retain the
native ToggleButton path. No timer, synthetic click or delayed model edit is used.
The three existing native Format Painter scenarios now also check cancelling
repeat mode by the next single click, re-arming by double-click, and Space-based
activation/cancellation. Exact saved report, undo, locked targets and pixel
requirements remain.

The same run also failed existing font-size/paper screenshot observations in
shard 2 and the initial preview candidate-count assertion in shard 6 (0
observed, at least 500 required). The retained final preview snapshot later
contains 501 candidates and one completed recording; the failure occurred
before wheel input, not at a scroll-offset assertion. The font and compact-paper
PNGs also still show the initial Professional invoice rather than the loaded
fixture. These failures are retained, not relabeled as passes or hidden by
retries. They are not claimed to be caused by the Format Painter double-tap
defect. Combined-head acceptance must run every existing case unchanged apart
from the added input checks.

The branch now incorporates the native authoring recording bounds from #22.
The overlapping ConformanceSuite line retains both the eight recording cases
and all seventeen formatting cases. Format Painter canvas input/disposal and
the recording clip/loaded invalidation coexist in ReportDesignView. Both
original parents are preserved in Git history; older source is not copied over
new rendering fixes.

The combined review also fixes the clip helper's extreme-range guard: a
binary64 edge just beyond float.MaxValue can cast to a finite float, but an
outward adjustment would then produce infinity. Exact double-range checks
reject it before canvas mutation, with positive and negative regressions.
Normal report viewport geometry and raster requirements are unchanged.

## Host callback ownership

The reusable tool previously treated any change observed while Apply was on the
stack as its own repeat-mode edit. A host observer can make a different edit or
reload the document during that notification. The tool could then adopt the
new revision and remain armed, contrary to its external-edit invalidation rule.
An internal commit callback now supplies the exact formatting revision before
public notifications. Other content changes cancel the capture regardless of
observer subscription order. The callback is not retained in undo history.

Capture generations keep a newly requested capture from being consumed by an
older operation. Recursive Apply is rejected before mutation. Validation errors
before mutation keep the original capture; an observer failure after commit
cancels the tool while retaining the completed edit's normal undo entry. Host
exceptions are propagated, not presented as rolled-back report changes.

Twelve additional shared native/WebAssembly regressions exercise both callback
orders, reentrant external edits and reload, equal-style selection callbacks and drag startup,
save acknowledgments, replacement captures, cancellation, disposal, recursive
Apply rejection and post-commit observer exceptions. The original seventeen
formatting cases and all eight recording cases remain registered.

All 70 browser scenarios across eight shards, zero retries/skips/flaky results,
core conformance, differential benchmarks, isolated package consumers, desktop
and actual Uno WebAssembly gates remain mandatory. Local JavaScript syntax and
Git whitespace checks pass, but local .NET/runtime execution is unavailable.
Final-head CI results must be recorded separately. This is not full FastReport
parity or a proven fix for every browser/presentation stall.

Primary gesture contract:
https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.doubletapped
