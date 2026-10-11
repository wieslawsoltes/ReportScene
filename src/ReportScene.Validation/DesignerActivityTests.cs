using ReportScene.Designer;
using ReportScene.Model;

namespace ReportScene.Validation;

public static class DesignerActivityTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("designer activity: " + name, true); } catch (Exception e) { return new("designer activity: " + name, false, e.ToString()); } }
        DesignerSession Session() => new(new ReportDocument { Pages = [new() { Bands = [new() { Items = [new TextItem { Id = "text" }] }] }, new() { Name = "Other", Bands = [new()] }] });
        yield return Test("selection navigation and saving do not invalidate document caches", () =>
        {
            var s = Session(); var t = new DesignerActivityTracker(s); long revision = s.Revision;
            s.Select("text"); s.SetPage(1); s.MarkSaved(); var change = t.Observe(s);
            Conformance.Equal(revision, s.Revision); Conformance.Equal(false, change.InvalidatePrepared); Conformance.Equal(false, change.SaveRecovery);
        });
        yield return Test("drag frames invalidate preview without structural rebuild or recovery serialization", () =>
        {
            var s = Session(); var t = new DesignerActivityTracker(s); s.Select("text"); s.BeginDrag();
            for (int n = 1; n <= 100; n++) { s.PreviewDrag(n, 0); var change = t.Observe(s); Conformance.Equal(true, change.InvalidatePrepared); Conformance.Equal(false, change.RefreshStructure || change.SaveRecovery); }
            s.CommitDrag(); var committed = t.Observe(s); Conformance.Equal(true, committed.RefreshStructure && committed.SaveRecovery && committed.InvalidatePrepared);
        });
        yield return Test("drag cancellation restores structure without saving a transient edit", () =>
        {
            var s = Session(); var t = new DesignerActivityTracker(s); s.Select("text"); s.BeginDrag(); s.PreviewDrag(20, 0); t.Observe(s); s.CancelDrag();
            var change = t.Observe(s); Conformance.Equal(true, change.InvalidatePrepared && change.RefreshStructure); Conformance.Equal(false, change.SaveRecovery);
        });
        yield return Test("undo redo and document load are committed content changes", () =>
        {
            var s = Session(); var t = new DesignerActivityTracker(s); s.Select("text"); s.Move(10, 0); Conformance.Equal(true, t.Observe(s).SaveRecovery);
            s.Undo(); Conformance.Equal(true, t.Observe(s).SaveRecovery); s.Redo(); Conformance.Equal(true, t.Observe(s).SaveRecovery);
            s.Load(Session().Document); Conformance.Equal(true, t.Observe(s).SaveRecovery);
        });
        yield return Test("recovery queue coalesces requests and drains an edit made during save", () =>
        {
            var queue = new RecoverySaveCoordinator(); var saved = new List<string>(); string current = "first"; int snapshots = 0;
            queue.Request(); queue.Request();
            queue.FlushAsync(() => { snapshots++; return current; }, value => { saved.Add(value); if (saved.Count == 1) { current = "second"; queue.Request(); } return Task.CompletedTask; }).GetAwaiter().GetResult();
            Conformance.Equal("first,second", string.Join(',', saved)); Conformance.Equal(2, snapshots); Conformance.Equal(false, queue.HasPending);
        });
        yield return Test("recovery failure remains pending for an explicit retry", () =>
        {
            var queue = new RecoverySaveCoordinator(); queue.Request();
            Conformance.Throws<IOException>(() => queue.FlushAsync(() => "data", _ => Task.FromException(new IOException("fixture"))).GetAwaiter().GetResult());
            Conformance.Equal(true, queue.HasPending); queue.FlushAsync(() => "new data", _ => Task.CompletedTask).GetAwaiter().GetResult(); Conformance.Equal(false, queue.HasPending);
        });
        yield return Test("recovery without an edit does not serialize", () =>
        {
            var queue = new RecoverySaveCoordinator(); queue.FlushAsync(() => throw new InvalidOperationException("Should not serialize"), _ => Task.CompletedTask).GetAwaiter().GetResult();
            Conformance.Equal(false, queue.HasPending);
        });
    }
}
