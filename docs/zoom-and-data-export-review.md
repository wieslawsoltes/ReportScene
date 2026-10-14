# Zoom presentation and data-only export review

## Native zoom presentation

The old ribbon ComboBox contained only ten presets. A fitted or restored non-preset scale could therefore show a blank selection, and Fit or status-bar zoom commands updated the canvas without synchronizing the ribbon field. The replacement reusable ReportZoomSelector presents the same ten preset positions plus at most one current non-preset row. The familiar compact native ComboBox remains in use. Existing preset indices stay stable.

SetZoom updates presentation without requesting another zoom. User preset choices raise ZoomRequested; the host owns and acknowledges the actual scale. The current non-preset row stores the unrounded double: selecting a displayed 68.86% does not round a fit such as 0.6886291558279585 to 0.6886. Formatting is invariant and limited to two decimal percentage places. Current-row replacement is bounded rather than accumulating every visited zoom. Delayed equivalent selection notifications do not feed back into the host. The designer guards removed controls by identity and synchronizes both independent view scales alongside its status bar.

Two native-input browser regressions exercise presets, Fit, status buttons, fractional-current-row selection, viewport changes, mode transitions, screenshots, stable report layout/index ownership, and unchanged saved report content/history. The prior 65 scenarios remain mandatory; the aggregate now requires 67 with all eight shards and no scenario retries/skips/flaky results. The standalone Uno package consumer compiles the reusable selector API.

This corrects a concrete blank/stale UI field; it is not a claim that the complete designer matches FastReport visually or that custom free-form zoom input is implemented.

## Data-only CSV work

The ordinal-selection exporter previously called selection.Apply even for CSV. That snapshots selected page references despite CSV consuming only bound tables. The CSV branch now validates All mode, the original page count, options, cancellation and report diagnostics without copying or enumerating the unrelated page list. Existing data serialization and non-CSV selection behavior are unchanged. Legacy physical range filtering is unchanged.

Five shared native/WebAssembly tests use a count-only page list whose element reads and enumeration throw. CSV succeeds with page counts from zero through int.MaxValue and reports zero element access. Stale counts, Current/custom selection, cancellation, absent data and report errors still fail. Ordinary CSV output bytes match the pre-existing exporter. This demonstrates removal of page-proportional work and page-reference storage on this path, not elimination of CSV output allocation or a measured whole-UI/GPU speedup.

All source is independently committed. Current-head native, package, desktop and full browser validation is required before merge. No older artifact or unchanged-head retry substitutes for these checks.
