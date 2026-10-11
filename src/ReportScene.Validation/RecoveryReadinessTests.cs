using ReportScene.Designer;
using ReportScene.Model;

namespace ReportScene.Validation;

public static class RecoveryReadinessTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("recovery readiness: " + name, true); } catch (Exception e) { return new("recovery readiness: " + name, false, e.ToString()); } }
        yield return Test("deferred requests do not snapshot until the host is ready", () =>
        {
            var queue = new RecoverySaveCoordinator(); int snapshots = 0; bool ready = false; queue.Request();
            Task Flush() => queue.FlushWhenReadyAsync(() => ready, () => { snapshots++; return "committed"; }, _ => Task.CompletedTask);
            Flush().GetAwaiter().GetResult(); Conformance.Equal(0, snapshots); Conformance.Equal(true, queue.HasPending);
            ready = true; Flush().GetAwaiter().GetResult(); Conformance.Equal(1, snapshots); Conformance.Equal(false, queue.HasPending);
        });
        yield return Test("readiness is rechecked before draining a newer request", () =>
        {
            var queue = new RecoverySaveCoordinator(); var saved = new List<string>(); bool ready = true; string content = "first"; queue.Request();
            Task Flush() => queue.FlushWhenReadyAsync(() => ready, () => content, value =>
            {
                saved.Add(value);
                if (saved.Count == 1) { content = "transient"; ready = false; queue.Request(); }
                return Task.CompletedTask;
            });
            Flush().GetAwaiter().GetResult(); Conformance.Equal("first", string.Join(',', saved)); Conformance.Equal(true, queue.HasPending);
            content = "second"; ready = true; Flush().GetAwaiter().GetResult(); Conformance.Equal("first,second", string.Join(',', saved)); Conformance.Equal(false, queue.HasPending);
        });
        yield return Test("a queued save cannot persist a cancelled drag's preview geometry", () =>
        {
            var session = new DesignerSession(new ReportDocument { Pages = [new() { Bands = [new() { Items = [new TextItem { Id = "text" }] }] }] });
            session.Select("text"); session.Move(12, 0);
            var queue = new RecoverySaveCoordinator(); queue.Request(); var saved = new List<double>();
            Task Flush() => queue.FlushWhenReadyAsync(() => !session.HasActiveDrag,
                () => session.Selection.Single().Bounds.X.ToString(System.Globalization.CultureInfo.InvariantCulture),
                value => { saved.Add(double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)); return Task.CompletedTask; });
            session.BeginDrag(); session.PreviewDrag(40, 0); Flush().GetAwaiter().GetResult(); Conformance.Equal(0, saved.Count);
            session.CancelDrag(); Flush().GetAwaiter().GetResult(); Conformance.Equal(12d, saved.Single()); Conformance.Equal(false, queue.HasPending);
        });
        yield return Test("pre-cancellation is enforced even while not ready", () =>
        {
            var queue = new RecoverySaveCoordinator(); queue.Request();
            Conformance.Throws<OperationCanceledException>(() => queue.FlushWhenReadyAsync(() => false,
                () => throw new InvalidOperationException("Should not snapshot"), _ => Task.CompletedTask, new(true)).GetAwaiter().GetResult());
            Conformance.Equal(true, queue.HasPending);
        });
    }
}
