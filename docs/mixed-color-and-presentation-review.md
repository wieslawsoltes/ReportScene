# Mixed color and preview presentation review

## Mixed selection protection (#20)

Invalid or partial hex/channel input is a draft, not a completed color choice. ReportColorEditor now records HasUserChanges only after typed values parse successfully. Explicit palette, spectrum, transparent and original-color choices retain their previous behavior. Escape restores the last valid color. Thus invalid-only entry followed by Escape and Apply cannot flatten a mixed selection to its first object's color. Valid input is still applied as one session transaction; no-op and untouched Apply remain nonmutating.

The new native-input regression enters invalid hex and a channel value outside 0–255, attempts Apply, restores via a real Escape key event, and requires identical saved report content, revision and history. It then explicitly chooses the already-previewed color and requires both objects to change in one undo transaction. None of these operations calls a model command through test diagnostics.

## Preview assertion's presentation boundary

The retained failing run 38384065939 supplied both `whole-page-1600.png` and the later `test-failed-1.png`. The earlier screenshot still shows the pre-fit large paper with the footer below the viewport. The later failure screenshot contains the expected exact #305EA0 footer at x=435..462, y=894..915. Native diagnostic state already reported fitted paper bounds (401,154,547,773) and the correct full-paper paint viewport. A white-only screenshot poll accepted the old frame; the following immediate blue-footer assertion failed before the fitted frame was presented.

The test now polls the original white-paper requirement and exact footer RGBA together in the same screenshot, using fresh geometry on each attempt. The exact footer assertion remains, and neither timeout nor scenario retry policy changes. This is an assertion synchronization fix, not a production rendering correction or claim that unrelated historical browser stalls are fixed.

## Host clarity and local recovery

Print help now explicitly distinguishes browser print-dialog invocation from the desktop host opening HTML in the default viewer. Printer devices, duplex/tray selection and actual completion remain outside that host contract.

The recovered local source tree was independently reconstructed from the CI archive as `35e082b8669d8b0c1772661b0a80f860845c4dcf`, exactly matching pushed print head 9e011fda. Retained older archives remain unchanged. No editable checkout or untracked source directory existed on recovery; older snapshots are not applied over newer GitHub files. Changes in this continuation are separately committed, and current-source native/browser acceptance is required before merge.
