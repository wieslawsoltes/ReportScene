# Pane reset and resize cancellation

Run 38555861342 on 358f890 passed the native core (822 checks), package
consumers, both Uno builds and seven browser shards, including repeat Format
Painter and the grid raster cases. It failed one scenario in shard 2: after
resizing to 328 and keyboard-sizing to 320, another drag followed by Escape
left the sidebar at its default 280 instead of the committed 320. Native
publication continued; this was not a rendering deadline. The retained trace
records preference generation advancing to 3 and subsequently writing 280.

The splitter reset handler unconditionally ended a drag and immediately reset
layout on DoubleTapped. A platform gesture can accompany pointer input; in the
pinned Uno event ordering, double-tap recognition can precede the second press.
Treating it as an immediate layout command discards the opening resize value
before Escape can restore it. A drag or intervening keyboard input must not
qualify as the first tap of a reset.

DesignerPaneResetGesture now qualifies the native request using completed
stationary interactions. It does not invent a double-click timeout or replace
platform hit testing. Movement beyond three stable-parent DIPs on either axis,
keyboard input, cancellation and unload invalidate the pair. Reset only takes
effect after an uncancelled unmoved second release. One dispatcher observation
supports native recognition after release, without an idle loop or timer.
Unexpected pointer-capture loss cancels resizing; ordinary release still
commits it and cannot recursively cancel during capture cleanup.

Eleven shared native/WebAssembly cases cover early, during-press and late
recognition, first/second drags, out-and-back motion, keyboard invalidation,
Escape, lost capture, unload, jitter and duplicate completion. An added native
browser scenario exercises both splitter orientations, real double-click
reset, rapid press/move/Escape, unchanged preference generation/write count,
saved report and history. The original failing compact-window case remains
unchanged. Acceptance requires all 73 browser cases in all eight shards, zero
retries/skips/flaky outcomes, plus the existing builds and core/package gates.

This correction does not establish absence of every timing or input defect.
Earlier failed runs remain failures; new-head acceptance is recorded separately.

Primary gesture contract:
https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.doubletapped
