using System.Diagnostics;
using ReportScene.Engine;
using ReportScene.Model;
using ReportScene.Validation;

internal static class RelationBenchmarks
{
    public static IEnumerable<object> Run()
    {
        static Measurement Measure(Func<int> action)
        {
            _ = action(); var records = new List<Measurement>();
            for (int n = 0; n < 3; n++)
            {
                long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp(); int checksum = action();
                records.Add(new(Stopwatch.GetElapsedTime(start).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - bytes, checksum));
            }
            return records.OrderBy(r => r.Milliseconds).ElementAt(1);
        }
        var rows = Enumerable.Range(0, 100000).Select(n => new DataRow { ["Id"] = n % 1000 }).ToArray();
        var index = new DataKeyIndex(rows, "Id"); long candidates = 0;
        for (int n = 0; n < 1000; n++)
        {
            var actual = index.Find(n, out int examined); candidates += examined;
            var expected = Enumerable.Range(0, rows.Length).Where(i => Value.Compare(rows[i]["Id"], n) == 0);
            if (!actual.SequenceEqual(expected)) throw new InvalidOperationException("Relation-key differential mismatch.");
        }
        yield return new { name = "relation-key-index", rows = rows.Length, lookups = 1000,
            scanCandidates = (long)rows.Length * 1000, indexedCandidates = candidates,
            build = Measure(() => new DataKeyIndex(rows, "Id").KeyedRowCount),
            scan = Measure(() => Enumerable.Range(0, 1000).Sum(key => rows.Count(row => Value.Compare(row["Id"], key) == 0))),
            indexed = Measure(() => Enumerable.Range(0, 1000).Sum(key => index.Find(key, out _).Count)) };
        foreach (bool subreport in new[] { false, true })
        {
            var document = RelationIndexTests.Fixture(300, 9000, subreport); var preparer = new ReportPreparer();
            var options = new PreparationOptions(ReportDate: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            PreparedReport Prepare(bool indexed) => preparer.Prepare(document, options: options with { EnableRelationIndexes = indexed });
            string Signature(PreparedReport report) => string.Join('|', report.Pages.SelectMany(p => p.Items.Select(i => $"{p.Number}:{i.Bounds}:{i.Clip}:{i.Source.Id}:{i.Text}")));
            var reference = Prepare(false); var optimized = Prepare(true);
            if (Signature(reference) != Signature(optimized)) throw new InvalidOperationException("Complete master/detail differential mismatch.");
            yield return new { name = subreport ? "subreport-master-detail-preparation" : "master-detail-preparation", parentRows = 300, childRows = 9000,
                pages = optimized.Pages.Count, preparedItems = optimized.Pages.Sum(p => p.Items.Count),
                note = "Same current engine with relation indexes off/on; includes index construction and page-count passes. Does not infer a historical whole-engine speedup.",
                scan = Measure(() => Prepare(false).Pages.Sum(p => p.Items.Count)),
                indexed = Measure(() => Prepare(true).Pages.Sum(p => p.Items.Count)) };
        }
    }
}
