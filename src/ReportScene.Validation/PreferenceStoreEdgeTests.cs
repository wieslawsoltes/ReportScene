using System.Text;
using ReportScene.Designer;

namespace ReportScene.Validation;

public static class PreferenceStoreEdgeTests
{
    private sealed class Host : IDesignerPreferenceStore
    {
        public int Writes; public Func<string, Task> OnSave = _ => Task.CompletedTask;
        public Task<string?> LoadAsync(CancellationToken token = default) => Task.FromResult<string?>(null);
        public Task SaveAsync(string text, CancellationToken token = default) { Writes++; return OnSave(text); }
    }
    public static async Task<List<TestResult>> RunAsync()
    {
        var results = new List<TestResult>();
        async Task Test(string name, Func<Task> run)
        { try { await run(); results.Add(new("preference edges: " + name, true)); } catch (Exception error) { results.Add(new("preference edges: " + name, false, error.ToString())); } }
        static async Task Throws<T>(Func<Task> run) where T : Exception
        { try { await run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
        await Test("readiness defers pending snapshots without entering the store", async () =>
        {
            var host = new Host(); using var session = new DesignerPreferenceSession(host);
            session.Update(session.Current with { SidebarWidth = 320 });
            await session.FlushWhenReadyAsync(() => false); Conformance.Equal(0, host.Writes); Conformance.Equal(true, session.HasPending);
            await session.FlushWhenReadyAsync(() => true); Conformance.Equal(1, host.Writes); Conformance.Equal(false, session.HasPending);
        });
        await Test("readiness is rechecked after an awaited save before capturing newer choices", async () =>
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var saved = new List<double>(); bool ready = true;
            var host = new Host { OnSave = async json => { saved.Add(DesignerPreferencesJson.Deserialize(json).SidebarWidth); if (saved.Count == 1) { entered.SetResult(true); await release.Task; } } };
            using var session = new DesignerPreferenceSession(host); session.Update(session.Current with { SidebarWidth = 300 });
            var flush = session.FlushWhenReadyAsync(() => ready); await entered.Task;
            session.Update(session.Current with { SidebarWidth = 320 }); ready = false; release.SetResult(true); await flush;
            Conformance.Equal(1, host.Writes); Conformance.Equal(true, session.HasPending);
            ready = true; await session.FlushWhenReadyAsync(() => ready); Conformance.Equal("300,320", string.Join(',', saved));
        });
        await Test("pre-cancelled readiness check cannot swallow cancellation", async () =>
        {
            var host = new Host(); using var session = new DesignerPreferenceSession(host); session.RestoreDefaults();
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Throws<OperationCanceledException>(() => session.FlushWhenReadyAsync(() => false, cancellation.Token));
            Conformance.Equal(0, host.Writes); Conformance.Equal(true, session.HasPending);
        });
        if (OperatingSystem.IsBrowser())
        {
            await Test("filesystem store rejects browser construction before path access", () =>
            {
                Conformance.Throws<PlatformNotSupportedException>(() => new FileDesignerPreferenceStore("unused.json")); return Task.CompletedTask;
            });
            return results;
        }
        string directory = Path.Combine(Path.GetTempPath(), "ReportScene-preferences-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "settings.json"); var store = new FileDesignerPreferenceStore(path);
            await Test("native missing-file load returns no settings", async () => Conformance.Equal(true, await store.LoadAsync() is null));
            await Test("native writes replace an existing complete record without leftover temp files", async () =>
            {
                await store.SaveAsync(DesignerPreferencesJson.Serialize(new() { SidebarWidth = 400 }));
                await store.SaveAsync(DesignerPreferencesJson.Serialize(new() { SidebarWidth = 500 }));
                Conformance.Equal(500d, DesignerPreferencesJson.Deserialize((await store.LoadAsync())!).SidebarWidth);
                Conformance.Equal(1, Directory.GetFiles(directory).Length);
            });
            await Test("invalid and cancelled native writes preserve existing bytes", async () =>
            {
                string old = await File.ReadAllTextAsync(path);
                await Throws<FormatException>(() => store.SaveAsync("{\"version\":2}"));
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                await Throws<OperationCanceledException>(() => store.SaveAsync(DesignerPreferencesJson.Serialize(new()), cancelled.Token));
                Conformance.Equal(old, await File.ReadAllTextAsync(path)); Conformance.Equal(1, Directory.GetFiles(directory).Length);
            });
            await Test("native reads enforce byte and decoded character limits", async () =>
            {
                await File.WriteAllTextAsync(path, new string('x', DesignerPreferencesJson.MaximumCharacters + 1));
                await Throws<FormatException>(() => store.LoadAsync());
                await File.WriteAllBytesAsync(path, new byte[DesignerPreferencesJson.MaximumCharacters * 4 + 1]);
                await Throws<FormatException>(() => store.LoadAsync());
            });
            await Test("native malformed UTF8 cannot silently change settings", async () =>
            {
                await File.WriteAllBytesAsync(path, [0xC3, 0x28]);
                await Throws<DecoderFallbackException>(() => store.LoadAsync());
            });
            await Test("cancelled native read does not enter file IO", async () =>
            {
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                await Throws<OperationCanceledException>(() => store.LoadAsync(cancelled.Token));
            });
        }
        finally { Directory.Delete(directory, recursive: true); }
        return results;
    }
}
