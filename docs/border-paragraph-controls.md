# Border widths and paragraph formatting

## Native workflow

The Home ribbon now provides a compact, synchronized vertical-alignment group
with distinct top/middle/bottom text icons, and a whole-object border-width
selector in the Border & fill group. Values are points, matching ReportScene's
model, not an assertion of vendor-pixel unit compatibility. None removes the
border. Ten preset positions remain stable; at most one additional display row
represents the exact current imported/property-edited width. The display uses
three decimal places (scientific notation for very large widths), but selecting
that row never rounds or reparses the stored double. Mixed selections show no
invented common width and stay unchanged until a real choice is made.

Border presence has an explicit mixed state. Previously the command inverted
every item's border independently, exchanging which objects had borders.
It now removes borders only when all editable targets have them; otherwise it
adds missing one-point borders while preserving existing positive widths.
Locked targets neither change nor decide the toggle action. Width and vertical
alignment commands preserve every unrelated style/content field, create one
undoable batch, validate arguments before mutation, and do not add history for
an equal value. The reusable commands reject an active drag.

Vertical-alignment buttons show the actual single/common alignment. Mixed
alignments have no selected button. Repeating the current alignment does not
uncheck the active button or invent an edit. Empty and all-locked selections
disable the appearance controls. Native controls and independent library
consumers use the same public implementation, not an HTML imitation.

## Work avoided

Formatting state reuses three instance-owned brushes and static descriptor
arrays rather than constructing/parsing equivalent brushes and descriptor
arrays on every notification. The new controls reuse the selection array
already gathered for ribbon synchronization. The border selector retains no
past selection/document and only one custom row. Rebuilt ribbons invalidate
old selector callbacks through current-instance identity.

The 2,000-object shared test checks exactly one replacement scan for 1,000
selected width changes. That count excludes selection gathering, validation,
layout and rendering; it is not a measured whole-application speedup or a
hardware GPU/FPS/latency claim. No report data, grid points or visible strokes
are omitted to reduce work.

## Acceptance

Thirteen additional shared native/WebAssembly cases cover mixed/locked/nested
commands, exact fractional widths, invalid values, no-op history, drag guards,
field preservation and bounded replacement work. Three additional isolated
core-package checks execute public commands. An independent Uno package
consumer compiles the width selector and its event contract.

Two new real-input browser scenarios require exact saved reports, selection
and undo behavior, dynamic custom-row replacement, preset choices, mixed and
disabled controls, and actual green border pixels. All earlier scenarios stay
included: at least 72 cases across eight shards with zero retries/skips/flaky
results, plus the existing core, benchmark, desktop and WebAssembly gates.
The code/tests are not considered accepted until final-head CI completes.

## References and scope

FastReport documents border width/color/style and top/center/bottom text
alignment. These independently implemented controls expose the appearance
already supported by ReportScene; they do not supply per-side borders, dashed
or double lines, shadows, full font/style dialogs or complete FastReport parity.

https://www.fast-report.com/public_download/docs/FRNet/online/en/api/FastReport.Border.html
https://www.fast-report.com/public_download/docs/FRNet/online/en/api/FastReport.VertAlign.html
