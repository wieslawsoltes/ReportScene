using ReportScene.Designer;
using ReportScene.Model;
using ReportScene.Serialization;

namespace ReportScene.Validation;

public static class DesignerBatchTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("designer batch: " + name, true); } catch (Exception e) { return new("designer batch: " + name, false, e.ToString()); } }
        DesignerSession Fixture(int count = 12) => new(new() { Pages = [new() { Bands = [new() { Id = "band", Items = Enumerable.Range(0, count).Select(n => (ReportItem)new TextItem { Id = "t" + n, Name = "Text" + n, Bounds = new(20 + n, 30, 100, 80), Locked = n == 1 }).ToList() }] }] });
        yield return Test("large selection replaces each document item in one pass", () =>
        {
            var s = Fixture(20000); s.SelectAll(); s.Move(10, 10); Conformance.Equal(20000, s.LastReplacementScanCount);
            Conformance.Equal(30d, s.Document.Pages[0].Bands[0].Items[0].Bounds.X); Conformance.Equal(21d, s.Document.Pages[0].Bands[0].Items[1].Bounds.X);
            s.Undo(); Conformance.Equal(20000, s.LastReplacementScanCount); Conformance.Equal(20d, s.Document.Pages[0].Bands[0].Items[0].Bounds.X);
            s.Redo(); Conformance.Equal(30d, s.Document.Pages[0].Bands[0].Items[0].Bounds.X);
        });
        yield return Test("invalid identity cannot partly apply or record history", () =>
        {
            var s = Fixture(); s.SelectAll(); string old = ReportJson.Serialize(s.Document);
            Conformance.Throws<ArgumentException>(() => s.UpdateSelection("bad", i => i.Id == "t10" ? i with { Id = "missing" } : i with { Bounds = i.Bounds.Offset(2, 2) }));
            Conformance.Equal(old, ReportJson.Serialize(s.Document)); Conformance.Equal(0, s.HistoryCount);
        });
        yield return Test("invalid later bounds cannot partly apply", () =>
        {
            var s = Fixture(); s.SelectAll(); string old = ReportJson.Serialize(s.Document);
            Conformance.Throws<ArgumentException>(() => s.UpdateSelection("bad", i => i with { Bounds = i.Bounds with { Width = i.Id == "t10" ? double.NaN : 120 } }));
            Conformance.Equal(old, ReportJson.Serialize(s.Document)); Conformance.Equal(0, s.HistoryCount);
        });
        yield return Test("all resize handles produce one reversible drag transaction", () =>
        {
            foreach (var handle in Enum.GetValues<ResizeHandle>())
            {
                var s = Fixture(); s.SnapToGrid = false; s.Select("t0"); var before = s.Selection.Single().Bounds;
                s.BeginDrag(); for (int n = 1; n <= 15; n++) s.PreviewResize(n, n, handle); s.CommitDrag();
                var after = s.Selection.Single().Bounds; Conformance.Equal(DesignerGeometry.Resize(before, 15, 15, handle), after); Conformance.Equal(1, s.HistoryCount);
                s.Undo(); Conformance.Equal(before, s.Selection.Single().Bounds); s.Redo(); Conformance.Equal(after, s.Selection.Single().Bounds);
            }
        });
        yield return Test("cancelled resize restores every selected object", () =>
        {
            var s = Fixture(); s.SelectAll(); var old = ReportJson.Serialize(s.Document); s.BeginDrag(); s.PreviewResize(16, 12, ResizeHandle.TopLeft); s.CancelDrag();
            Conformance.Equal(old, ReportJson.Serialize(s.Document)); Conformance.Equal(0, s.HistoryCount);
        });
        yield return Test("identical snapped frames do not send duplicate changed events", () =>
        {
            var s = Fixture(); s.Select("t0"); s.BeginDrag(); int changes = 0; s.Changed += (_, _) => changes++;
            s.PreviewDrag(8, 8); s.PreviewDrag(8, 8); s.PreviewDrag(8.1, 8.1); Conformance.Equal(1, changes); s.CancelDrag();
        });
        yield return Test("invalid snapped transform leaves drag recoverable", () =>
        {
            var s = Fixture(); s.Select("t0"); var old = s.Selection.Single(); s.BeginDrag(); s.GridSize = double.PositiveInfinity;
            Conformance.Throws<ArgumentException>(() => s.PreviewDrag(4, 4)); Conformance.Equal(old, s.Selection.Single()); s.CancelDrag(); Conformance.Equal(false, s.HasActiveDrag);
        });
        yield return Test("paste reserves unique names within the same batch", () =>
        {
            var s = Fixture(); var payload = ReportJson.SerializeItems([new TextItem { Name = "Same" }, new TextItem { Name = "Same" }, new TextItem { Name = "same" }]);
            s.Paste(payload); Conformance.Equal(3, s.Selection.Select(i => i.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            var ids = s.SelectedIds.ToArray(); s.Undo(); Conformance.Equal(12, DocumentTree.AllItems(s.Document).Count()); s.Redo(); Conformance.Equal(true, ids.All(s.SelectedIds.Contains));
        });
        yield return Test("delete restores original positions across multiple bands", () =>
        {
            var s = Fixture(); s.Document.Pages[0].Bands.Add(new() { Id = "other", Items = [new TextItem { Id = "a" }, new TextItem { Id = "b" }] });
            string old = ReportJson.Serialize(s.Document); s.SelectMany(["t0", "t3", "t8", "a", "b"]); s.DeleteSelection(); s.Undo();
            Conformance.Equal(old, ReportJson.Serialize(s.Document)); s.Redo(); Conformance.Equal(9, DocumentTree.AllItems(s.Document).Count());
        });
        yield return Test("no-op ordering and locked selection do not dirty the report", () =>
        {
            var s = Fixture(); s.Reorder(true); s.Select("t11"); s.Reorder(true); s.Select("t1"); s.Reorder(false); s.BeginDrag();
            Conformance.Equal(0, s.HistoryCount); Conformance.Equal(false, s.IsDirty || s.HasActiveDrag);
        });
        yield return Test("lock and unlock are reversible and locked objects do not transform", () =>
        {
            var s = Fixture(); s.Select("t0"); s.SetSelectionLocked(true); s.Move(10, 10); Conformance.Equal(1, s.HistoryCount);
            s.SetSelectionLocked(false); s.Move(10, 10); Conformance.Equal(30d, s.Selection.Single().Bounds.X);
            s.Undo(); s.Undo(); Conformance.Equal(true, s.Selection.Single().Locked); s.Undo(); Conformance.Equal(false, s.Selection.Single().Locked);
        });
        yield return Test("renaming cannot introduce a case-colliding object name", () =>
        {
            var s = Fixture(); s.Select("t0"); Conformance.Throws<ArgumentException>(() => s.UpdateSelection("rename", i => i with { Name = "text2" }));
            Conformance.Equal("Text0", s.Selection.Single().Name); Conformance.Equal(0, s.HistoryCount);
        });
    }
}
