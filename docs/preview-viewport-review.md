# Preview keyboard and viewport regression review

## Observed failures

The first complete preview acceptance run [38246880941](https://github.com/wieslawsoltes/ReportScene/actions/runs/38246880941) passed 43 of 45 browser scenarios: all 42 earlier scenarios plus the new button/page-entry scenario. Two actual UI assertions failed: PageDown did not change the displayed page after clicking the paper, and the lower paper region remained gray after Whole page at a 1280 by 800 viewport. The second failure persisted through the existing 60-second screenshot assertion and its failure-hook screenshot; it was not the earlier first-frame full-paper test race.

The keyboard correction focuses the native ScrollViewer on pointer input and handles the preview's tunnelling PreviewKeyDown before the viewer consumes PageUp/PageDown. Ctrl+Home/End remain scoped to the preview; sibling page-entry/search TextBoxes retain editing semantics. The focused run [38253176953](https://github.com/wieslawsoltes/ReportScene/actions/runs/38253176953) then passed the button and keyboard scenarios, including repeated ribbon rebuilds, but still failed fitted paper. Its first infrastructure attempt stopped before any browser test because the exact app artifact was not published; it was restarted only after that artifact existed. This diagnostic run is not an acceptance substitute.

## Drawing error isolated by paint evidence

At 1280 by 800, native layout and the render callback both reported a 405 by 573 DIP paper element, a 963 by 613 viewport and zero horizontal/vertical offset. The render point scale was 0.6806114813099098. However, LocalClipBounds reported approximately (-1.4693,-1.4693,614.1536,802.2198) in points: its height represented only about 546 DIPs, not 573. The unchanged RenderCount and repeated PNGs showed that this was the retained drawing, not just a pending layout pass. At the initial 635 by 898 element another clip-query discrepancy also truncated the right edge.

ReportPreviewView passed LocalClipBounds into DrawIndexedPage as both the spatial query and an additional ClipRect. That turned an inaccurate recording-canvas clip query into missing paper and potentially missing report objects. It also treated a recording clip as if it were the actual ScrollViewer viewport.

The pinned Uno 6.7.135 implementation records each dirty visual using an SKPictureRecorder over approximately plus/minus 536,870,880 coordinates, then clips and replays the picture during composition. The observed boundaries align with coarse single-precision steps at those magnitudes. This supports a large-recorder clip-query precision explanation; it is not a claim that all Skia versions or platforms return the same values. The application correction does not depend on that implementation detail remaining unchanged.

## Correction and reuse

`ReportScene.Model.ReportPreviewViewport.Calculate` intersects actual paper-element bounds in viewport DIPs with the viewport, includes a two-DIP conservative guard and converts the result back to local report points. It is independent of Uno and Skia, validates finite/representable geometry and returns an empty region for absent or offscreen paper.

The Uno host supplies those bounds through TransformToVisual and the native viewport dimensions. The recording canvas's clip query remains read-only diagnostic data, not drawing or culling authority. The existing compositor still applies its physical element and ancestor clips. When layout is not measured yet, drawing falls back to the physical page; the measured size/view events invalidate that recording. Surface size, viewport resize and native scrolling invalidate the retained viewport drawing without an idle timer. Whole-page fitting and page changes reset offsets without animation.

The current-page PreparedPageIndex is retained and queried, not rebuilt on fit, zoom, no-op navigation or scrolling. A new native-input scenario measures actual candidate counts with 500 page objects at normal zoom, 400 percent zoom, after physical mouse-wheel scrolling and after fitting again. It also verifies stable index builds, report revision/history and serialized source. These are workload/correctness counters, not a hardware-GPU, FPS, memory-allocation or latency benchmark.

## Regression coverage and acceptance

Seven additional shared native/WebAssembly cases cover geometry, scroll conversion, empty/invalid bounds, indexed candidate reduction and pixel-for-pixel comparison between large-cull recorded drawing and independent direct page drawing at both affected scales. The recording cases include actual right-edge and lower-page colored objects; no giant raster surface is allocated.

The original lower-paper pixel assertion and deadline remain. The browser fixture additionally paints a colored footer and checks its exact RGBA values in the same actual screenshot; a white control background cannot mask missing report content. Button/page entry, keyboard navigation, two-size whole-page fitting and real scroll/zoom culling bring the browser suite to 46 scenarios. Independent package consumption now includes the new pure viewport API.

The complete suite is distributed across eight isolated runners, one browser worker per runner. The aggregator requires all eight shard reports and every scenario must have exactly one passing result, with no retries, skips, flaky outcomes or report errors. The extra parallelism changes scheduling only, not test selection or acceptance. Core, desktop, independent package consumers and the actual WebAssembly suite remain required. Final results must come from the final source revision; successful arithmetic/build checks alone do not establish native UI correctness.

## Primary references

- [Pinned Uno SKCanvasElement](https://github.com/unoplatform/uno/blob/da217df2312e9e39676880f9cb4b8d519cc297e0/src/AddIns/Uno.WinUI.Graphics2DSK/SKCanvasElement.cs).
- [Pinned Uno SKCanvasVisual](https://github.com/unoplatform/uno/blob/da217df2312e9e39676880f9cb4b8d519cc297e0/src/Uno.UI/Graphics/SKCanvasVisual.skia.cs).
- [Pinned Uno Visual recording and replay](https://github.com/unoplatform/uno/blob/da217df2312e9e39676880f9cb4b8d519cc297e0/src/Uno.UI.Composition/Composition/Visual.skia.cs).
- [Skia canvas clip-query contracts](https://api.skia.org/classSkCanvas.html).
- [Skia picture recorder and cull bounds](https://api.skia.org/classSkPictureRecorder.html).

This remains a single-page preview implementation, not continuous multi-page rendering, thumbnails, complete FastReport parity or a claim to have audited every renderer/control.
