# Browser readiness and retained failure evidence

The completed `3ecfbc5` run (38174908110) passed 31 of 32 native-input scenarios; the local-report/search scenario timed out before clicking the Report tab. It was not accepted for merge. A temporary read-only workflow inspected its retained trace without rerunning tests or altering assertions (review run 38204463465).

The trace did **not** show a missing Report tab: both recorded states contain `Commands["tab-report"]` with positive bounds. The next state evaluation returned after the assertion's 60-second deadline. There were no recorded error/fatal console events in the inspected stream. This establishes a stalled browser evaluation, not its complete underlying cause; a hardware, compositor or application-lifetime root cause is not claimed.

The original `reportscene.spec.mjs` still had a duplicate, older readiness helper. It accepted an internal render count without waiting for the actual Skia loading handoff, rendered screenshot or progressing diagnostic snapshots. It also passed `waitForFunction` timeout options in the second (page-function argument) position instead of the third options position. These are concrete harness defects regardless of the stall's cause.

All three original reporting scenarios now use the shared native-input driver, including its presentation, loader-removal, nonblank screenshot and fresh-state checks. The self-test uses the same path and passes timeout options correctly. Command, freshness and text-value polls transfer only their requested geometry/scalar rather than serializing every report object. This reduces diagnostic transport work without mutating the application or changing report behavior; no end-to-end speedup is inferred.

The 32 existing scenarios, all four required shards, 60-second assertion deadlines and zero retry/skip/flaky-result acceptance remain. The temporary trace workflow is removed. Only the subsequent complete current-head result can establish acceptance. References: [Playwright page API](https://playwright.dev/docs/api/class-page#page-wait-for-function), [actionability](https://playwright.dev/docs/actionability).
