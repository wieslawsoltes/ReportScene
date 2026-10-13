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
shard 2 and a preview vertical-offset assertion in shard 6. Those failures are
retained, not relabeled as passes or hidden by retries. They are not claimed to
be caused by the Format Painter double-tap defect. Combined-head acceptance
must run every existing case unchanged apart from the added input checks.

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

All 70 browser scenarios across eight shards, zero retries/skips/flaky results,
core conformance, differential benchmarks, isolated package consumers, desktop
and actual Uno WebAssembly gates remain mandatory. Local JavaScript syntax and
Git whitespace checks pass, but local .NET/runtime execution is unavailable.
Final-head CI results must be recorded separately. This is not full FastReport
parity or a proven fix for every browser/presentation stall.

Primary gesture contract:
https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.doubletapped
