#if __WASM__
using System.Text.Json;
using ReportScene.Designer;
using Uno.Foundation;

namespace ReportScene.App;

/// <summary>Origin-local application preferences, deliberately separate from report recovery.</summary>
public sealed class BrowserDesignerPreferenceStore : IDesignerPreferenceStore
{
    public const string StorageKey = "reportscene.designer.preferences.v1";
    private static string J(string value) => JsonSerializer.Serialize(value, AppJsonContext.Default.String);
    public Task<string?> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string result = WebAssemblyRuntime.InvokeJS("(() => { try { const value = localStorage.getItem(" + J(StorageKey) + "); " +
            "if (value !== null && value.length > " + DesignerPreferencesJson.MaximumCharacters + ") return 'error:Stored designer preferences are too large.'; " +
            "return 'ok:' + JSON.stringify(value); } catch (e) { return 'error:' + String(e.message || e); } })()");
        if (!result.StartsWith("ok:", StringComparison.Ordinal)) throw new IOException(result.StartsWith("error:", StringComparison.Ordinal) ? result[6..] : "Invalid preference storage response.");
        string? json = JsonSerializer.Deserialize(result[3..], AppJsonContext.Default.String);
        cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(json);
    }
    public Task SaveAsync(string json, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); _ = DesignerPreferencesJson.Deserialize(json);
        string result = WebAssemblyRuntime.InvokeJS("(() => { try { localStorage.setItem(" + J(StorageKey) + "," + J(json) + "); return 'ok'; } " +
            "catch (e) { return String(e.message || e); } })()");
        if (result != "ok") throw new IOException(result);
        cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask;
    }
}
#endif
