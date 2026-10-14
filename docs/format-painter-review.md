# Native Format Painter

## Workflow and reference

FastReport .NET exposes a Format Painter designer command:
https://www.fast-report.com/public_download/docs/FRNet/online/en/api/FastReport.Design.Designer.html
The independent ReportScene implementation adds a native brush tool to the Home
Clipboard group. It is not vendor source, artwork or a claim of complete visual
identity. The reusable core remains independent of Uno and Skia.

Select one source object, choose Format Painter, then click an unlocked object
on the actual design canvas. The one-shot tool applies only ItemStyle and exits.
Double-click the tool for repeated painting; Escape cancels without clearing the
current selection. The active button and status text identify the source and
repeat mode. Locked targets do not consume the tool, while an equal editable
style consumes one-shot mode without creating an edit. Locked objects may be
sources because copying appearance does not modify them.

## Transactions, lifetime and cost

DesignerSession.ApplyFormatting validates the style and every explicit target
before mutation, rejects missing/ambiguous identifiers, copies the caller's
selection list and commits all changed targets in a single undoable operation.
Nested objects are supported. Only style is replaced: identities, bounds,
content, expressions, links, data, container children and lock flags remain.
Undo restores the prior objects and selection; redo owns its target identifiers.

DesignerFormatPainter retains one immutable ItemStyle reference and a captured
session revision, not a serialized report or copied data table. Its own repeat
applications advance the captured revision; external edits, undo/redo and even
reloading the same document object cancel it. A save acknowledgement or selection
change does not invalidate its content snapshot. The view owns and disposes the
tool and unsubscribes from session notifications.

A batch resolves targets with a hash set and one document scan, then uses the
existing one-pass replacement path, rather than searching the document for each
target. The 2,000-object regression requires one 2,000-object replacement scan
for 1,000 changed styles. This is a deterministic operation-count contract, not
an application, GPU, FPS, peak-memory or latency benchmark. The designer still
performs its normal layout/metadata refresh after a real edit.

## Acceptance

Seventeen shared native/WebAssembly conformance cases cover exact appearance,
content preservation, nested batching, locked/equal targets, invalid styles,
missing/duplicate identifiers, stale captures, drag state, disposal, undo/redo
and large batches. Four isolated core-package checks execute the public API; an
independent Uno package consumer compiles the view-owned tool.

Three actual native-input browser scenarios exercise the toolbar, canvas clicks,
double-click repeat mode, disabled mixed-source state, Escape, reload/external
edit cancellation, exact saved JSON and history. The first also requires the
actual green target pixels; native SelectedItem/state alone cannot pass it.
All earlier scenarios remain: the aggregate requires at least 70 tests across
all eight shards with zero retries, skips or flaky results. Existing core,
benchmark, package, desktop and actual WebAssembly gates remain unchanged.
Final-head results must be recorded separately after those jobs finish. Local
syntax/whitespace checks are not a substitute for native application acceptance.

## Boundaries

This copies the model's supported appearance fields. It does not implement a
named/shared style manager, inherited style links, conditional-style rules,
gradients/hatches or matrix-cell style policies. It does not copy source content
or arbitrary vendor properties. General docking, continuous preview, scripting,
inheritance, rich text, additional providers/formats and full FastReport parity
remain separate work.
