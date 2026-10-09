#if !__WASM__
using System.Diagnostics;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using ReportScene.Designer.Uno;
using ReportScene.Export;

namespace ReportScene.App;

public sealed class DesktopWorkspaceHost : IReportWorkspaceHost
{
    public async Task<WorkspaceFile?> OpenFileAsync(IReadOnlyList<string> extensions)
    {
        var picker = new FileOpenPicker(); foreach (var extension in extensions) picker.FileTypeFilter.Add(extension);
        var file = await picker.PickSingleFileAsync(); if (file is null) return null;
        using var input = await file.OpenStreamForReadAsync(); if (input.Length > 64_000_000) throw new FormatException("Input exceeds the 64 MB limit.");
        using var output = new MemoryStream(); await input.CopyToAsync(output); return new(file.Name, output.ToArray());
    }
    public async Task SaveFileAsync(ExportFile file)
    {
        var picker = new FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(file.Name) };
        picker.FileTypeChoices.Add("ReportScene file", new List<string> { Path.GetExtension(file.Name) });
        var target = await picker.PickSaveFileAsync(); if (target is null) throw new OperationCanceledException("Save canceled.");
        using var output = await target.OpenStreamForWriteAsync(); output.SetLength(0); await output.WriteAsync(file.Content);
    }
    public async Task PrintAsync(ExportFile html)
    {
        var file = await ApplicationData.Current.TemporaryFolder.CreateFileAsync("ReportScene-print-" + Guid.NewGuid().ToString("N") + ".html", CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteBytesAsync(file, html.Content);
        Process.Start(new ProcessStartInfo(file.Path) { UseShellExecute = true });
    }
    public async Task<string?> LoadRecoveryAsync()
    {
        var file = await ApplicationData.Current.LocalFolder.TryGetItemAsync("reportscene-recovery.json") as StorageFile;
        return file is null ? null : await FileIO.ReadTextAsync(file);
    }
    public async Task SaveRecoveryAsync(string json)
    {
        var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("reportscene-recovery.json", CreationCollisionOption.ReplaceExisting); await FileIO.WriteTextAsync(file, json);
    }
    public Task SetClipboardTextAsync(string text) { var package = new DataPackage(); package.SetText(text); Clipboard.SetContent(package); return Task.CompletedTask; }
    public async Task<string?> GetClipboardTextAsync() { var content = Clipboard.GetContent(); return content.Contains(StandardDataFormats.Text) ? await content.GetTextAsync() : null; }
}
#endif
