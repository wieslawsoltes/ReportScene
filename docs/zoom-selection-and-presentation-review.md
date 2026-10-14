# Selected zoom values and presented browser pixels

## Evidence retained

The fitted-zoom branch's run 38399679062 did not pass. In its native-input
browser shard 8, the authoring selector remained at `95%` after the actual
scale became `1.2847970095323868`; the preview selector retained `80%` while
the scale became `0.6886291558279585`. Both use the same additional item slot.
The trace continued to publish diagnostics: this was not an absent report or
merely a guessed preset index.

Replacing an item at an unchanged selected index can leave the native
ComboBox's SelectedItem (and its painted text) on the old value. The reusable
ReportZoomSelector now clears the selection under its existing synchronization
guard before replacing that display item, then selects the replacement.
Preset order, exact double scales, independent authoring/preview ownership,
no-op feedback suppression and the bound of one extra row are unchanged.
The existing native-input scenarios require both a real selected index and
matching percentage text after repeated fitted/non-preset changes. They still
check unchanged source, history, layout and prepared-index ownership.

A separate failure in run 38443712672, browser shard 2, observed zero dark
font-size pixels. Its retained `visible-font-size.png` is the *old Professional
invoice frame*, while the later failure-hook screenshot displays the loaded
Designer fidelity contract and `14` in the selector. Read-only model/native
geometry had advanced ahead of the presented frame. The font-size test now
observes fresh screenshots against the original 16-pixel-wide crop, dark
threshold, minimum count and existing assertion deadline. It does not replay
input, retry a failed scenario, accept an empty selector, or claim a production
font-renderer correction.

## Browser observation work

The remaining color, font, paper, outline and stroke pixel checks use the
existing Node-side screenshotPixels reader. Actual browser screenshots remain
the evidence. The tests no longer base64-upload their screenshots to the
application page, decode them into an HTML image, create a second canvas and
transfer pixel arrays back through page.evaluate. All original coordinates,
color tolerances and required nonblank/content checks are retained. The reader
already has its own required PNG tests in CI.

This removes unnecessary test-inspection work from the application's browser
thread. It is not a measured application/GPU/FPS improvement. The retained
trace contains a 43.5-second browser-side PNG decode, but does not prove a
single root cause for every CI/browser scheduling or screenshot delay.

## Integration and acceptance

The export-work branch incorporates the zoom branch with both parents and
retains the newer print/color diagnostic and Node-side PNG changes. All 11
export-work regressions and all five earlier data-only regressions are
registered. All 67 browser scenarios, eight shards, core/package/desktop and
WebAssembly gates remain required, with zero retries, skips or flaky results.
No earlier failed run is relabeled as accepted. Final-head acceptance must be
recorded separately after the combined tree completes those gates.

Full FastReport functional/visual parity, continuous preview, complete style
management/docking, arbitrary scripting, inheritance, rich text and additional
provider/export formats are not supplied by this correction.
