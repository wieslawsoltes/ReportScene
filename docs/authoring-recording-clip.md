# Native authoring recording bounds

Run 38451658579 passed seven browser shards, including both fitted-zoom tests,
but failed the distant-authoring scenario in shard 3. Its trace records 35-51
second animation-frame/scalar-read calls, then diagnostic sequence 303 after
the original 60-second observation deadline. This is a retained failed run,
not acceptance and not proof of a single application/GPU/scheduler cause.

## Production change

ReportDesignView already computes a guarded viewport from native surface and
ScrollViewer geometry. It now applies that rectangle as an explicit Skia
recording clip before paper, bands, rulers, grid, content and selection draws.
ReportViewportClip rounds binary32 edges outward and never reads the large
virtual recorder's LocalClipBounds. Empty viewports clip all drawing. The
existing two-DIP native geometry guard remains; no spacing, dot, stroke,
content or selection-handle requirement is relaxed.

Unknown/unloaded viewport geometry no longer falls back to recording the whole
document. Rendering is deferred until Loaded/SizeChanged/ViewChanged schedules
another invalidation. A deferred attempt is not counted as a rendered frame.
The caller restores the canvas on success, deferral and failure. Public
recording clip/time/deferred-count and grid-count diagnostics help distinguish
recording work from compositor presentation. Recording time is not GPU time,
input latency or frame rate.

## Validation

Eight shared native/WebAssembly regressions cover exact full-raster equality
at 15%, 50%, 85%, 100% and 400%, a large virtual recording canvas, fractional
scroll/scale, translucent grid dots, a nominally offscreen red stroke and green
lower-right content. Additional checks cover outward rounding, empty clipping,
Save/Restore and invalid geometry. Package-only consumers compile the API.
The existing 67 browser scenarios, all eight shards and zero retries/skips/
flaky results remain required. The native authoring scenario also checks that
recording dimensions stay within the actual viewport plus its existing guard.

Source archives were recovered and checked against Git tree
45986a5934010114d8b5ed6b58c67c84badaa8e7. Local JavaScript syntax and whitespace
checks are available. Local .NET is absent, external Git/NuGet networking is
unavailable, and local Chromium navigation returned ERR_BLOCKED_BY_ADMINISTRATOR;
no local runtime/profile result is claimed. Normal repository CI remains the
runtime validation gate. Final-head results must be recorded separately.

This bounds recording visibility; it does not establish a speed ratio, the
complete cause of every retained stall, full FastReport parity, or physical
GPU certification.

Primary references:
- https://platform.uno/docs/articles/controls/SKCanvasElement.html
- https://api.skia.org/classSkCanvas.html (clipRect and quickReject)
