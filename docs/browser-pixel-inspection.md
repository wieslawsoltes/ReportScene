# Screenshot inspection without application-renderer roundtrips

Run 38396564664 was not acceptance. It completed 745 native and 735 actual WebAssembly conformance checks, but three browser scenarios failed: the mixed-color regression observed a missing empty validation label; authoring viewport checks reached the overall test deadline while inspecting a screenshot; and the bookmark replacement scenario timed out awaiting a Report-tab command. These failures are retained in that run. The complete cause of every responsiveness stall is not established by these changes.

## Real native text versus clickable geometry

`DesignerSnapshot` previously published a control's value only when that control had a positive-size hit rectangle. A loaded empty validation TextBlock can have zero width. It therefore disappeared from Inputs precisely when its text was cleared, conflating an empty native message with a missing/unloaded element.

The diagnostic now publishes actual TextBlock.Text for loaded, non-collapsed native text elements even when they measure zero area. It does not create a clickable rectangle for them, and unloaded/collapsed elements remain excluded. The mixed-color regression still requires the exact empty string, successful Apply, unchanged saved report/revision/history, and explicit-color behavior. The assertion is not changed to treat any missing field as success. Actual input and report changes remain native UI operations.

## Test-process decoding

The common screenshot helper previously sent each screenshot back to the application as base64, decoded it into a browser Image, copied it onto a detached Canvas and read its pixels. Authoring/preview checks then repeated that work for their exact-color assertions. Earlier retained traces documented long image-decode and scalar-evaluation times. Although detached elements did not mutate report state, this inspection still consumed the application's rendering process, memory and bridge bandwidth.

`tests/browser/screenshot-pixels.mjs` now decodes the actual captured PNG Buffer in Node with pinned pngjs 7.0.0. Header/dimension budgets and CRC checks guard inspection. A WeakMap reuses decoded pixels for the same retained Buffer; no report, page or browser object is retained. Exact pixel access floors the same coordinates, copies RGBA bytes and rejects out-of-bounds coordinates. No expected image or model-rendered replacement is synthesized for application assertions.

The generic nonblank-screen guard keeps its 160-by-100 sampling grid, five-bit RGB quantization and greater-than-30 threshold. The Node resampling is explicitly defined as bilinear sample centers; it is a coarse diversity check, not an assertion that its interpolation is byte-identical to every browser Canvas implementation. Five separate Node tests require blank/sparse splash rejection, dense-fixture acceptance, exact RGBA/copy/ownership behavior and invalid-coordinate/header/CRC rejection. They are mandatory before each browser shard and are not counted as native-input browser scenarios.

Authoring distant-object/border tests and preview fit/footer tests use the same decoded Buffer for their original exact points and tolerances. The original blue-footer and white-paper conditions must still hold in the same presented screenshot. All previous viewport sizes, zooms, scrolling, native controls, report/history/index checks, 60-second assertion deadlines, 420-second test limits, eight shards and zero-retry policy remain unchanged. Other custom pixel helpers may still perform an additional browser decode; this increment does not claim to have eliminated every inspection cost.

## Acceptance and limits

These are diagnostic correctness and test-process work-placement changes, not a new renderer feature or proof that all application stalls are fixed. Full current-head native/package/desktop/WebAssembly and 65-scenario browser acceptance is required before merging the color/grid/print stack. Main and Pages are not updated by merely creating commits on the feature branch.

The local shell and image tools returned ClientError during this continuation, so no new local execution or untracked-file audit is claimed. An attempted separate retained-artifact inspection workflow was platform-blocked and was not created; no alternate route was used to create it.

Primary references: Playwright's screenshot Buffer API (https://playwright.dev/docs/screenshots) and pngjs's synchronous PNG reader (https://github.com/pngjs/pngjs). Application pixel evidence comes from Playwright screenshots of the real Uno WebAssembly build, not these libraries' synthetic unit fixtures.
