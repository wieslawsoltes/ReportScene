using System.Diagnostics;
using ReportScene.Designer;

internal static class OutlineNavigationBenchmarks
{
    public static IEnumerable<object> Run()
    {
        var entries = new List<DesignerOutlineEntry> { new("root", null, "Report", OutlineKind.Page) };
        for (int n = 0; n < 20000; n++) entries.Add(new("item:" + n, "root", "Object" + n, OutlineKind.Object));
        var outline = new DesignerOutline(entries);
        var projection = new DesignerOutlineProjection(outline);
        var queries = Enumerable.Range(0, 1000).Select(n => (n % 4) switch
        {
            0 => "item:19999", 1 => "item:" + (n * 17 % 20000), 2 => "missing", _ => "ITEM:19999"
        }).ToArray();
        long comparisons = 0; int referenceFound = 0;
        foreach (string query in queries)
        {
            bool found = false;
            foreach (var entry in outline.Entries) { comparisons++; if (entry.Key == query) { found = true; break; } }
            if (found != projection.ContainsKey(query)) throw new InvalidOperationException("Indexed outline selection differs from the ordinal scan.");
            if (found) referenceFound++;
        }
        int Scan() => queries.Count(key => outline.Entries.Any(entry => entry.Key == key));
        int Indexed() => queries.Count(projection.ContainsKey);
        if (Scan() != referenceFound || Indexed() != referenceFound) throw new InvalidOperationException("Outline selection checksum mismatch.");
        yield return new
        {
            name = "outline-selection-membership", entries = projection.Count, queryCount = queries.Length, referenceFound,
            referenceEntryComparisons = comparisons, indexedApiProbes = queries.Length,
            projectionConstruction = Measure(() => new DesignerOutlineProjection(outline).Count),
            referenceScan = Measure(Scan), indexedLookup = Measure(Indexed),
            note = "Same current immutable metadata, ordinal keys, present/missing/case-mismatched probes. Index construction is separate. API probes are not internal hash-table string-comparison counts. Native row realization, property refresh, rendering and browser latency are excluded."
        };
    }
    private static Measurement Measure(Func<int> action)
    {
        _ = action(); var runs = new List<Measurement>();
        for (int n = 0; n < 3; n++)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread(), started = Stopwatch.GetTimestamp(); int checksum = action();
            runs.Add(new(Stopwatch.GetElapsedTime(started).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocated, checksum));
        }
        return runs.OrderBy(r => r.Milliseconds).ElementAt(1);
    }
}
