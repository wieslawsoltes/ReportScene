using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ReportScene.Engine;
using ReportScene.Model;
using ReportScene.Rendering.Skia;
using ReportScene.Validation;

// Timings are evidence, not noisy CI thresholds; differential correctness remains mandatory.
var records = new List<object>();
var style = new ItemStyle { FontSize = 10 };
foreach (int size in new[] { 8192, 32768 })
{
    string text = new('x', size); var baseline = new CountingMeasurer(); var optimized = new CountingMeasurer();
    var a = TextLayoutPerformanceTests.OriginalWrap(text, 40, style, baseline);
    var b = TextLayout.Wrap(text, 40, style, optimized);
    if (!a.SequenceEqual(b)) throw new InvalidOperationException("Differential layout mismatch.");
    var oldMeasure = Measure(() => TextLayoutPerformanceTests.OriginalWrap(text, 40, style, new CountingMeasurer()).Count);
    var newMeasure = Measure(() => TextLayout.Wrap(text, 40, style, new CountingMeasurer()).Count);
    records.Add(new { name = "wrap-counting", inputCharacters = size, lineCount = b.Count, oldMeasuredCharacters = baseline.Characters, newMeasuredCharacters = optimized.Characters,
        oldMeasurementCalls = baseline.Calls, newMeasurementCalls = optimized.Calls, oldMeasure, newMeasure });
}
using (var metrics = new SkiaTextMeasurer())
{
    string text = string.Concat(Enumerable.Repeat("Invoice AV office services 12345. ", 200));
    var a = TextLayoutPerformanceTests.OriginalWrap(text, 220, style, metrics); var b = TextLayout.Wrap(text, 220, style, metrics);
    if (!a.SequenceEqual(b)) throw new InvalidOperationException("Font-backed differential layout mismatch.");
    records.Add(new { name = "wrap-skia-warm", inputCharacters = text.Length, lineCount = b.Count,
        oldMeasure = Measure(() => TextLayoutPerformanceTests.OriginalWrap(text, 220, style, metrics).Count),
        newMeasure = Measure(() => TextLayout.Wrap(text, 220, style, metrics).Count), metrics.CachedWidthCount, metrics.CachedCharacterCount });
}
var source = new TextItem();
var items = Enumerable.Range(0, 100000).Select(n => new PreparedItem(source, new(0, n * 20, 200, 18), "Row " + n)).ToArray();
var page = new PreparedPage(1, 300, 2000000, items);
var build = Measure(() => new PreparedPageIndex(page).Count); var index = new PreparedPageIndex(page);
var queries = Enumerable.Range(0, 100).Select(n => new RectD(0, n * 19000, 300, 400)).ToArray();
long examined = 0;
foreach (var query in queries)
{
    var actual = index.Query(query, out int candidates); examined += candidates;
    var expected = items.Where(i => PreparedVisibility.Intersect(i.Bounds, query) is { Width: > 0, Height: > 0 }).ToArray();
    if (!actual.SequenceEqual(expected)) throw new InvalidOperationException("Viewport differential mismatch.");
}
records.Add(new { name = "page-index", itemCount = items.Length, queryCount = queries.Length, baselineCandidates = (long)items.Length * queries.Length,
    indexedCandidates = examined, build,
    oldMeasure = Measure(() => queries.Sum(q => items.Count(i => PreparedVisibility.Intersect(i.Bounds, q) is { Width: > 0, Height: > 0 }))),
    newMeasure = Measure(() => queries.Sum(q => index.Query(q).Count)) });
var intervals = new VerticalIntervalIndex(items.Select(i => (i.Bounds.Y, i.Bounds.Bottom))); long intervalCandidates = 0;
foreach (var query in queries) { _ = intervals.Query(query.Y, query.Bottom, out int candidates); intervalCandidates += candidates; }
records.Add(new { name = "pagination-interval-index", intervalCount = intervals.Count, queryCount = queries.Length,
    baselineCandidates = (long)intervals.Count * queries.Length, indexedCandidates = intervalCandidates,
    queryMeasure = Measure(() => queries.Sum(q => intervals.Query(q.Y, q.Bottom, out _).Count)) });
var table = PaginationPerformanceTests.TableReport(10000); var engine = new ReportPreparer();
records.Add(new { name = "current-table-preparation", sourceRows = 10000,
    note = "Current complete engine preparation, including page-count passes. No old-engine timing is inferred.",
    measure = Measure(() => engine.Prepare(table).Pages.Count) });
var report = new PreparedReport("Search", [page], [], TimeSpan.Zero); var search = new PreparedSearchSession(report); search.SetQuery("Row 999");
records.Add(new { name = "search-api-rescan-vs-session", matchCount = search.Matches.Count,
    note = "Compares repeated lazy Search API scans with cached session navigation, not the prior designer's already-cached Find-next behavior.",
    rescanMeasure = Measure(() => Enumerable.Range(0, 100).Sum(_ => report.Search("Row 999").Count())),
    cachedNavigationMeasure = Measure(() => { int found = 0; for (int n = 0; n < 100; n++) { search.SetQuery("Row 999"); if (search.MoveNext() is not null) found++; } return found; }),
    search.QueryScanCount });
records.AddRange(RelationBenchmarks.Run());
var evidence = new { schema = 1, framework = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    processors = Environment.ProcessorCount, timestampUtc = DateTime.UtcNow, note = "Median of three in-process repetitions after one warmup; index build cost recorded separately. No hardware GPU, FPS or browser latency claim.", results = records };
string json = JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true });
Directory.CreateDirectory("artifacts"); File.WriteAllText("artifacts/performance.json", json); Console.WriteLine(json);
return 0;

static Measurement Measure(Func<int> action)
{
    _ = action(); var runs = new List<Measurement>();
    for (int n = 0; n < 3; n++)
    {
        long allocation = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp(); int checksum = action();
        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds; long bytes = GC.GetAllocatedBytesForCurrentThread() - allocation;
        runs.Add(new(elapsed, bytes, checksum));
    }
    return runs.OrderBy(r => r.Milliseconds).ElementAt(1);
}
sealed record Measurement(double Milliseconds, long AllocatedBytes, int Checksum);
sealed class CountingMeasurer : ITextMeasurer
{
    public long Characters; public int Calls;
    public double MeasureWidth(string text, ItemStyle style) { Characters += text.Length; Calls++; return text.Length; }
    public double LineHeight(ItemStyle style) => 12;
}
