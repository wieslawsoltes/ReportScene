#if __WASM__
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using ReportScene.Designer.Uno;
using ReportScene.Export;
using ReportScene.Validation;
using Uno.Foundation;

namespace ReportScene.App;

public sealed class BrowserWorkspaceHost : IReportWorkspaceHost
{
    private DispatcherTimer? diagnosticsTimer;
    public bool SelfTest { get; }
    public BrowserWorkspaceHost()
    {
        using var stream = typeof(BrowserWorkspaceHost).Assembly.GetManifestResourceStream("ReportScene.BrowserBridge.js") ?? throw new InvalidOperationException("The browser host bridge is missing.");
        using var reader = new StreamReader(stream); WebAssemblyRuntime.InvokeJS(reader.ReadToEnd());
        SelfTest = WebAssemblyRuntime.InvokeJS("String(new URLSearchParams(location.search).get('selftest') === '1')") == "true";
    }
    private static string J(string value) => JsonSerializer.Serialize(value, AppJsonContext.Default.String);
    private static string Invoke(string expression) => WebAssemblyRuntime.InvokeJS(expression);
    public void Attach(ReportDesignerControl designer)
    {
        bool diagnostics = SelfTest || Invoke("String(new URLSearchParams(location.search).get('diagnostics') === '1')") == "true";
        if (diagnostics)
        {
            diagnosticsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            diagnosticsTimer.Tick += (_, _) =>
            {
                try { var snapshot = designer.Snapshot(); Invoke("window.reportScene.state=" + JsonSerializer.Serialize(snapshot, DesignerSnapshotJsonContext.Default.DesignerSnapshot) + ";window.reportScene.ready=" + (snapshot.RenderCount > 0 ? "true" : "false") + ";"); }
                catch (Exception e) { Invoke("window.reportScene.diagnosticError=" + J(e.ToString()) + ";"); }
            };
            diagnosticsTimer.Start();
        }
        if (SelfTest)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try { var results = ConformanceSuite.Run(); Invoke("window.reportScene.tests=" + JsonSerializer.Serialize(results, ValidationJsonContext.Default.ListTestResult) + ";"); }
                catch (Exception e) { Invoke("window.reportScene.testFatal=" + J(e.ToString()) + ";"); }
            };
            timer.Start();
        }
    }
    public async Task<WorkspaceFile?> OpenFileAsync(IReadOnlyList<string> extensions)
    {
        string id = Guid.NewGuid().ToString("N"); Invoke($"window.reportSceneHost.pick({J(id)},{J(string.Join(',', extensions))})");
        var result = await ResultAsync(id); if (result.Cancelled) return null;
        return new(result.Name ?? "report.json", Convert.FromBase64String(result.Data ?? ""));
    }
    public Task SaveFileAsync(ExportFile file)
    { Invoke($"window.reportSceneHost.download({J(file.Name)},{J(file.MediaType)},{J(Convert.ToBase64String(file.Content))})"); return Task.CompletedTask; }
    public Task PrintAsync(ExportFile html)
    {
        string result = Invoke($"window.reportSceneHost.print({J(System.Text.Encoding.UTF8.GetString(html.Content))})");
        if (result != "ok") throw new InvalidOperationException(result); return Task.CompletedTask;
    }
    public async Task<string?> LoadRecoveryAsync()
    {
        string id = Guid.NewGuid().ToString("N"); Invoke($"window.reportSceneHost.recover({J(id)})"); return (await ResultAsync(id)).Text;
    }
    public async Task SaveRecoveryAsync(string json)
    {
        string id = Guid.NewGuid().ToString("N"); Invoke($"window.reportSceneHost.saveRecovery({J(id)},{J(json)})"); await ResultAsync(id);
    }
    public async Task SetClipboardTextAsync(string text)
    {
        string id = Guid.NewGuid().ToString("N"); Invoke($"window.reportSceneHost.copy({J(id)},{J(text)})"); await ResultAsync(id);
    }
    public async Task<string?> GetClipboardTextAsync()
    {
        string id = Guid.NewGuid().ToString("N"); Invoke($"window.reportSceneHost.paste({J(id)})"); return (await ResultAsync(id)).Text;
    }
    private static async Task<BridgeResult> ResultAsync(string id)
    {
        for (int n = 0; n < 6000; n++)
        {
            string json = Invoke($"window.reportSceneHost.take({J(id)})");
            if (!string.IsNullOrEmpty(json))
            {
                var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.BridgeResult) ?? throw new InvalidOperationException("Invalid browser response.");
                if (result.Error is not null) throw new InvalidOperationException(result.Error);
                return result;
            }
            await Task.Delay(50);
        }
        throw new TimeoutException("The browser operation did not complete.");
    }
}
#endif
