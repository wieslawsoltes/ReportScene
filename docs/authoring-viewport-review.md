# Authoring viewport and stroke culling

The preview surface was already corrected to derive its viewport from actual native layout rather than the recording canvas clip. Source review found that the design surface still used `canvas.LocalClipBounds` for grid, ruler and object culling. The same recording/composition distinction applies there: the recorded clip does not reliably represent the visible scrolled native element. A fixed six-DIP tolerance also ignored thick report borders extending beyond an object's nominal rectangle.

## Implementation

The authoring surface transforms its actual origin into its ScrollViewer, combines actual dimensions with native viewport dimensions, and uses the shared `ReportPreviewViewport.Calculate` conversion into report points. Native surface size, viewport size and scroll changes invalidate the rendering. No idle polling, document mutation or extra preparation is introduced. Before native layout is available, drawing conservatively considers the full authoring extent rather than an untrustworthy recording clip. The native compositor still performs final clipping.

`DesignerPaintCulling` is a reusable headless predicate over a `DesignObject`, point viewport and point-to-DIP scale. It accounts for half the report border width, renderer-owned checkbox and line strokes, and six DIPs of selection-adornment tolerance. Checkbox outlines scale with their side even when BorderWidth is zero; line strokes have a renderer-owned minimum. Invalid input geometry/scale/border values are rejected, and empty viewports emit no report objects. Overflow of an expanded conservative edge can only retain extra content, not accidentally discard a visible stroke.

The actual canvas uses that predicate for report items and selection overlays. Text wrapping and grid iteration use the native visible range; existing wrapped-text caching and painter order are unchanged. Selection navigation uses the existing native ChangeView with animation disabled, so selection target geometry is not deliberately moving while the property grid updates. Reporting model and prepared-report/export behavior are unchanged.

## Validation

Seven shared native/WebAssembly cases cover four border edges, renderer-owned strokes, zoom-scaled selection tolerance, empty viewports, distant scrolled geometry, lower/right paper content and invalid input. The isolated Uno package consumer compiles the public surface viewport/drawn-count and headless predicate APIs. It is not a substitute for interactive tests.

Two additional browser scenarios preserve all preceding 49 scenarios and require a 51-scenario aggregate across all eight mandatory shards. They load a real report, navigate with native selectors and wheel input, and check exact colored pixels at 50%, 85% and 400% zoom. They require fewer submitted objects at a distant viewport, retain authoring layout during scrolling, and compare saved report content/history. A second scenario scrolls a thick red border's nominal rectangle fully outside the native viewport and requires the extended border to remain visibly painted inside it. Read-only diagnostics report actual draw counts and point viewport; they do not invoke application commands or synthesize pixels.

These are correctness and work-selection changes, not a new spatial index: the design surface still scans the object list to choose visible items and scans it again for selection overlays when needed. The predicate performs input checks on each call. No whole-application timing ratio, hardware GPU/FPS, input-latency or native-memory improvement is claimed. Current-head CI results must pass before merge; staged source and model-only checks are not browser acceptance.

The task remains an independent reporting implementation, not complete FastReport visual/functional parity. Native docking/floating, report inheritance, arbitrary scripts/events, rich-text/HTML objects, dialog pages, FPX and the full provider/export catalog remain outside this correction.

References: [Uno SKCanvasElement source](https://github.com/unoplatform/uno/blob/master/src/AddIns/Uno.WinUI.Graphics2DSK/SKCanvasElement.cs), [WinUI ScrollViewer.ChangeView](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.scrollviewer.changeview).
