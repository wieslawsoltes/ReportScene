using ReportScene.Model;

namespace ReportScene.Validation;

public static class PreparedQueryTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("prepared query: " + name, true); } catch (Exception e) { return new("prepared query: " + name, false, e.ToString()); } }
        var source = new TextItem(); var index = new PreparedPageIndex(new PreparedPage(1, 200, 200, [new(source, new(20, 20, 10, 10))]));
        yield return Test("empty and disjoint queries share empty results with no examined entries", () =>
        {
            foreach (var viewport in new[] { new RectD(0, 0, 0, 100), new RectD(0, 0, -1, 100), new RectD(50, 50, 10, 10) })
                for (int n = 0; n < 1000; n++)
                { var result = index.Query(viewport, out int examined); Conformance.Equal(true, ReferenceEquals(Array.Empty<PreparedItem>(), result)); Conformance.Equal(0, examined); }
        });
        yield return Test("early returns do not waive cancellation or invalid viewport checks", () =>
        {
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Conformance.Throws<OperationCanceledException>(() => index.Query(new(50, 50, 10, 10), out _, cancelled.Token));
            Conformance.Throws<ArgumentOutOfRangeException>(() => index.Query(new(double.NaN, 0, 0, 0)));
            var empty = new PreparedPageIndex(new PreparedPage(1, 200, 200, []));
            Conformance.Throws<OperationCanceledException>(() => empty.Query(new(0, 0, 0, 0), out _, cancelled.Token));
        });
        yield return Test("positive result snapshots remain independent and read only", () =>
        {
            var first = index.Query(new(0, 0, 100, 100)); var second = index.Query(new(0, 0, 100, 100));
            Conformance.Equal(1, first.Count); Conformance.Equal(true, first.SequenceEqual(second));
            Conformance.Throws<NotSupportedException>(() => ((IList<PreparedItem>)first)[0] = new(source, new(0, 0, 1, 1)));
            Conformance.Equal(new RectD(20, 20, 10, 10), second[0].Bounds);
        });
    }
}
