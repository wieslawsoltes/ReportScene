# Designer options, persistence and responsive panes

The designer has a native **Designer Options** dialog, available from the quick-access settings button, **File → Designer options** and **View → Settings → Options**. This independent implementation brings everyday configuration closer to a desktop report designer; it is not a complete FastReport settings or visual-parity claim.

## Options and report independence

Three compact tabs group Workspace, Grid and rulers, and Display. Options include preferred sidebar width and tree height, pane visibility, initial Data/Report Tree tab, grid visibility and point spacing, snapping, rulers, geometry display units, full-paper versus compact-band authoring, alphabetical properties, and separate design/preview zoom.

The editor holds a draft. **Apply** parses every field and validates the whole record before applying it. **Cancel** discards the draft. **Restore defaults** changes only the draft and leaves the dialog open; it becomes effective only after Apply. A delayed preference load or another externally applied preference change cannot silently be overwritten by an old open dialog. An explicit Apply can repair invalid stored preferences even when active values already equal defaults.

Preferences never contain report content, paths, source data, credentials, selection or undo history. Changing options does not change report bytes, dirty state or prepared-report semantics. Display units affect geometry editors, not the point-based document representation. The matrix/subreport editors retain their explicitly labelled units. Filters and expanded tree nodes remain per-document in-memory view state rather than persisted preferences.

## Preferred versus visible geometry

`DesignerPaneLayout.Calculate` is a pure layout calculation. Preferred sizes and user-selected visibility are distinct from the currently visible layout. Shrinking the viewport can reduce pane sizes or temporarily suppress panes without overwriting those preferences. Growing it restores the requested extents.

The calculation reserves a 33-pixel object toolbar, a 4-pixel divider and at least 240 logical pixels for the canvas. It suppresses the sidebar when less than 180 pixels remain for it. In a short workspace, Properties takes precedence over the tree when their combined minimum heights would not fit. A single visible pane fills the available height. Tree and sidebar maxima remain 480 and 640 respectively. This is responsive tool-pane layout, not mobile/accessibility certification or arbitrary docking/floating windows.

Splitter dragging updates visible geometry, but saving waits for a completed interaction. Escape restores the pre-drag preference, including when that preference was larger than the viewport's temporarily constrained size. Viewport-only coercion does not issue a preference change or write. Keyboard splitter changes, pane close/restore, zoom, sort, grid/rulers and snap changes all reach the event-driven settings observer. The snap checkbox previously lacked that notification; it now publishes it.

## Reusable contracts

Core APIs in `ReportScene.Designer` are independent of Uno:

```csharp
var settings = new DesignerPreferences { SidebarWidth = 520, Unit = DesignerUnit.Millimeters };
string json = DesignerPreferencesJson.Serialize(settings);
var visible = DesignerPaneLayout.Calculate(settings, viewportWidth: 1280, viewportHeight: 720);

using var preferences = new DesignerPreferenceSession(store);
await preferences.LoadAsync(cancellationToken);
preferences.Update(preferences.Current with { ShowGrid = false });
await preferences.FlushAsync(cancellationToken);
```

`IDesignerPreferenceStore` loads/saves a small JSON string. `DesignerPreferenceSession` retains immutable current values and generations, coalesces queued changes and serializes writes. New choices made before or during an initial load win over the loaded result. Each load is attempted once per session. Changes arriving during an awaited save are drained afterwards; a host-supplied readiness predicate is rechecked before every snapshot. Failed/cancelled writes remain pending for an explicit retry or the next user-triggered save, without an idle retry loop.

The service protects its state with a lock but raises events on the calling context. UI hosts must call it on their dispatcher and synchronize their view updates there; it is not a cross-thread UI dispatcher. Multiple independent application instances use last-successful-writer semantics, not conflict-free settings synchronization.

The existing two-argument `ReportDesignerControl` constructor retains in-memory options. A three-argument overload accepts a preference store without adding requirements to existing `IReportWorkspaceHost` implementations:

```csharp
var designer = new ReportDesignerControl(workspaceHost, report, preferenceStore);
await designer.RestorePreferencesAsync();
// Before a controlled host close, if that host can await it:
bool acknowledged = await designer.FlushPreferencesAsync();
```

`DesignerOptionsEditor` is a separately reusable native control with `LoadValue`, `ReadValue`, `RestoreDefaults` and read-only editor metadata. Its methods do not operate on a live report. `PaneSplitter` exposes interaction start/completion and drag state for hosts that defer work during input.

## Storage and validation

The browser app uses origin-local storage key `reportscene.designer.preferences.v1`. The desktop app uses a separate `reportscene-preferences.v1.json` in the application's local folder. Neither path shares report recovery. The browser checks the length before transferring stored text into .NET and catches quota/security exceptions. The reusable native file store reads bounded strict UTF-8, writes a sibling temporary file, then replaces the destination; incomplete temporary writes are not treated as successful settings.

The JSON codec is source-generated, versioned and bounded to 8,192 UTF-16 characters. It rejects duplicate/unknown fields, absent or unsupported versions, invalid enum values, nonfinite numbers, out-of-range settings and excessive nesting. Missing optional fields explicitly retain defaults. An initial CI regression found that source-generated init-only deserialization supplied CLR zeroes for omitted properties; normalization now fills only absent fields from the serialized default record. Explicit nulls/invalid values remain errors, and the original regression assertion is retained.

Sidebar width is 180–640, tree height 96–480, grid spacing 1–72 points, design zoom 15–400%, and preview zoom 10–400%. These are supported persisted-option ranges, not a redefinition of every headless reporting API's bounds. Hosts may supply their own store or omit persistence entirely.

## Performance and lifecycle limits

The Uno observer does fixed-size comparisons and allocates no settings record on unchanged status, selection or view notifications. It does not traverse report data. A 400-millisecond one-shot-style dispatcher timer coalesces user preference changes; the timer is stopped during pane dragging. Saving through the options dialog explicitly awaits a flush. Failures are retained and surfaced without blocking report editing. Report-recovery writes remain independently coordinated.

The counting-store benchmark checks 1,000 queued changes coalescing to one acknowledged write, 10,000 unchanged updates issuing no events, and 10,000 pure viewport calculations. These exclude storage latency and native rendering; they are not a historical whole-UI speedup, GPU/FPS or input-latency claim. Real file IO is covered by tests, not inferred from that benchmark.

Abrupt browser/process termination may lose changes still inside the debounce window. `Dispose` cancels outstanding work; it cannot guarantee an asynchronous flush. A controlled host can await `FlushPreferencesAsync`. Successful localStorage or file-replace acknowledgement is not a physical-disk durability guarantee. This increment does not add cross-tab synchronization, persisted dock arrangements, undo for application preferences, or remote settings accounts.

## Acceptance evidence

Shared native/WebAssembly tests cover schema/defaults/limits, pure resize/restoration behavior, delayed loads, overlapping writes, errors, readiness, cancellation and disposal. Native-only tests exercise actual file replacement, malformed UTF-8, limits and cancellation; browser conformance instead checks explicit filesystem-store rejection. The isolated core consumer exercises generated packages including persistence, and the separate Uno consumer compiles the options editor, optional-store designer and interaction APIs.

Six additional Playwright scenarios operate actual Uno controls and real browser storage: draft/cancel/defaults/invalid Apply; atomic application and reload; preferred versus visible pane sizes and cancelled constrained drags; corrupt storage and explicit repair; quota failure and explicit retry; and direct View/property/pane settings notifications. Report bytes/history are compared. Test-only storage fault injection alters the host API, not report state or application command dispatch. All preceding browser scenarios remain required; aggregate acceptance requires at least 42 passing scenarios and zero retries/skips/flaky outcomes. Final counts and deployment outcomes are recorded only after the current-head workflow finishes.

Rich-text/HTML objects, arbitrary scripts/events, report inheritance, dialog pages, native FPX, the full provider/export catalog, complete docking/floating and original-designer parity remain outstanding. This is a targeted preferences and pane-lifecycle review, not exhaustive defect elimination. FRX translation and the report engine/export formats are unchanged.

References: [System.Text.Json unmapped-member handling](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/missing-members), [FastReport designer manual](https://www.fast-report.com/public_download/docs/FRNet/online/en/UserManual/en-US/Fundamentals/TheReport/TheDesigner.html), [Uno control source](https://github.com/unoplatform/uno).
