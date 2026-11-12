# Rendering performance evidence

This update reduces repeated text work in the production renderer and avoids expensive line-distance work for fragments that cannot contribute coverage. The recorded CPU comparisons use the real baseline assemblies from `22939aadd71c26917ce9fb7911be5467dcc76eaf` and candidate assemblies from `f12ce32082705fe1eb94c235a4613147b99a1e9d`. The candidate CPU implementation was introduced in `de98c2b`; the later candidate revision also contains its native WebGPU integration and the conservative line shader.

The measurements cover four separate parts of rendering:

| Evidence | What executes | Interpretation |
| --- | --- | --- |
| Historical CPU comparison | The same public-API harness built against each actual revision; real text drawing, layout and width queries | CPU time and managed allocation for the specified native fixtures |
| Differential CPU benchmarks | Native shaped drawing and the glyph geometry/font queries performed before atlas lookup | Identifies the work removed within those phases |
| Native Uno browser scenario | File import, preparation, 100% preview and real wheel scrolling | Submitted-frame counters and actual visible text; no timing threshold |
| Paired production WGSL benchmark | Baseline and candidate pipelines on one standalone WebGPU device with identical input bytes | Separate CPU encoding, queue completion and optional GPU render-pass timestamps |

The [PR checks and retained artifacts](https://github.com/wieslawsoltes/ReportScene/pull/26/checks) identify the tested revision and current results. CPU raster measurements, software-adapter shader measurements and application input latency are different quantities. None of the numbers below is a hardware GPU throughput or application FPS claim.

## Production changes and bounds

### Retain shaped and raster text information

[`SkiaTextMeasurer`](../src/ReportScene.Rendering.Skia/SkiaTextMeasurer.Cache.cs) now retains immutable glyph IDs, positioned advances and native font metrics for reusable text runs. A separate raster-run cache stores native ink bounds and a value descriptor of the exact raster font at the requested scale. The descriptor includes the typeface identity, size, scale, skew and raster flags needed for glyph-cache identity. Repeated text can therefore reuse the same geometry without rebuilding arrays, querying each glyph's ink again, or repeatedly reading native font properties.

The native CPU drawing path retains positioned `SKTextBlob` objects. Its key includes the original X coordinate and baseline, and blob construction uses HarfBuzz's offset accumulation directly. This preserves float rounding from the previous `DrawShapedText` path. Public `ShapeText` results retain their caller-owned array contract; the new immutable run API is separate. Width-only pagination probes do not create additional `ReportTextRun` drawing arrays or retain raster geometry merely to answer a width query. [`ReportRenderer`](../src/ReportScene.Rendering.Skia/ReportRenderer.cs) also reuses font metrics, avoids unnecessary single-line splitting, and skips line-width queries when left alignment does not require them.

Retention is bounded independently for each cache:

| Cache | Entry bound | Text and glyph bounds |
| --- | ---: | --- |
| Native fonts | 128 | FIFO eviction disposes the native font |
| Immutable shaped runs | 2,048 | 131,072 UTF-16 text-plus-family characters and 131,072 glyphs |
| Raster runs | 2,048 | 131,072 UTF-16 text-plus-family characters and 131,072 glyphs |
| Native positioned blobs | 2,048 | 131,072 UTF-16 text-plus-family characters and 131,072 glyphs; eviction disposes the native blob |

A retained run must also fit the 4,096-character text-plus-family and 4,096-glyph per-run limits. Oversized raster runs use fixed-size ink staging and visible clipping. Oversized native intrinsic-color drawing consumes HarfBuzz's original arrays directly; a 16,384-glyph regression checks that the new immutable API does not introduce a duplicate whole-run glyph array in opacity or raster-export paths. Typeface-cache eviction clears dependent geometry and disposes cached native fonts/blobs; immutable snapshots already held by a caller contain readable managed data. These are item/character/glyph budgets, not a claim about an exact byte count for native Skia allocations. The [cache conformance tests](../src/ReportScene.Validation/TextRunCacheTests.cs) cover ownership, budgets, eviction, native raster equivalence and width-only behavior.

### Use warm glyph information in WebGPU recording

[`WebGpuReportCanvas`](../src/ReportScene.Rendering.WebGpu/WebGpuReportCanvas.cs) obtains the cached raster run, rejects invisible glyph ink before atlas lookup, and uses the value font descriptor to query the atlas. A native raster font is created only for a visible glyph miss. Cached glyphs reuse their coverage; oversized runs retain the bounded fallback. Text changes, a new raster scale or eviction can legitimately introduce cold work.

The textured-primitive builder in [`WebGpuReportCanvas`](../src/ReportScene.Rendering.WebGpu/WebGpuReportCanvas.cs), introduced in `16112ea`, constructs the fixed GPU fields directly for glyphs, path masks and images. It removes the per-primitive native `SKPaint` allocation while preserving the original coordinate, color, clipping and antialiasing arithmetic. Primitive byte counts, the existing span-upload path, painter/batch order, the 128-byte ABI and device ownership retain their established behavior.

### Reject transparent line fragments conservatively

[`Report2D.wgsl`](../src/ReportScene.Rendering.WebGpu/Shaders/Report2D.wgsl) preserves the original axis-aligned quad vertices and interpolated point expression. For eligible thin diagonal lines, the vertex stage supplies a flat normal, plane bias and conservative distance limit. The fragment stage can exit outside that strip before evaluating the full capped-segment distance.

The strip includes the half stroke plus `4 / min(pixels_per_dip.x, pixels_per_dip.y)` DIPs. This retains the antialiasing neighborhood, derivative helper lanes and float-rounding guard. Degenerate lines, distant endpoints and cases where the strip would not save work keep the original path. Rejected lanes execute both `discard` and an explicit entry-point `return`: WGSL discard alone demotes an invocation to a helper without terminating its execution. Caps, per-primitive clipping, the signed-distance function and premultiplied blending are unchanged. The optimization reduces work within the existing raster domain; it does not reduce the submitted quad area.

## Historical CPU comparison

The reusable harness is [`tests/ReportScene.TextBenchmarks`](../tests/ReportScene.TextBenchmarks). It performs real native drawing on a 1,000 × 520 RGBA surface. Each draw/layout measurement contains 64 frames with 128 distinct text runs, or 8,192 calls. Text includes kerning/ligature candidates, accented characters and a combining mark. The width control measures 8,192 distinct strings with a fresh metrics owner on every invocation.

The recorded environment was .NET 10.0.14, Debian GNU/Linux 13, X64. Both variants used identical harness source with SHA-256 `CDDAFCF1E1870A1F61AA6E52C6A65A36E578A99535631EB8B39FB5AC7BE4EE63`. The four process executions were baseline, candidate, candidate, baseline. Each process performed one warmup and five measured samples per workload, using the runtime's default tiered compilation setting. Widths execute before drawing in both variants. The table pools all ten measured samples per variant; elapsed-time and allocation medians are calculated independently.

| Work per measurement | Baseline median | Candidate median | Baseline / candidate time | Baseline managed bytes | Candidate managed bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| 8,192 warm `SkiaTextMeasurer.Draw` calls | 145.3552 ms | 94.0997 ms | 1.54× | 8,323,112 | 40 |
| 8,192 warm `ReportRenderer.DrawText` calls | 136.5699 ms | 110.3799 ms | 1.24× | 10,907,752 | 1,470,568 |
| 8,192 fresh-owner width probes | 54.8833 ms | 39.7062 ms | 1.38× | 8,637,712 | 7,786,888 |

For these fixtures, the pooled time medians fell by 35.3%, 19.2% and 27.7%, respectively. Layout's median managed allocation fell by 86.5%; the warm direct drawing measurement retained 40 bytes total. `GC.GetAllocatedBytesForCurrentThread` measures managed allocation on the calling thread. It does not count native Skia memory, GPU memory or the retained-cache footprint, and warm drawing excludes initial cache population.

Full raw samples, source/binary hashes, environment and output fingerprints are retained in the [baseline report](performance/text-cache-2026-11-12-baseline.json) and [candidate report](performance/text-cache-2026-11-12-candidate.json). Each native draw output has SHA-256 `FE025ADD4A58845A9C4D7641436C0C8D14C88530AADE08FE910AA1AE21890199`; each layout output has SHA-256 `4B81CF582589FDAEF9F39C291B3BA3849D18968AF68FB09FAD1257CBF8FD0AD5`. Every width sample sums to exactly `1363798.125` in both variants.

Two processes per variant are a limited sample. Individual time ranges remain broad: direct drawing spans 114.6590–216.1087 ms in the baseline and 85.7190–158.9761 ms in the candidate; fresh-owner widths span 40.2089–79.8113 ms and 35.1888–52.6463 ms. The medians characterize these recorded executions, without estimating a universal speedup or asserting statistical significance. Managed allocation and the zero-operation warm-path contracts provide separate evidence of the work removed.

### Retained unfavorable result and harness correction

The initial harness measured direct drawing, then layout, then fresh-owner widths. Its first pair reported widths at 28.4354 ms in the baseline and 39.0276 ms in the candidate: a 37.3% candidate slowdown. Reversing the process order did not remove the difference. That complete initial ABBA series remains in [diagnostic evidence](performance/text-cache-2026-11-12-diagnostics.json), alongside the [exact original harness source](performance/text-cache-2026-11-12-harness-initial.cs.txt).

Inspection found unequal preceding code histories: the historical left-aligned renderer called `MeasureWidth` for every rendered line, while the optimized renderer correctly omits those calls. Running layout first gave only the baseline tens of thousands of extra width-method invocations before the width experiment. A separate diagnostic with `DOTNET_TieredCompilation=0` and candidate/baseline/baseline/candidate process order produced width medians of 24.8747/24.5423 ms for the candidate and 30.8097/29.9385 ms for the baseline. That experiment changes runtime policy and is retained as diagnostic evidence, rather than being substituted for the default-runtime comparison.

The durable harness moves fresh-owner width measurement first, before either drawing workload. The primary ABBA reports above were rebuilt from that identical corrected source and retain all of their samples. This removes the identified baseline-only prewarming; it does not remove ordinary JIT, scheduling or host-load variation. Production code was not changed to obtain the corrected comparison.

## Differential CPU phase measurements

The normal [`ReportScene.Benchmarks`](../tests/ReportScene.Benchmarks/TextRenderingBenchmarks.cs) runner also records two constrained comparisons. These use one warmup and the median of three in-process repetitions. The [recorded differential results](performance/text-cache-2026-11-12-differential.json) describe their distinct references and limits. This corrected phase run uses renderer sources from `83e9824a69c2e784eef74987d32e8b3c20bcb67d`; its benchmark source has SHA-256 `7E58FFD82CF79DA50B9F8A962D43DB27652953D728ADF4D8438EA132C173E91C`. The evidence includes exact source-file and executed-assembly hashes because the benchmark correction awaited its subsequent commit when measured.

| Phase | Reference | Cached | Reference / cached | Managed bytes, reference → cached |
| --- | ---: | ---: | ---: | ---: |
| 8,192 native shaped draws | 110.6027 ms | 90.3448 ms | 1.22× | 7,471,240 → 40 |
| 4,096 raster runs; 110,592 positive-ink glyphs | 42.6252 ms | 1.1901 ms | 35.82× | 426,024 → 40 |

The native drawing reference deliberately reuses the same font and HarfBuzz shaper while rebuilding shaped arrays/text blobs. Its rendered pixels are compared byte for byte with cached native drawing, and the measured cached path performs zero shapes, blob builds or font creations.

The raster comparison covers native ink queries and per-positive-ink-glyph native font-descriptor reads versus cached immutable geometry/descriptor traversal. Each measurement traverses 135,168 shaped glyphs. Both paths reject the same 24,576 zero-ink glyphs with `ReportViewportClip.HasPositiveArea` before descriptor or fingerprint work, leaving 110,592 positive-ink glyphs. The fixture treats these ink-bearing glyphs as visible; it does not simulate additional viewport clipping. Positive-ink counts and descriptor/bounds checksums must agree before and after measurement. Counts come from the existing untimed correctness passes, without an additional cache-priming loop.

Review found that the first reference queried descriptors for zero-ink spaces, which production rejected before those reads. That initial raster timing is not accepted performance evidence. Its [raw diagnostic record](performance/text-cache-2026-11-12-differential-initial.json) and [exact initial benchmark source](performance/text-cache-2026-11-12-raster-initial.cs.txt) remain available. The same diagnostic record also retains an intermediate filtered run whose count instrumentation performed extra cached lookups; that priming was removed before the final run reported above. The historical public-API ABBA measurements are unaffected by these separate microbenchmark corrections.

The raster benchmark's scope excludes atlas lookup, rasterization, GPU upload, command submission, presentation and UI work. Its warm cached path performs zero bounds queries, shapes or font creations; those work-count checks remain distinct from any timing ratio. These local phase timings are sensitive to runtime warmup and do not replace the historical public-API ABBA comparison or establish a total rendering speedup.

## Actual Uno warm-text regression

[`webgpu-text-performance.spec.mjs`](../tests/browser/webgpu-text-performance.spec.mjs) imports a real report through the native file picker, prepares one tall page and selects 100% preview zoom. It contains 100 identical labels, each with an explicit white background: preparation therefore produces exactly 200 indexed objects. The scenario then scrolls down 20 DIPs and back with calibrated native wheel input; diagnostics are read-only.

Each measured settled viewport must have a newer successful GPU submission, all 200 prepared candidates, more than 100 primitives and at least 800 glyph-cache hits. The selected frame must report zero `TextShapingOperations`, `TextBoundsQueries`, `TextFontCreations`, `GlyphRasterizations` and `TextureUploads`. These zero counters are asserted directly on that frame, without polling until they become zero. The test checks real text-colored pixels and white background pixels in the first, middle and last labels, and preserves the prepared index, document identity, content revision and history. Its JSON artifact retains initial/down/back statistics and every native wheel acknowledgement.

The first execution on [run 43243208525](https://github.com/wieslawsoltes/ReportScene/actions/runs/43243208525) exposed an incorrect test expectation of 100 prepared candidates. The actual 100 labels correctly produced 100 background objects plus 100 text objects. The exact expected count was corrected to 200; the glyph, pixel and zero-native-work requirements remain unchanged. That run stopped before the scroll measurements and is not warm-path acceptance evidence. Subsequent [PR check artifacts](https://github.com/wieslawsoltes/ReportScene/pull/26/checks) contain the corrected scenario's outcome under `browser-evidence-8` and the merged browser report. The required merged suite includes this scenario and requires at least 79 passing tests with no retries, skips or flaky results.

These frame counters cover text command recording and glyph-cache activity. They do not time GPU execution or establish an application frame-rate improvement.

## Shader quality and paired timing protocol

The conservative shader passed the independent GPU gate in [run 43243208525](https://github.com/wieslawsoltes/ReportScene/actions/runs/43243208525). Its retained `webgpu-shader-validation` artifact reports:

- Zero differing RGBA bytes for 51 line cases across three full-image comparisons, including fractional/nonuniform pixel densities and nonzero `firstInstance`, against the unchanged distance path with early rejection disabled.
- All existing 73 raw pixel assertions and 73 presented pixel assertions passing with their existing tolerances, plus zero full-canvas presentation mismatches.
- All 4,104 independent ellipse-distance probes passing; maximum projected error was approximately 0.001534 physical pixels for that corpus.
- No shader compilation messages and no device loss on the reported SwiftShader adapter.

The earlier oriented-quad experiment was rejected after strict pixel equality failed. Its reduced geometric area is not an accepted performance result. The accepted shader retains the original vertex envelope and point interpolation; the [migration record](uno7-webgpu-migration.md) documents that change in direction.

The separate production A/B runner uses the exact baseline WGSL extracted from `22939aadd71c26917ce9fb7911be5467dcc76eaf`. Both pipelines share one private device, target, buffers, resident textures, sampler, ordered draw batches, upload mode and timestamp setting. Input and output bytes have SHA-256 fingerprints. Every completed pair must pass full-frame RGBA8 byte equality; a single changed byte fails the comparison. Baseline runs first on even repeats and candidate first on odd repeats; each variant receives its own warmup. Workload order rotates independently. The [benchmark guide](webgpu-benchmarks.md) documents the full runner interface and timing definitions.

The first combined 64-instance line/ellipse comparison reached its 300-second deadline after 12 completed trials. Its failure artifact records no GPU error, device loss or pixel-comparison failure, but did not retain completed raw timings. It is an incomplete run and supplies no accepted A/B speedup. The CI protocol now separates the bounded workloads while retaining 640 × 480 resolution, four warmup frames, twelve measured frames and four repeats per variant:

| Invocation | Workloads | Instances each | Artifact directory |
| --- | --- | ---: | --- |
| Main comparison | Shapes, glyphs, images, mixed content, diagonal lines | 64 | `comparison-main` |
| Ellipse control | Thin eccentric ellipse strokes | 16 | `comparison-ellipses` |

The general workloads check for regressions from the shader's additional flat varying and branch; the targeted diagonal workload checks the intended optimization. Each workload supplies 48 measured frames per variant. CPU preparation, submit-call time, queue-completion wall time and optional GPU-pass timestamps remain separate. GPU timestamps are never replaced with queue wall time, and zero/unavailable denominators produce no ratio. Software-adapter results remain specific to that adapter and fixture.

If a later run fails or times out while the page remains responsive, `failure.json` now retains `progress.completedEvidence` with `complete: false`. It contains only completed raw trial records, input/output hashes and strict accepted-pair equality results. It stores no packed pixel arrays, pooled performance summaries or speedup ratios, and does not change the failed status. A complete successful report is still required for a completed A/B result. The evolving full results are retained under `webgpu-benchmark-smoke` in the [PR checks](https://github.com/wieslawsoltes/ReportScene/pull/26/checks).

### Completed discard-only result and execution correction

The expanded comparison completed at `9a5716649a14f8513bd46bb8d0f1a9a00ffb803d` in [run 43248015197](https://github.com/wieslawsoltes/ReportScene/actions/runs/43248015197). All 24 paired trials passed exact full-image equality, with 48 measured samples per variant/workload. It did **not** show the intended line speedup. The reported SwiftShader GPU render-pass p50 values were:

| Workload | Baseline | Discard-only candidate | Baseline / candidate |
| --- | ---: | ---: | ---: |
| Shapes, 64 instances | 8.8246 ms | 8.9516 ms | 0.9858 |
| Glyphs, 64 instances | 3.6198 ms | 3.6459 ms | 0.9928 |
| Images, 64 instances | 57.7780 ms | 58.3552 ms | 0.9901 |
| Mixed, 64 instances | 8.1254 ms | 8.1937 ms | 0.9917 |
| Diagonal lines, 64 instances | 1774.0132 ms | 1787.4335 ms | 0.9925 |
| Ellipse control, 16 instances | 288.4241 ms | 290.2270 ms | 0.9938 |

The controls show a small consistent cost in several workloads, so this run is not described as neutral or faster. Its [summary and provenance](performance/webgpu-2026-11-12-discard-only.json) and [unaltered raw JSON/CSV archive](performance/webgpu-2026-11-12-discard-only.zip) are retained. The archive preserves both invocations' complete reports and samples; the summary records each original file's digest and the downloaded Actions artifact digest.

The guard was active for all 64 targeted lines. Inspection of [WGSL discard semantics](https://www.w3.org/TR/2026/CRD-WGSL-20261021/#discard-statement) explains why discarding coverage alone does not require the remaining storage reads or distance work to stop: execution becomes a helper invocation. An [entry-point return](https://www.w3.org/TR/2026/CRD-WGSL-20261021/#return-statement) terminates the invocation. The correction adds `return vec4f(0)` immediately after the proven far-line `discard`. The guard still preserves all potentially covered lanes and their derivative neighbors; geometry, input bytes and every quality/equality threshold remain unchanged. Subsequent full paired runs must establish the actual timing effect. No speedup is inferred merely from the source change or the CPU support oracle.

## Reproduce the CPU comparison

Use clean baseline and candidate checkouts, the same installed .NET runtime and separate harness build directories. Copying the small public-API project isolates `obj`/`bin` and permits either checkout to supply its own package/property configuration. For the recorded baseline, `baseline_root` must identify revision `22939aadd71c26917ce9fb7911be5467dcc76eaf`.

```bash
candidate_root="$(pwd)"
baseline_root="/absolute/path/to/ReportScene-baseline"
comparison_root="$(mktemp -d)"
baseline_revision="$(git -C "$baseline_root" rev-parse HEAD)"
candidate_revision="$(git -C "$candidate_root" rev-parse HEAD)"

mkdir -p "$comparison_root/baseline" "$comparison_root/candidate"
for variant in baseline candidate; do
  cp "$candidate_root/tests/ReportScene.TextBenchmarks/Program.cs" \
     "$candidate_root/tests/ReportScene.TextBenchmarks/ReportScene.TextBenchmarks.csproj" \
     "$comparison_root/$variant/"
done
dotnet build "$comparison_root/baseline/ReportScene.TextBenchmarks.csproj" \
  -c Release -p:ReportSceneRoot="$baseline_root"
dotnet build "$comparison_root/candidate/ReportScene.TextBenchmarks.csproj" \
  -c Release -p:ReportSceneRoot="$candidate_root"

baseline_dll="$comparison_root/baseline/bin/Release/net10.0/ReportScene.TextBenchmarks.dll"
candidate_dll="$comparison_root/candidate/bin/Release/net10.0/ReportScene.TextBenchmarks.dll"
dotnet "$baseline_dll" "$comparison_root/baseline-1.json" "$baseline_revision"
dotnet "$candidate_dll" "$comparison_root/candidate-1.json" "$candidate_revision"
dotnet "$candidate_dll" "$comparison_root/candidate-2.json" "$candidate_revision"
dotnet "$baseline_dll" "$comparison_root/baseline-2.json" "$baseline_revision"
```

Keep all four reports. Verify equal harness source hashes, matching draw/layout pixel fingerprints and exact width checksums before comparing timings. Pool each variant's ten raw samples for each workload and calculate elapsed-time and managed-allocation medians separately. Record any runtime policy changes, host load or additional process executions alongside the reports; a favorable rerun must not replace an unfavorable series.
