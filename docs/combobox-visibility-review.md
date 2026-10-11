# Native dropdown visibility and keyed tree captions

The `83f7504` validation run passed 573 native checks, 568 actual WebAssembly checks, all package/build gates and 30 of 32 browser scenarios. It was **not** acceptance: sample selection and font-size selection failed. The complete run and its failure traces remain available as [run 38170565437](https://github.com/wieslawsoltes/ReportScene/actions/runs/38170565437).

## Popup geometry, not another timing allowance

The trace showed a 25-pixel-high closed sample ComboBox, 36-pixel popup rows and a popup limited to roughly 225 pixels. The last two realized items were outside its scroll viewport. Diagnostics incorrectly published their full bounds, and the test clicked an invisible row, dismissing the popup without selecting anything. Waiting for stable coordinates could not fix that geometric error. The font-size failure had the same cause.

Uno's `ComboBox.custom.cs`, `DropDownLayouter.Measure`, caps the managed popup by the lesser of visible space, `MaxDropDownHeight`, and nine times the closed control height. Merely increasing `MaxDropDownHeight` is therefore insufficient for a compact toolbar. [Official Uno implementation](https://github.com/unoplatform/uno/blob/master/src/Uno.UI/UI/Xaml/Controls/ComboBox/ComboBox.custom.cs).

`ReportComboBox` retains the native ComboBox template, selection, scrolling and keyboard behavior while using 22-pixel native item containers. The designer's font/size, units, zoom, sample and object selectors use it, as do property-grid choices. Short lists now fit their compact hosts; longer lists still scroll normally.

Read-only popup diagnostics intersect realized item rectangles with the popup's scroll presenter, border and screen. Fully clipped rows are no longer advertised as clickable. The browser driver delivers real wheel input inside the popup until the requested item is visible, then requires stable published geometry and clicks once. It does not set selection through diagnostics or retry failed assertions. The formatting test preserves its original 16/14-point checks and additionally selects the last 48-point entry through actual scrolling, then checks undo.

## Retaining nodes without retaining obsolete labels

Review of an actual compact-workspace screenshot also found old invoice data captions still painted after loading the new fixture. Metadata and node identities were current, but the realized native row content was stale. Keyed outline updates now refresh an existing `TreeViewItem.Content` when its node label changes, without discarding the node or its expansion/selection state. Newly realized rows continue to use the updated node content.

Browser diagnostics read the real container content, not a second copy of the model label. Tests check that a report replacement changes visible table/field captions and that renaming an object changes its realized tree caption while preserving node counts. Screenshots remain review artifacts; this is not a claim of exhaustive pixel identity.

## Acceptance and limits

The same published WebAssembly application is now tested across four isolated one-worker shards instead of three, reducing the imbalance that previously put the longest editing scenarios on one runner. Every shard remains required, and the aggregate requires four reports, all 32 scenarios, no retries, skips or flaky results. Per-test assertions and timeouts are not weakened.

The native/core/package totals are not inferred from another revision. Final acceptance, source-tree identity and deployment status must be read from the completed final-head run. These changes correct concrete UI bugs; they do not complete FastReport parity, arbitrary docking, accessibility qualification or hardware-GPU benchmarking.
