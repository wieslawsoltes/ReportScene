# Square grid batching candidate

The native designer uses non-antialiased square grid dots with a solid source-over
paint and positive axis scales. The existing Draw method batches these dots as two
indexed triangles, preserving the existing row-major float centers and widths.
It retains the same DrawPoints path for unsupported paints, transforms, round
caps, hairlines and extreme corners. The existing public Draw API is unchanged.
The original DrawPoint raster tests also exercise the optimized default. All point enumeration, budgets, cancellation and submission counts are shared.

The candidate reduces dependence on Skia's point primitive replay; it does not
reduce the number of dots, change spacing or infer a hardware-GPU speedup.
Ten additional conformance cases compare exact nonblank raster bytes at five
zooms, three fractional translations, two opacities, clipped views and fallback
paints (including rotation, skew, mirroring and non-source-over blending), then
verify bounded tails, clearing, empty paths, budgets and cancellation.
All prior grid/raster/browser tests remain required. The native designer uses
this same Draw API so actual existing viewport pixel/input scenarios test the integration.

Retained managed mesh payload is bounded to 360404 bytes (8191 dots * 44), in
addition to the existing 65528-byte point payload. Headers and Skia's copied
recorded mesh storage are separate. Clear releases managed buffers. This uses
more coordinates than point-only batching; lower replay cost is a hypothesis
until measured. No native report, paint, canvas, or data table is retained.

A separate review found that the original additive lattice could begin advancing
but stop at a binary64 exponent boundary. A preflight precision check now rejects
spacing smaller than the largest coordinate ULP before buffers or drawing, rather
than repeating a stuck coordinate until the point budget. Both axes crossing 2^55
are covered with an unchanged-surface and zero-buffer requirement. Ordinary
coordinate arithmetic and the existing raster requirements are unchanged.

SkiaSharp 3.119.2 API reference:
https://github.com/mono/SkiaSharp/blob/v3.119.2/binding/SkiaSharp/SKVertices.cs

Final-head conformance and all 72 browser scenarios across eight shards, zero
retries/skips/flaky results, are acceptance gates. No completed result is claimed
by this design note. The earlier repeat/authoring failures remain recorded.
