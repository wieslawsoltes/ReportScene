# Designer fidelity and authoring review

This increment moves the actual Uno/Skia designer toward the publicly documented FastReport .NET workspace. It is independently implemented, not a vendor skin or copied artwork, and does not claim complete visual or behavioral parity.

## Workspace and editing

The title/quick-access area, tab strip, grouped ribbon, left object toolbar, gray paper workspace, bottom report-page tabs and right-side tool windows use a denser layout. Formatting uses compact B/I/U controls rather than word-sized buttons. The Data/Report Tree tabs sit below the explorer; the property pane has a caption, object selector, category/alphabetical controls, filter, two-column rows and a description area. The status bar has live zoom controls. Large ribbon buttons fit inside their group rows.

Formatting reflects the actual selection, including custom font/size values and mixed B/I/U state. Programmatic synchronization does not write a report edit. A mixed formatting toggle enables every editable object; only an all-enabled editable selection turns off. Locked objects retain their formatting. Generic multi-object properties explicitly say that displayed scalar values come from the first editable object; they do not pretend that every scalar editor is a full mixed-value editor.

The side and tree/property dividers accept pointer/touch dragging and keyboard arrows. Escape during a divider drag restores the prior size, and double-click resets the layout. Tool panes can be closed independently and restored through **View → Panels**; Reset layout restores both. Sizes are constrained to keep working space available. This is resizable tool-window layout, not arbitrary docking, floating OS windows, persisted pane layouts or the full vendor docking manager.

**View → Rulers** selects millimeters, centimeters, inches or points. Position/size, padding, shape radius and table-row-height editors use the selected display unit. Font sizes and border widths remain explicitly in points; specific matrix/subreport configuration editors retain their explicitly labeled point units. All document geometry remains in points, independently of the view setting. Unit changes do not dirty the report. Snap spacing remains a point-based setting. Rulers and grid marks limit work to the visible range; rulers still scroll with the authoring surface.

Eight visible corner/edge handles resize with the opposite edge anchored. Minimum sizes and origin clamps prevent inversion and negative positions. A drag is one undo transaction, Escape restores the originals, and locked objects cannot be moved/resized. For multiple objects, resize applies the same directional delta to each editable object; affine scaling of a shared group rectangle is not implemented. Lock/unlock is reversible, and no-op or fully locked reorder operations do not create dirty history entries.

## Reusable tree and property controls

`DesignerOutline` is a parent-first immutable metadata snapshot, independent of Uno and data rows. It validates keys/parents, keeps nested band ownership, caches the most recent filtered projection, preserves ancestor paths for leaf matches, and includes descendants when a parent matches. Cancellation leaves the prior cached result intact. Report and data outlines include source/field, system-variable and parameter entries; inserted field expressions are qualified to avoid reserved-name collisions.

`ReportOutlineView` uses native TreeView nodes keyed by metadata identity. Unchanged edits retain nodes; a rename updates labels without recreating the whole tree. Filtering temporarily reveals ancestors/descendants and clearing the filter restores the previous unfiltered expansion. The current Data/Report switch replaces the active outline; separate per-tab expansion/search persistence is not claimed. The object selector navigates pages, bands and items through the normal designer session.

`PropertySheet` remains usable independently. It adds compact rows, filtering, category expansion, alphabetical sorting, descriptions, finite numeric input, Escape restoration and rollback of invalid choice/check-box changes. `DeferRefresh()` batches projection updates. Rebuilding the sheet expires old editor generations; the designer additionally captures its document/page/band/selection target before creating editors. Delayed focus/commit events cannot apply to a replacement selection. Values update only after a successful session mutation.

`PaneSplitter`, `DesignerGeometry`, outline snapshots, batch session operations and the new control settings are covered by package-only consumers. Core packages have no Uno dependency. Native browser interaction, not package compilation alone, exercises the actual UI behavior.

## Performance and correctness changes

Multi-object replacement previously restarted a full document scan for every item. Replacement now validates all new objects first, resolves destinations in one traversal and applies the batch only after all destinations exist. Move, property edits, drag frames, commit/cancel and undo/redo use that path. Selection gathering still takes a traversal; the replacement diagnostic counter intentionally does not hide that distinction. Identity changes, non-finite/invalid bounds and newly colliding names fail before mutation/history recording.

Delete and paste-undo use per-band removal with a set of IDs instead of scanning the same band for every selected item. Paste reserves names against both existing objects and earlier names in its own batch. Pathological repeated prefixes can still require repeated suffix probing; this is not a guarantee of constant-time name allocation for every input.

A repeated snapped drag frame does not send another change notification when geometry is unchanged. Authoring layout snapshots are reused for selection, normal saves and zoom; edits/page changes refresh them. Completing even a no-op pointer interaction refreshes property targets after the transient drag ends. The existing queued-status generation guard is retained so a property refresh cannot erase a newer save outcome.

Explorer entries use structural record equality instead of an ambiguous concatenated string fingerprint, including parameter names and band kinds. Native nodes and unchanged page tabs are retained across nonstructural edits. Metadata snapshot creation and comparison still traverse the report on relevant session notifications; this is not a fully incremental document graph. Large native object-selector lists, text wrapping during authoring draws, aggregate/lookahead preparation and broad rendering stress profiles remain review targets.

The shared renderer creates one dashed-outline effect per authoring draw rather than one per object. Grid and ruler loops clip to the viewport, but prepared preview rendering still uses the previously implemented page index. CI's software WebGL rendering is not evidence of hardware GPU, FPS or input-latency gains.

## Validation and evidence

`DesignerGeometryTests`, `DesignerBatchTests` and `DesignerOutlineTests` are registered in both native and actual WebAssembly conformance. They cover eight handle directions, boundaries, invalid values, atomic failures, 20,000-object batch work, undo/redo/cancellation, locked/no-op edits, colliding names, hierarchical filtering, cached queries, cancellation and structure equality.

Six additional Playwright scenarios use real mouse/keyboard/file input to test pane sizing/cancellation/restoration, compact row geometry, nested-tree filtering and native selection, stable nodes, synchronized mixed formatting, property validation/Escape, display-unit round trips, all eight handles, locking, and authoring-layout reuse. The existing twenty-three scenarios remain. The merged-evidence gate requires at least twenty-nine passing scenarios including all six new scenario families, with no retries, skips or flaky results. Screenshots and state artifacts are evidence of the actual running application, not generated mockups or pixel-identity certification.

Benchmarks record replacement lookup work for 1,000 and 10,000 objects against the preceding per-item scan algorithm and check exact object equality. Current complete headless Move timing includes selection, validation and history; no historical whole-command timing ratio is inferred from lookup counts. Outline construction, construction-plus-first-query and 1,000 cached-query calls are recorded separately for 20,000 objects. Timing/managed allocation observations use the standard warmup/three-repetition harness and are not timing-based CI pass thresholds. Consult the final commit's `core-validation` artifact for measured results and its full browser aggregate for acceptance, not a superseded intermediate run.

## Remaining parity boundaries

Rich-text/HTML objects, report inheritance, arbitrary C#/VB.NET scripts/events, dialog pages, native FPX, the full provider/office-export catalog, advanced subreport options, drag-to-place toolbox tools, true docking/floating windows and full original-designer matching remain outstanding. FRX translation is unchanged. This increment is a focused authoring/UI review, not exhaustive defect elimination or production certification.

References: [FastReport .NET designer manual](https://www.fast-report.com/public_download/docs/FRNet/online/en/UserManual/en-US/Fundamentals/TheReport/TheDesigner.html), [FastReport report-tree search and expansion](https://www.fast-report.com/blogs/improved-interface-report-tree-fastreport-designer), [WinUI TreeView](https://learn.microsoft.com/en-us/windows/apps/design/controls/tree-view), [Playwright sharding](https://playwright.dev/docs/test-sharding).
