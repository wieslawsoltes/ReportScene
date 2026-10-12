using ReportScene.Designer;

namespace ReportScene.Validation;

public static class DesignerPreferenceStoreTests
{
    private sealed class Store : IDesignerPreferenceStore
    {
        public string? Text;
        public int Loads, Saves;
        public Func<CancellationToken, Task<string?>>? Read;
        public Func<string, CancellationToken, Task>? Write;
        public Task<string?> LoadAsync(CancellationToken token = default)
        { Loads++; return Read?.Invoke(token) ?? Task.FromResult(Text); }
        public async Task SaveAsync(string json, CancellationToken token = default)
        { Saves++; if (Write is not null) await Write(json, token); Text = json; }
    }
    public static async Task<List<TestResult>> RunAsync()
    {
        var results = new List<TestResult>();
        async Task Test(string name, Func<Task> test)
        { try { await test(); results.Add(new("preference store: " + name, true)); } catch (Exception e) { results.Add(new("preference store: " + name, false, e.ToString())); } }
        static TaskCompletionSource<T> Signal<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        static async Task Throws<T>(Func<Task> action) where T : Exception
        { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }

        await Test("valid stored settings load once without another persistence write", async () =>
        {
            var stored = new DesignerPreferences { SidebarWidth = 530, Unit = DesignerUnit.Inches };
            var store = new Store { Text = DesignerPreferencesJson.Serialize(stored) }; using var session = new DesignerPreferenceSession(store);
            int changed = 0; session.Changed += () => changed++;
            Conformance.Equal(true, await session.LoadAsync()); Conformance.Equal(false, await session.LoadAsync());
            await session.FlushAsync(); Conformance.Equal(stored, session.Current); Conformance.Equal(1, changed);
            Conformance.Equal(1, store.Loads); Conformance.Equal(0, store.Saves); Conformance.Equal(false, session.HasPending);
        });
        await Test("missing storage leaves defaults and does not write", async () =>
        {
            var store = new Store(); using var session = new DesignerPreferenceSession(store);
            Conformance.Equal(false, await session.LoadAsync()); await session.FlushAsync();
            Conformance.Equal(new DesignerPreferences(), session.Current); Conformance.Equal(0, store.Saves);
        });
        await Test("many queued choices coalesce into one immutable save", async () =>
        {
            var store = new Store(); using var session = new DesignerPreferenceSession(store);
            for (int n = 0; n < 100; n++) session.Update(session.Current with { SidebarWidth = 300 + n });
            Conformance.Equal(true, session.HasPending); await session.FlushAsync();
            Conformance.Equal(1, store.Saves); Conformance.Equal(399d, DesignerPreferencesJson.Deserialize(store.Text!).SidebarWidth);
            Conformance.Equal(false, session.HasPending);
        });
        await Test("equal updates do not notify or queue work", async () =>
        {
            var store = new Store(); using var session = new DesignerPreferenceSession(store); int changes = 0; session.Changed += () => changes++;
            Conformance.Equal(false, session.Update(session.Current with { })); await session.FlushAsync();
            Conformance.Equal(0, changes); Conformance.Equal(0, store.Saves);
        });
        await Test("late stored values cannot overwrite a newer local choice", async () =>
        {
            var read = Signal<string?>(); var store = new Store { Read = _ => read.Task }; using var session = new DesignerPreferenceSession(store);
            var loading = session.LoadAsync(); session.Update(session.Current with { ShowGrid = false });
            read.SetResult(DesignerPreferencesJson.Serialize(new() { Unit = DesignerUnit.Inches }));
            Conformance.Equal(false, await loading); Conformance.Equal(false, session.Current.ShowGrid);
            Conformance.Equal(DesignerUnit.Points, session.Current.Unit); await session.FlushAsync();
            Conformance.Equal(false, DesignerPreferencesJson.Deserialize(store.Text!).ShowGrid);
        });
        await Test("choices made before initial load are preserved without storage access", async () =>
        {
            var store = new Store(); using var session = new DesignerPreferenceSession(store);
            session.Update(session.Current with { SidebarWidth = 350 }); Conformance.Equal(false, await session.LoadAsync());
            Conformance.Equal(0, store.Loads); await session.FlushAsync(); Conformance.Equal(350d, session.Current.SidebarWidth);
        });
        await Test("concurrent flushes serialize writes and drain the newest queued value", async () =>
        {
            var started = Signal<bool>(); var release = Signal<bool>(); var saved = new List<DesignerPreferences>(); int active = 0, maximumActive = 0;
            var store = new Store { Write = async (json, _) =>
            {
                active++; maximumActive = Math.Max(maximumActive, active); saved.Add(DesignerPreferencesJson.Deserialize(json));
                if (saved.Count == 1) { started.TrySetResult(true); await release.Task; } active--;
            } };
            using var session = new DesignerPreferenceSession(store); session.Update(session.Current with { SidebarWidth = 300 });
            var first = session.FlushAsync(); await started.Task;
            session.Update(session.Current with { SidebarWidth = 350 }); session.Update(session.Current with { SidebarWidth = 400 });
            var second = session.FlushAsync(); release.SetResult(true); await Task.WhenAll(first, second);
            Conformance.Equal(1, maximumActive); Conformance.Equal(2, saved.Count);
            Conformance.Equal(300d, saved[0].SidebarWidth); Conformance.Equal(400d, saved[1].SidebarWidth);
            Conformance.Equal(false, session.HasPending); Conformance.Equal(2L, session.WriteCount);
        });
        await Test("a failed write stays pending for explicit retry", async () =>
        {
            bool fail = true; var store = new Store { Write = (_, _) => fail ? Task.FromException(new IOException("denied")) : Task.CompletedTask };
            using var session = new DesignerPreferenceSession(store); session.Update(session.Current with { ShowRulers = false });
            await Throws<IOException>(() => session.FlushAsync()); Conformance.Equal(true, session.HasPending); Conformance.Equal(1, store.Saves);
            fail = false; await session.FlushAsync(); Conformance.Equal(false, session.HasPending); Conformance.Equal(2, store.Saves);
        });
        await Test("invalid stored JSON cannot partially change active settings", async () =>
        {
            var store = new Store { Text = "{\"version\":1,\"sidebarWidth\":-5}" }; using var session = new DesignerPreferenceSession(store);
            await Throws<FormatException>(() => session.LoadAsync()); Conformance.Equal(new DesignerPreferences(), session.Current);
            Conformance.Equal(0, store.Saves); session.RestoreDefaults(); await session.FlushAsync();
            Conformance.Equal(new DesignerPreferences(), DesignerPreferencesJson.Deserialize(store.Text!));
        });
        await Test("invalid user edits do not queue persistence or change generation", () =>
        {
            using var session = new DesignerPreferenceSession(new Store());
            Conformance.Throws<ArgumentException>(() => session.Update(session.Current with { GridSize = double.NaN }));
            Conformance.Equal(0L, session.Generation); Conformance.Equal(false, session.HasPending); return Task.CompletedTask;
        });
        await Test("pre-cancelled load and flush do not enter host callbacks", async () =>
        {
            var store = new Store(); using var session = new DesignerPreferenceSession(store); using var cancel = new CancellationTokenSource(); cancel.Cancel();
            await Throws<OperationCanceledException>(() => session.LoadAsync(cancel.Token));
            session.Update(session.Current with { ShowGrid = false }); await Throws<OperationCanceledException>(() => session.FlushAsync(cancel.Token));
            Conformance.Equal(0, store.Loads); Conformance.Equal(0, store.Saves); Conformance.Equal(true, session.HasPending);
            await session.FlushAsync(); Conformance.Equal(1, store.Saves);
        });
        await Test("disposal rejects a late load even when the host ignores cancellation", async () =>
        {
            var read = Signal<string?>(); var store = new Store { Read = _ => read.Task }; var session = new DesignerPreferenceSession(store);
            var loading = session.LoadAsync(); session.Dispose(); read.SetResult(DesignerPreferencesJson.Serialize(new() { ShowGrid = false }));
            await Throws<OperationCanceledException>(() => loading); Conformance.Equal(true, session.Current.ShowGrid);
            Conformance.Throws<ObjectDisposedException>(() => session.Update(new()));
        });
        await Test("cancelled queued flush does not invalidate an active successful write", async () =>
        {
            var release = Signal<bool>(); var started = Signal<bool>(); var store = new Store { Write = async (_, _) => { started.TrySetResult(true); await release.Task; } };
            using var session = new DesignerPreferenceSession(store); session.Update(session.Current with { SidebarWidth = 350 });
            var first = session.FlushAsync(); await started.Task; using var cancel = new CancellationTokenSource();
            var queued = session.FlushAsync(cancel.Token); cancel.Cancel(); await Throws<OperationCanceledException>(() => queued);
            release.SetResult(true); await first; Conformance.Equal(1, store.Saves); Conformance.Equal(false, session.HasPending);
        });
        await Test("an optional store is not required for in-memory options", async () =>
        {
            using var session = new DesignerPreferenceSession(); session.Update(session.Current with { Unit = DesignerUnit.Centimeters });
            Conformance.Equal(false, session.CanPersist); Conformance.Equal(false, session.HasPending);
            await session.LoadAsync(); await session.FlushAsync(); Conformance.Equal(DesignerUnit.Centimeters, session.Current.Unit);
        });
        return results;
    }
}
