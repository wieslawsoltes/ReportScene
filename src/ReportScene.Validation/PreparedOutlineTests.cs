using ReportScene.Model;

namespace ReportScene.Validation;

public static class PreparedOutlineTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("preview outline: " + name, true); } catch (Exception e) { return new("preview outline: " + name, false, e.ToString()); } }
        PreparedItem Item(string? label, RectD? bounds = null, RectD? clip = null) => new(new TextItem { Id = "same-source", Bookmark = label }, bounds ?? new(5, 5, 40, 20), "text", Clip: clip);
        PreparedReport Report(params PreparedPage[] pages) => new("Bookmarks", pages, [], TimeSpan.Zero);
        yield return Test("duplicate labels and source identifiers retain occurrence order", () =>
        {
            var snapshot = new PreparedOutlineSnapshot(Report(new(7, 100, 100, [Item("Repeated"), Item("Repeated")]), new(7, 100, 100, [Item("Repeated")])));
            Conformance.Equal(3, snapshot.Count); Conformance.Equal(3, snapshot.Entries.Select(e => e.Key).Distinct().Count());
            Conformance.Equal(true, snapshot.Entries.Select(e => e.PageIndex).SequenceEqual([0, 0, 1]));
            Conformance.Equal(true, snapshot.Entries.All(e => e.PageNumber == 7 && e.ObjectId == "same-source"));
            Conformance.Equal(snapshot.Entries[2], snapshot.Find("bookmark:1:0")!);
        });
        yield return Test("outline targets intersect both page and item clip", () =>
        {
            var snapshot = new PreparedOutlineSnapshot(Report(new(10, 100, 100, [Item("Visible", new(80, 80, 40, 40), new(85, 85, 10, 10))])));
            Conformance.Equal(new RectD(85, 85, 10, 10), snapshot.Entries.Single().Bounds); Conformance.Equal(0, snapshot.Entries[0].PageIndex);
        });
        yield return Test("blank fully clipped and off-paper entries do not become targets", () =>
        {
            var snapshot = new PreparedOutlineSnapshot(Report(new(1, 100, 100, [Item(null), Item(" \t"), Item("Off", new(200, 0, 20, 20)), Item("Clipped", clip: new(90, 90, 2, 2)), Item("Visible")])));
            Conformance.Equal(1, snapshot.Count); Conformance.Equal(5L, snapshot.ItemsExamined); Conformance.Equal("Visible", snapshot.Entries[0].Text);
        });
        yield return Test("snapshot owns metadata rather than mutable prepared lists", () =>
        {
            var items = new List<PreparedItem> { Item("Original") }; var pages = new List<PreparedPage> { new(4, 100, 100, items) };
            var snapshot = new PreparedOutlineSnapshot(new("Report", pages, [], TimeSpan.Zero)); items.Clear(); pages.Clear();
            Conformance.Equal("Original", snapshot.Entries.Single().Text);
            Conformance.Throws<NotSupportedException>(() => ((IList<PreparedBookmark>)snapshot.Entries).Clear());
        });
        yield return Test("entry and retained-string limits fail rather than truncate", () =>
        {
            var report = Report(new(1, 100, 100, [Item("One"), Item("Two")]));
            Conformance.Throws<ArgumentException>(() => new PreparedOutlineSnapshot(report, maximumBookmarks: 1));
            Conformance.Throws<ArgumentException>(() => new PreparedOutlineSnapshot(report, maximumTextCharacters: 3));
            Conformance.Throws<ArgumentOutOfRangeException>(() => new PreparedOutlineSnapshot(report, maximumBookmarks: 0));
        });
        yield return Test("invalid geometry and pre-cancellation are explicit", () =>
        {
            using var c = new CancellationTokenSource(); c.Cancel();
            Conformance.Throws<OperationCanceledException>(() => new PreparedOutlineSnapshot(Report(), cancellationToken: c.Token));
            Conformance.Throws<ArgumentException>(() => new PreparedOutlineSnapshot(Report(new(1, double.NaN, 100, []))));
            Conformance.Throws<ArgumentException>(() => new PreparedOutlineSnapshot(Report(new(1, 100, 100, [Item("Bad", new(double.NaN, 0, 10, 10))]))));
        });
        yield return Test("keys use ordinal identity and empty snapshots are valid", () =>
        {
            var snapshot = new PreparedOutlineSnapshot(Report(new(1, 100, 100, [Item("One")])));
            Conformance.Equal(true, snapshot.Find("BOOKMARK:0:0") is null && snapshot.Find(null) is null);
            Conformance.Equal(0, new PreparedOutlineSnapshot(Report()).Count);
        });
        yield return Test("large repeated label report is scanned once and every occurrence is distinct", () =>
        {
            var items = Enumerable.Range(0, 10000).Select(_ => Item("Same")).ToArray();
            var snapshot = new PreparedOutlineSnapshot(Report(new(1, 100, 100, items)));
            Conformance.Equal(10000L, snapshot.ItemsExamined); Conformance.Equal(10000, snapshot.Count);
            for (int n = 0; n < items.Length; n++) Conformance.Equal(n, snapshot.Find("bookmark:0:" + n)!.ItemIndex);
        });
        yield return Test("reveal leaves an already visible target at current offsets", () =>
        { Conformance.Equal(new PreviewScrollTarget(100, 200), ReportPreviewReveal.Calculate(new(50, 50, 40, 20), 300, 200, 100, 200, 1000, 1000)); });
        yield return Test("reveal scrolls both axes by the minimum required amount", () =>
        { Conformance.Equal(new PreviewScrollTarget(176, 296), ReportPreviewReveal.Calculate(new(320, 250, 40, 30), 300, 200, 100, 200, 1000, 1000)); });
        yield return Test("negative viewport position scrolls back with margins", () =>
        { Conformance.Equal(new PreviewScrollTarget(34, 84), ReportPreviewReveal.Calculate(new(-50, -100, 20, 20), 300, 200, 100, 200, 1000, 1000)); });
        yield return Test("oversized target aligns its leading edge and respects extent", () =>
        {
            Conformance.Equal(new PreviewScrollTarget(84, 184), ReportPreviewReveal.Calculate(new(0, 0, 500, 300), 300, 200, 100, 200, 1000, 1000));
            Conformance.Equal(new PreviewScrollTarget(700, 800), ReportPreviewReveal.Calculate(new(1000, 1000, 20, 20), 300, 200, 0, 0, 1000, 1000));
        });
        yield return Test("unmeasured viewport and no scroll extent do not invent offsets", () =>
        {
            Conformance.Equal(new PreviewScrollTarget(0, 0), ReportPreviewReveal.Calculate(new(500, 500, 20, 20), 300, 200, 0, 0, 100, 100));
            Conformance.Equal(new PreviewScrollTarget(10, 20), ReportPreviewReveal.Calculate(new(500, 500, 20, 20), 0, 0, 10, 20, 1000, 1000));
        });
        yield return Test("invalid reveal values fail before native scrolling", () =>
        {
            Conformance.Throws<ArgumentException>(() => ReportPreviewReveal.Calculate(new(0, 0, -1, 1), 100, 100, 0, 0, 200, 200));
            Conformance.Throws<ArgumentOutOfRangeException>(() => ReportPreviewReveal.Calculate(new(0, 0, 1, 1), 100, 100, 0, 0, double.NaN, 200));
        });
    }
}
