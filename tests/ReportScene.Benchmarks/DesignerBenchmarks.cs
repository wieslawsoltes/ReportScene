using System.Diagnostics;
using ReportScene.Designer;
using ReportScene.Model;

internal static class DesignerBenchmarks
{
    public static IEnumerable<object> Run()
    {
        foreach (int count in new[] { 1000, 10000 })
        {
            var reference = Fixture(count); var actual = new DesignerSession(Fixture(count)); actual.SelectAll();
            var changes = reference.Pages[0].Bands[0].Items.Select(i => i with { Bounds = i.Bounds.Offset(10, 10) }).ToArray();
            long oldComparisons = 0;
            // The previous replacement loop restarted document traversal for each selected item.
            foreach (var change in changes)
            {
                bool replaced = false;
                foreach (var band in DocumentTree.AllBands(reference))
                {
                    for (int n = 0; n < band.Items.Count; n++)
                    {
                        oldComparisons++;
                        if (band.Items[n].Id != change.Id) continue;
                        band.Items[n] = change; replaced = true; break;
                    }
                    if (replaced) break;
                }
                if (!replaced) throw new InvalidOperationException("Reference replacement target is missing.");
            }
            actual.Move(10, 10);
            if (!DocumentTree.AllItems(reference).SequenceEqual(DocumentTree.AllItems(actual.Document))) throw new InvalidOperationException("Designer replacement differential mismatch.");
            int comparisons = actual.LastReplacementScanCount;
            if (comparisons != count) throw new InvalidOperationException("Batch replacement scanned more than one document pass.");
            records(count, oldComparisons, comparisons, actual, out var record); yield return record;
        }
        var document = Fixture(20000);
        var outline = DesignerOutline.Report(document); var projection = outline.Filter("Object12345");
        if (projection.Count != 3) throw new InvalidOperationException("Outline ancestor filtering mismatch.");
        var construction = Measure(() => DesignerOutline.Report(document).Entries.Count);
        var buildAndQuery = Measure(() => DesignerOutline.Report(document).Filter("Object12345").Count);
        var cached = Measure(() => { int result = 0; for (int n = 0; n < 1000; n++) result += outline.Filter("Object12345").Count; return result; });
        if (outline.QueryScanCount != 1) throw new InvalidOperationException("Cached outline query unexpectedly scanned again.");
        yield return new { name = "designer-outline", objects = 20000, entries = outline.Entries.Count, construction, buildAndQuery,
            cached1000Queries = cached, outline.QueryScanCount,
            note = "Immutable headless outline creation and filtering only. Native tree-container realization and UI latency are not included." };
    }
    private static void records(int count, long oldComparisons, int comparisons, DesignerSession session, out object record)
    {
        var current = Measure(() => { session.Move(1, 1); return session.LastReplacementScanCount; });
        record = new { name = "designer-batch-update", objects = count, referenceReplacementComparisons = oldComparisons,
            batchedReplacementComparisons = comparisons, currentMove = current,
            note = "Comparison counts cover replacement target lookup, checked against the previous loop. Timing is the current complete headless Move command, including selection, validation and history; no historical elapsed-time or whole-UI speedup is inferred." };
    }
    private static ReportDocument Fixture(int count) => new()
    {
        Name = "Designer benchmark", Pages = [new() { Name = "Main", Bands = [new()
        {
            Id = "band", Name = "Details", Height = 100,
            Items = Enumerable.Range(0, count).Select(n => (ReportItem)new TextItem
            { Id = "item" + n, Name = "Object" + n.ToString("D5"), Text = "Text " + n, Bounds = new(20 + n, 30, 100, 22) }).ToList()
        }] }]
    };
    private static Measurement Measure(Func<int> action)
    {
        _ = action(); var measurements = new List<Measurement>();
        for (int n = 0; n < 3; n++)
        {
            long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp(); int checksum = action();
            measurements.Add(new(Stopwatch.GetElapsedTime(start).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - bytes, checksum));
        }
        return measurements.OrderBy(m => m.Milliseconds).ElementAt(1);
    }
}
