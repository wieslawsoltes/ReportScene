# Explorer state, selection and navigation review

This increment follows the merged compact-designer work in PR #7. It addresses independent explorer tabs, keyboard selection, unnecessary metadata reconstruction and a mismatch between visible selection handles and pointer hit testing. It is not a complete FastReport parity or hardware-GPU certification.

## Independent native explorer contexts

`ReportOutlineView.SetOutline(outline, contextKey)` retains a native tree, current filter, selection and expansion for each named context. The existing one-argument method still updates the active context. The designer uses `Data` and `Report`. Switching tabs does not recreate their unchanged nodes or discard their filters. Search continues to include ancestor paths; clearing it restores that context's unfiltered expansion. Equal delayed TextChanged events cannot rebuild a just-restored context. Escape inside search clears that filter rather than clearing the report's object selection.

Contexts retain native TreeViews and their metadata, so memory is proportional to the retained contexts and entries. The reusable control limits contexts to eight, validates names and offers `ResetContexts()` to release them. The designer resets both contexts when another document is loaded, including a reload of the same object. Normal edits, undo/redo, saving and page navigation keep the loaded-document identity and do not reset the explorer. These are in-memory view settings, not persisted user settings or arbitrary docking/floating windows. Earlier review documents describing missing per-tab search/expansion are superseded by this increment.

Native report-tree selection changes now request page/band/object navigation, including Arrow keys. Data-tree selection changes do not insert fields; insertion remains an explicit invocation. Programmatic tree selection, filtering, context switches and node updates are guarded against selection feedback. Native node-only drag reordering is disabled because it would change the visual tree without changing the report/history. Report-aware drag reparenting remains unimplemented rather than pretending that a visual-only move edits the report.

## Indexed metadata and skipped work

`DesignerOutlineProjection` retains an immutable filtered list and an ordinal key-to-index dictionary. Construction and storage are linear in projected entries. `ContainsKey` and `Find` use dictionary lookups instead of scanning the projection for each selection synchronization. Cancelled construction cannot publish a partly built projection. Case-sensitive identity, parent-first ordering and metadata ownership remain unchanged.

`DesignerSession.DocumentId` exposes the existing loaded-document generation without changing content or serialization. It is stable for edits, undo, saving and navigation; `Load` issues a new identity. `DesignerActivityTracker` now requests structural refresh after committed content changes or restored drag state, not after a save acknowledgement or pure page navigation. Selection/property/tab status still updates through the existing selection events. Report/data metadata generation and diffing still traverse the structure on real edits; this is not a fully incremental document graph or a promise that all selection work is constant time. Direct mutation of session-owned report lists still bypasses the session's notification contract.

The diagnostics distinguish metadata snapshot builds from native node creation/updates. Browser checks require no new snapshots/nodes on save or page navigation. The benchmark compares the previous ordinal membership scan and the indexed API over the same 20,001-entry metadata and 1,000 present/missing/case-mismatched queries, with construction measured separately. It records reference entry comparisons and indexed API probe counts; API probes are not internal hash-table string-comparison counts. Timings and managed allocation observations exclude native row realization, property refresh, drawing and browser input latency.

## Selection handles and pointer correctness

The surface formerly painted handles for every selected object but tested only the last selected object's bounds. `DesignLayout.HitSelectedHandle` now tests every editable selected object in reverse painter order, with the same eight-handle geometry and screen-scaled tolerance. Locked objects remain noneditable. Overlapping handles choose the frontmost matching editable object. All resizing continues through the existing atomic session transaction; multiple objects receive the same directional delta, not affine scaling of a group bounding box.

Selection outlines/handles are drawn as a final overlay after report objects, so a later filled object cannot obscure an actionable selected handle. The overlay pass runs only when a selection exists and still scans objects; it does not claim an asymptotic rendering improvement. The handle enumeration array is retained instead of allocated for each selected object. Ordinary object hit testing walks the existing list backwards rather than allocating a LINQ reverse buffer.

Small pointer jitter inside an object is ignored until physical displacement exceeds three device-independent pixels. A two-pixel click wobble with snapping disabled therefore does not move objects or create an undo entry. Explicit resize handles retain their existing precise delta behavior, and a real group drag still preserves the group selection.

## Validation

Nineteen additional shared conformance cases cover loaded identity, skipped save/navigation work, restored drag refresh, immutable/cancelled/case-sensitive/large indexed projections, all eight handles on the first selected object, painter order, locking and invalid hit inputs. Five additional package-consumer assertions bring the isolated core consumer to 29 checks; the independent Uno consumer compiles named-context, search, reset and native-navigation APIs.

Four additional native-input Playwright scenarios exercise independent filters/expansion and retained nodes, report-versus-data keyboard navigation, first-selected-object resizing with undo and click jitter, and save/navigation/new-document context lifecycle. They preserve all preceding 32 scenarios. The aggregate requires at least 36 scenarios and every named family across all four shards, with zero retries/skips/flaky outcomes. No result is counted until its current-head run completes.

Public references: [FastReport designer manual](https://www.fast-report.com/public_download/docs/FRNet/online/en/UserManual/en-US/Fundamentals/TheReport/TheDesigner.html), [report-tree search and expansion](https://www.fast-report.com/blogs/improved-interface-report-tree-fastreport-designer), [WinUI TreeView](https://learn.microsoft.com/en-us/windows/apps/design/controls/tree-view), [Uno TreeView properties and native drag defaults](https://github.com/unoplatform/uno/blob/master/src/Uno.UI/UI/Xaml/Controls/TreeView/TreeView.Properties.cs).

Rich-text/HTML reporting, arbitrary scripts/events, report inheritance, dialog pages, native FPX, the complete provider/export catalog, persisted docking layouts, toolbox drag placement, full scalar mixed-value editing and original-designer pixel identity remain outstanding. FRX translation and reporting/export semantics are unchanged by this increment.
