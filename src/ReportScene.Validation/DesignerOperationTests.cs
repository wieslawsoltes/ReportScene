using ReportScene.Designer;
using ReportScene.Model;
using ReportScene.Serialization;

namespace ReportScene.Validation;

public static class DesignerOperationTests
{
    private static DesignerSession Session()
    {
        var s = new DesignerSession(new ReportDocument { Name = "Operation fixture", Pages = [new()
        { Bands = [new() { Id = "band", Items = [new TextItem { Id = "a", Name = "A" }, new TextItem { Id = "b", Name = "B", Bounds = new(0, 40, 100, 20) }] }] }, new() { Name = "Other", Bands = [new() { Id = "other" }] }] });
        s.Select("a"); s.Move(4, 0); return s;
    }
    private static TaskCompletionSource<T> Deferred<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task Throws<T>(Task task) where T : Exception
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    public static async Task<IReadOnlyList<TestResult>> RunAsync()
    {
        var results = new List<TestResult>();
        async Task Test(string name, Func<Task> action)
        { try { await action(); results.Add(new("awaited designer operations: " + name, true)); } catch (Exception e) { results.Add(new("awaited designer operations: " + name, false, e.ToString())); } }
        await Test("successful save records the captured revision", async () =>
        {
            var s = Session(); using var ops = new DesignerOperations(s); var gate = Deferred<bool>(); DesignerSaveSnapshot? snapshot = null;
            var save = ops.SaveAsync((value, _) => { snapshot = value; return gate.Task; });
            Conformance.Equal(true, snapshot is not null); Conformance.Equal(true, s.IsDirty);
            gate.SetResult(true); var result = await save;
            Conformance.Equal(true, result.Written && result.Current); Conformance.Equal(false, s.IsDirty);
            Conformance.Equal(4d, ReportJson.Deserialize(snapshot!.Json).Pages[0].Bands[0].Items[0].Bounds.X);
        });
        await Test("editing during save remains dirty and undo reaches the saved revision", async () =>
        {
            var s = Session(); using var ops = new DesignerOperations(s); var gate = Deferred<bool>();
            var save = ops.SaveAsync((_, _) => gate.Task); s.Move(4, 0); gate.SetResult(true); var result = await save;
            Conformance.Equal(true, result.Written); Conformance.Equal(false, result.Current); Conformance.Equal(true, s.IsDirty);
            s.Undo(); Conformance.Equal(false, s.IsDirty);
        });
        await Test("undo during save cannot falsely leave the old disk revision clean", async () =>
        {
            var s = Session(); using var ops = new DesignerOperations(s); var gate = Deferred<bool>();
            var save = ops.SaveAsync((_, _) => gate.Task); s.Undo(); Conformance.Equal(false, s.IsDirty);
            gate.SetResult(true); await save; Conformance.Equal(true, s.IsDirty); s.Redo(); Conformance.Equal(false, s.IsDirty);
        });
        await Test("save completion cannot mark a replacement document saved", async () =>
        {
            var s = Session(); using var ops = new DesignerOperations(s); var gate = Deferred<bool>();
            var save = ops.SaveAsync((_, _) => gate.Task); s.Load(Session().Document); s.Select("b"); s.Move(4, 0);
            gate.SetResult(true); Conformance.Equal(false, (await save).Current); Conformance.Equal(true, s.IsDirty);
        });
        await Test("reloading the exact same object still expires old tokens", async () =>
        {
            var s = Session(); using var ops = new DesignerOperations(s); var gate = Deferred<bool>();
            var save = ops.SaveAsync((_, _) => gate.Task); s.Load(s.Document); gate.SetResult(true);
            Conformance.Equal(false, (await save).Current); Conformance.Equal(false, s.IsDirty);
        });
        await Test("competing saves serialize host writes without reversing completion order", async () =>
        {
            var s = Session(); using var ops = new DesignerOperations(s); var first = Deferred<bool>(); var second = Deferred<bool>(); var entered = Deferred<bool>();
            var writes = new List<long>();
            var a = ops.SaveAsync((snapshot, _) => { writes.Add(snapshot.Revision.Revision); return first.Task; });
            s.Move(4, 0);
            var b = ops.SaveAsync((snapshot, _) => { writes.Add(snapshot.Revision.Revision); entered.SetResult(true); return second.Task; });
            Conformance.Equal(1, writes.Count); first.SetResult(true); Conformance.Equal(false, (await a).Current);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); Conformance.Equal(2, writes.Count);
            second.SetResult(true); Conformance.Equal(true, (await b).Current); Conformance.Equal(false, s.IsDirty);
            Conformance.Equal(true, writes[0] < writes[1]);
        });
        await Test("a stale queued save never reaches the host", async () =>
        {
            var s = Session(); using var ops = new DesignerOperations(s); var gate = Deferred<bool>(); int writes = 0;
            var a = ops.SaveAsync((_, _) => { writes++; return gate.Task; });
            var b = ops.SaveAsync((_, _) => { writes++; return Task.FromResult(true); });
            s.Move(4, 0); gate.SetResult(true); await a; await Throws<StaleDesignerOperationException>(b);
            Conformance.Equal(1, writes); Conformance.Equal(true, s.IsDirty);
        });
        await Test("a cancelled queued save never reaches the host", async () =>
        {
            var s = Session(); using var ops = new DesignerOperations(s); var gate = Deferred<bool>(); using var cancel = new CancellationTokenSource(); int writes = 0;
            var a = ops.SaveAsync((_, _) => gate.Task);
            var b = ops.SaveAsync((_, _) => { writes++; return Task.FromResult(true); }, cancel.Token);
            cancel.Cancel(); await Throws<OperationCanceledException>(b); Conformance.Equal(0, writes); gate.SetResult(true); await a;
        });
        await Test("cancelled and failed writes preserve dirty state and release the gate", async () =>
        {
            var s = Session(); using var ops = new DesignerOperations(s);
            Conformance.Equal(false, (await ops.SaveAsync((_, _) => Task.FromResult(false))).Written); Conformance.Equal(true, s.IsDirty);
            await Throws<IOException>(ops.SaveAsync((_, _) => Task.FromException<bool>(new IOException("storage fixture"))));
            Conformance.Equal(true, s.IsDirty);
            Conformance.Equal(true, (await ops.SaveAsync((_, _) => Task.FromResult(true))).Current);
        });
        await Test("in-flight host cancellation is not successful persistence", async () =>
        {
            var s = Session(); using var ops = new DesignerOperations(s); var gate = Deferred<bool>();
            var save = ops.SaveAsync((_, _) => gate.Task); gate.SetCanceled(); await Throws<OperationCanceledException>(save); Conformance.Equal(true, s.IsDirty);
        });
        await Test("dispose cancels queued work and prevents in-flight completion mutations", async () =>
        {
            var s = Session(); using var ops = new DesignerOperations(s); var gate = Deferred<bool>();
            var a = ops.SaveAsync((_, _) => gate.Task); var b = ops.SaveAsync((_, _) => Task.FromResult(true));
            ops.Dispose(); await Throws<OperationCanceledException>(b); gate.SetResult(true); await Throws<ObjectDisposedException>(a);
            Conformance.Equal(true, s.IsDirty);
        });
        await Test("view liveness guards an operation even without disposing its coordinator", async () =>
        {
            var s = Session(); bool alive = true; using var ops = new DesignerOperations(s, () => alive); var gate = Deferred<bool>();
            var save = ops.SaveAsync((_, _) => gate.Task); alive = false; gate.SetResult(true);
            await Throws<ObjectDisposedException>(save); Conformance.Equal(true, s.IsDirty);
        });
        await Test("successful cut deletes only after acknowledgement and remains undoable", async () =>
        {
            var s = Session(); using var ops = new DesignerOperations(s); var gate = Deferred<bool>();
            var cut = ops.CutAsync(_ => gate.Task); Conformance.Equal(2, s.Page.Bands[0].Items.Count);
            gate.SetResult(true); await cut; Conformance.Equal("b", s.Page.Bands[0].Items.Single().Id); s.Undo(); Conformance.Equal(2, s.Page.Bands[0].Items.Count);
        });
        await Test("selection change during cut cannot delete a different object", async () =>
        {
            var s = Session(); using var ops = new DesignerOperations(s); var gate = Deferred<bool>(); string? copied = null;
            var cut = ops.CutAsync(text => { copied = text; return gate.Task; }); s.Select("b"); gate.SetResult(true);
            await Throws<StaleDesignerOperationException>(cut); Conformance.Equal(2, s.Page.Bands[0].Items.Count);
            Conformance.Equal("a", ReportJson.DeserializeItems(copied!).Single().Id);
        });
        await Test("document replacement during cut cannot remove matching IDs in the new report", async () =>
        {
            var s = Session(); using var ops = new DesignerOperations(s); var gate = Deferred<bool>();
            var cut = ops.CutAsync(_ => gate.Task); s.Load(Session().Document); s.Select("a"); gate.SetResult(true);
            await Throws<StaleDesignerOperationException>(cut); Conformance.Equal(2, s.Page.Bands[0].Items.Count);
        });
        await Test("failed cut never deletes its captured selection", async () =>
        {
            var s = Session(); using var ops = new DesignerOperations(s);
            await Throws<IOException>(ops.CutAsync(_ => Task.FromException(new IOException("clipboard fixture"))));
            Conformance.Equal(2, s.Page.Bands[0].Items.Count);
        });
        await Test("paste rejects a changed page or band after the clipboard read", async () =>
        {
            var s = Session(); using var ops = new DesignerOperations(s); string text = s.Copy(); var gate = Deferred<string?>();
            var paste = ops.PasteAsync(() => gate.Task); s.SetPage(1); gate.SetResult(text);
            await Throws<StaleDesignerOperationException>(paste); Conformance.Equal(0, s.Page.Bands[0].Items.Count);
        });
        await Test("clipboard permission failure retains the internal object clipboard", async () =>
        {
            var s = Session(); using var ops = new DesignerOperations(s); await ops.CopyAsync(_ => Task.CompletedTask);
            Conformance.Equal(true, await ops.PasteAsync(() => Task.FromException<string?>(new UnauthorizedAccessException("denied"))));
            Conformance.Equal(3, s.Page.Bands[0].Items.Count);
        });
        await Test("clipboard cancellation cannot fall through into a paste", async () =>
        {
            var s = Session(); using var ops = new DesignerOperations(s); await ops.CopyAsync(_ => Task.CompletedTask);
            await Throws<OperationCanceledException>(ops.PasteAsync(() => Task.FromCanceled<string?>(new(true))));
            Conformance.Equal(2, s.Page.Bands[0].Items.Count);
        });
        await Test("late open result cannot replace edits made during the picker", async () =>
        {
            var s = Session(); using var ops = new DesignerOperations(s); var gate = Deferred<ReportDocument?>();
            var open = ops.ReadAndApplyAsync(() => gate.Task, s.Load); s.Move(8, 0); gate.SetResult(new() { Name = "Late file" });
            await Throws<StaleDesignerOperationException>(open); Conformance.Equal("Operation fixture", s.Document.Name); Conformance.Equal(12d, s.Selection.Single().Bounds.X);
        });
        await Test("late image-style target update cannot affect a different selection", async () =>
        {
            var s = Session(); using var ops = new DesignerOperations(s); var gate = Deferred<string?>();
            var edit = ops.ReadAndApplyAsync(() => gate.Task, text => s.UpdateSelection("Delayed edit", i => ((TextItem)i) with { Text = text }), true);
            s.Select("b"); gate.SetResult("wrong target"); await Throws<StaleDesignerOperationException>(edit);
            Conformance.Equal(false, s.Page.Bands[0].Items.OfType<TextItem>().Any(t => t.Text == "wrong target"));
        });
        await Test("cancelled picker produces no apply and no document edit", async () =>
        {
            var s = Session(); using var ops = new DesignerOperations(s); int applied = 0;
            Conformance.Equal(false, await ops.ReadAndApplyAsync(() => Task.FromResult<string?>(null), _ => applied++)); Conformance.Equal(0, applied);
        });
        await Test("transient drag blocks operation snapshots and late mutations", async () =>
        {
            var s = Session(); using var ops = new DesignerOperations(s); var gate = Deferred<string?>();
            var read = ops.ReadAndApplyAsync(() => gate.Task, _ => s.DeleteSelection(), true);
            s.BeginDrag(); s.PreviewDrag(40, 0); Conformance.Throws<InvalidOperationException>(() => ops.CaptureRevision());
            gate.SetResult("ready"); await Throws<StaleDesignerOperationException>(read); s.CancelDrag(); Conformance.Equal(2, s.Page.Bands[0].Items.Count);
        });
        await Test("save choice cannot authorize replacing newer unsaved content", async () =>
        {
            var s = Session(); using var ops = new DesignerOperations(s); var gate = Deferred<bool>(); bool replaced = false;
            var replace = ops.ReadAndApplyAsync<ReportDocument>(async () =>
            {
                var result = await ops.SaveAsync((_, _) => gate.Task);
                return result.Written && result.Current && !s.IsDirty ? new ReportDocument() : null;
            }, _ => replaced = true);
            s.Move(4, 0); gate.SetResult(true); Conformance.Equal(false, await replace); Conformance.Equal(false, replaced); Conformance.Equal(true, s.IsDirty);
        });
        return results;
    }
}
