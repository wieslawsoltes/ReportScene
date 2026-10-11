using ReportScene.Model;

namespace ReportScene.Validation;

public static class PreparedSearchTests
{
    private static PreparedReport Report(params string[] texts) => new("Search", texts.Select((t, n) => new PreparedPage(n + 1, 100, 100, [new(new TextItem { Id = n.ToString() }, new(0, 0, 100, 20), t)])).ToArray(), [], TimeSpan.Zero);
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("prepared search: " + name, true); } catch (Exception e) { return new("prepared search: " + name, false, e.ToString()); } }
        yield return Test("next and previous wrap in physical report order", () =>
        {
            var s = new PreparedSearchSession(Report("one", "one", "one")); s.SetQuery("one");
            Conformance.Equal(1, s.MoveNext()!.Page); Conformance.Equal(3, s.MovePrevious()!.Page); Conformance.Equal(1, s.MoveNext()!.Page);
        });
        yield return Test("previous from a fresh query starts at the last result", () =>
        { var s = new PreparedSearchSession(Report("one", "one")); s.SetQuery("one"); Conformance.Equal(2, s.MovePrevious()!.Page); });
        yield return Test("unchanged query navigation does not scan the report again", () =>
        {
            var s = new PreparedSearchSession(Report("one", "one")); s.SetQuery("one");
            for (int n = 0; n < 100; n++) { s.SetQuery("one", new()); s.MoveNext(); }
            Conformance.Equal(1, s.QueryScanCount);
        });
        yield return Test("case-sensitive and case-insensitive options change cached results", () =>
        { var s = new PreparedSearchSession(Report("ALPHA", "Alpha", "alpha")); s.SetQuery("alpha"); Conformance.Equal(3, s.Matches.Count); s.SetQuery("alpha", new(MatchCase: true)); Conformance.Equal(1, s.Matches.Count); });
        yield return Test("whole-word search excludes prefixes underscores and combining marks", () =>
        {
            var s = new PreparedSearchSession(Report("cat", "cats", "_cat", "cat_", "cat\u0301", "(cat)", "😀cat😀", "a cat")); s.SetQuery("cat", new(WholeWord: true));
            Conformance.Equal("1,6,7,8", string.Join(',', s.Matches.Select(m => m.Page)));
        });
        yield return Test("whole-word search handles letters outside the basic multilingual plane", () =>
        { var s = new PreparedSearchSession(Report("𐐀cat", "cat𐐀", "cat")); s.SetQuery("cat", new(WholeWord: true)); Conformance.Equal(3, s.Matches.Single().Page); });
        yield return Test("later whole-word occurrence in the same object is found", () =>
        { var s = new PreparedSearchSession(Report("cats cat")); s.SetQuery("cat", new(WholeWord: true)); Conformance.Equal(1, s.Matches.Count); });
        yield return Test("new prepared snapshot never retains another report's results", () =>
        {
            var a = new PreparedSearchSession(Report("old")); a.SetQuery("old"); Conformance.Equal(1, a.Matches.Count);
            var b = new PreparedSearchSession(Report("new")); b.SetQuery("old"); Conformance.Equal(0, b.Matches.Count);
        });
        yield return Test("empty search and failed search have no navigation hit", () =>
        { var s = new PreparedSearchSession(Report("one")); s.SetQuery(""); Conformance.Equal<SearchHit?>(null, s.MoveNext()); s.SetQuery("two"); Conformance.Equal<SearchHit?>(null, s.MovePrevious()); });
        yield return Test("cancelled query preserves the previous cached match set", () =>
        {
            var s = new PreparedSearchSession(Report("one", "two")); s.SetQuery("one");
            Conformance.Throws<OperationCanceledException>(() => s.SetQuery("two", cancellationToken: new(true))); Conformance.Equal(1, s.Matches.Single().Page);
        });
    }
}
