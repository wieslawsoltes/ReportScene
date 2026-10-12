using ReportScene.Designer;
using ReportScene.Model;

namespace ReportScene.Validation;

public static class DesignerNavigationTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("designer navigation: " + name, true); } catch (Exception e) { return new("designer navigation: " + name, false, e.ToString()); } }
        DesignerSession Fixture() => new(new() { Pages = [new() { Name = "Main", Bands = [new() { Id = "band", Height = 220, Items = [
            new TextItem { Id = "a", Name = "Alpha", Bounds = new(20, 20, 100, 40) },
            new TextItem { Id = "b", Name = "Beta", Bounds = new(200, 100, 100, 40) }
        ] }] }, new() { Name = "Other" }] });
        yield return Test("loaded identity survives edits undo redo and page navigation", () =>
        {
            var s = Fixture(); var id = s.DocumentId; s.Select("a"); s.Move(10, 10); s.Undo(); s.Redo(); s.MarkSaved(); s.SetPage(1);
            Conformance.Equal(id, s.DocumentId);
        });
        yield return Test("even reloading the same document object issues a new identity", () =>
        { var s = Fixture(); var id = s.DocumentId; s.Load(s.Document); Conformance.Equal(false, id == s.DocumentId); });
        yield return Test("save and navigation avoid structural metadata reconstruction", () =>
        {
            var s = Fixture(); var tracker = new DesignerActivityTracker(s);
            for (int n = 0; n < 1000; n++)
            {
                s.MarkSaved(); Conformance.Equal(false, tracker.Observe(s).RefreshStructure);
                s.SetPage(n % 2); Conformance.Equal(false, tracker.Observe(s).RefreshStructure);
            }
            s.SetPage(0); s.Select("a"); s.Move(10, 0); Conformance.Equal(true, tracker.Observe(s).RefreshStructure);
        });
        yield return Test("cancelled drag still requests restored structure without recovery", () =>
        {
            var s = Fixture(); var tracker = new DesignerActivityTracker(s); s.Select("a"); s.BeginDrag(); s.PreviewDrag(20, 10); tracker.Observe(s); s.CancelDrag();
            var activity = tracker.Observe(s); Conformance.Equal(true, activity.RefreshStructure); Conformance.Equal(false, activity.SaveRecovery);
        });
        yield return Test("projection lookup preserves filtered hierarchy and ordinal identifiers", () =>
        {
            var view = new DesignerOutlineProjection(DesignerOutline.Report(Fixture().Document), "Alpha");
            Conformance.Equal(3, view.Count); Conformance.Equal(true, view.ContainsKey("item:a"));
            Conformance.Equal(false, view.ContainsKey("ITEM:A")); Conformance.Equal(false, view.ContainsKey("item:b"));
            Conformance.Equal("band:band", view.Find("item:a")!.ParentKey!); Conformance.Equal(true, view.Find(null) is null);
        });
        yield return Test("projection remains an immutable metadata snapshot after edits", () =>
        {
            var s = Fixture(); var view = new DesignerOutlineProjection(DesignerOutline.Report(s.Document));
            s.Select("a"); s.UpdateSelection("Rename", i => i with { Name = "Renamed" });
            Conformance.Equal("Alpha", view.Find("item:a")!.Label); Conformance.Equal("Renamed", DesignerOutline.Report(s.Document).Find("item:a")!.Label);
        });
        yield return Test("empty projection and cancelled construction never invent keys", () =>
        {
            var outline = DesignerOutline.Report(Fixture().Document); var view = new DesignerOutlineProjection(outline, "missing");
            Conformance.Equal(0, view.Count); Conformance.Equal(false, view.ContainsKey("page:0"));
            using var c = new CancellationTokenSource(); c.Cancel();
            Conformance.Throws<OperationCanceledException>(() => new DesignerOutlineProjection(outline, "Alpha", c.Token));
        });
        yield return Test("large indexed selection lookup agrees with parent-first source", () =>
        {
            var source = new List<DesignerOutlineEntry> { new("root", null, "Report", OutlineKind.Page) };
            for (int n = 0; n < 20000; n++) source.Add(new("item:" + n, "root", "Object" + n, OutlineKind.Object));
            var view = new DesignerOutlineProjection(new DesignerOutline(source)); source.Clear();
            for (int n = 0; n < 20000; n++) Conformance.Equal("Object" + n, view.Find("item:" + n)!.Label);
            Conformance.Equal(20001, view.Count); Conformance.Equal(false, view.ContainsKey("item:20000"));
        });
        foreach (var handle in Enum.GetValues<ResizeHandle>())
        {
            var direction = handle;
            yield return Test("first selected object exposes " + direction + " handle", () =>
            {
                var layout = DesignLayout.Create(Fixture().Page); var selected = new HashSet<string> { "a", "b" }; var first = layout.Objects[0];
                var point = DesignerGeometry.HandlePoint(first.Bounds, direction); var hit = layout.HitSelectedHandle(selected, point.X, point.Y, 2);
                Conformance.Equal(true, hit.HasValue); Conformance.Equal("a", hit!.Value.Object.Item.Id); Conformance.Equal(direction, hit.Value.Handle);
            });
        }
        yield return Test("overlapping handles respect painter order and locked state", () =>
        {
            var s = Fixture(); var a = s.Page.Bands[0].Items[0]; s.Page.Bands[0].Items[1] = s.Page.Bands[0].Items[1] with { Bounds = a.Bounds };
            var selected = new HashSet<string> { "a", "b" }; var layout = DesignLayout.Create(s.Page); var p = layout.Objects[0].Bounds;
            Conformance.Equal("b", layout.HitSelectedHandle(selected, p.X, p.Y, 2)!.Value.Object.Item.Id);
            s.Page.Bands[0].Items[1] = s.Page.Bands[0].Items[1] with { Locked = true }; layout = DesignLayout.Create(s.Page);
            Conformance.Equal("a", layout.HitSelectedHandle(selected, p.X, p.Y, 2)!.Value.Object.Item.Id);
        });
        yield return Test("unselected interior and invalid points do not hit selection handles", () =>
        {
            var layout = DesignLayout.Create(Fixture().Page); var b = layout.Objects[0].Bounds; var selected = new HashSet<string> { "a" };
            Conformance.Equal(true, layout.HitSelectedHandle(new HashSet<string>(), b.X, b.Y, 2) is null);
            Conformance.Equal(true, layout.HitSelectedHandle(selected, b.X + 20, b.Y + 20, 2) is null);
            Conformance.Equal(true, layout.HitSelectedHandle(selected, double.NaN, b.Y, 2) is null);
            Conformance.Equal(true, layout.HitSelectedHandle(selected, b.X, b.Y, -1) is null);
        });
        yield return Test("object hit testing preserves reverse painter order without a reverse buffer", () =>
        {
            var s = Fixture(); var first = s.Page.Bands[0].Items[0]; s.Page.Bands[0].Items[1] = s.Page.Bands[0].Items[1] with { Bounds = first.Bounds };
            var layout = DesignLayout.Create(s.Page); var b = layout.Objects[0].Bounds;
            Conformance.Equal("b", layout.HitTest(b.X + 10, b.Y + 10)!.Item.Id); Conformance.Equal(true, layout.HitTest(-100, -100) is null);
        });
    }
}
