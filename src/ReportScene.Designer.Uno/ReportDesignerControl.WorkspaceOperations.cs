using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ReportScene.Export;
using ReportScene.Model;
using ReportScene.Samples;
using ReportScene.Serialization;

namespace ReportScene.Designer.Uno;

public sealed partial class ReportDesignerControl
{
    private DesignerOperations? workspaceOperations;
    private DesignerOperations Operations => workspaceOperations ??= new(Session, () => !disposed);

    private async Task<bool> DiscardAsync()
    {
        var requested = Operations.CaptureRevision();
        if (!Session.IsDirty) return true;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Save report changes?", Content = "Save your current report before opening another document.", PrimaryButtonText = "Save", SecondaryButtonText = "Discard", CloseButtonText = "Cancel" };
        var registered = new List<string>();
        dialog.Opened += (_, _) =>
        {
            var pending = new Stack<DependencyObject>(); pending.Push(dialog);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (node is Button button)
                {
                    string? id = button.Name switch { "PrimaryButton" => "save-confirm-save", "SecondaryButton" => "save-confirm-discard", "CloseButton" => "save-confirm-cancel", _ => null };
                    if (id is not null) { commands[id] = button; registered.Add(id); }
                }
                for (int n = 0; n < VisualTreeHelper.GetChildrenCount(node); n++) pending.Push(VisualTreeHelper.GetChild(node, n));
            }
            NotifyState();
        };
        ContentDialogResult result;
        try { result = await dialog.ShowAsync(); }
        finally { foreach (var id in registered) commands.Remove(id); }
        Operations.EnsureCurrent(requested);
        if (result == ContentDialogResult.None) return false;
        if (result == ContentDialogResult.Secondary) return true;
        try
        {
            var saved = await SaveCurrentAsync();
            return saved.Written && saved.Current && !Session.IsDirty;
        }
        catch (OperationCanceledException) { if (!disposed) SetStatus("Save cancelled; the current report was retained."); return false; }
    }
    private Task NewAsync() => Operations.ReadAndApplyAsync<ReportDocument>(async () =>
        await DiscardAsync() ? new ReportDocument { Name = "Untitled report", Pages = [new() { Bands = [new() { Name = "ReportTitle", Kind = BandKind.ReportTitle, Height = 120 }, new() { Name = "PageFooter", Kind = BandKind.PageFooter, Height = 24 }] }] } : null, Load);
    private Task LoadSampleAsync(string name) => Operations.ReadAndApplyAsync<ReportDocument>(async () =>
        await DiscardAsync() ? SampleReports.Create(name) : null, Load);
    private void Load(ReportDocument document)
    {
        Session.Load(document); prepared = null; PreviewView.Report = null; SetPreview(false);
        searchTerm = ""; searchHits.Clear(); searchIndex = -1; searchSession = null;
        ShowDiagnostics([]); BuildRibbon(); RefreshStructure(true); SetStatus("Loaded " + document.Name);
    }
    private Task OpenAsync() => Operations.ReadAndApplyAsync<WorkspaceFile>(async () =>
        await DiscardAsync() ? await host.OpenFileAsync([".json", ".frx"]) : null, file =>
    {
        string content = Encoding.UTF8.GetString(file.Content);
        if (file.Name.EndsWith(".frx", StringComparison.OrdinalIgnoreCase) || content.TrimStart().StartsWith('<'))
        {
            var imported = FrxSerializer.Import(content); ShowDiagnostics(imported.Diagnostics);
            if (!imported.Success) throw new FormatException("This FRX file contains unsupported features. The current document has not been replaced. See Messages for the import diagnostics.");
            Load(imported.Document); ShowDiagnostics(imported.Diagnostics);
        }
        else Load(ReportJson.Deserialize(content));
    });
    private async Task<DesignerSaveResult> SaveCurrentAsync()
    {
        var result = await Operations.SaveAsync(async (snapshot, cancellation) =>
        {
            cancellation.ThrowIfCancellationRequested();
            await host.SaveFileAsync(new(snapshot.Name + ".report.json", "application/json", Encoding.UTF8.GetBytes(snapshot.Json)));
            return true;
        });
        SetStatus(result.Written ? result.Current ? "Saved " + result.Name : "Saved " + result.Name + "; current changes remain unsaved." : "Save cancelled; the report remains unsaved.");
        return result;
    }
    private async Task SaveAsync() { await SaveCurrentAsync(); }
    private async Task SaveFrxAsync()
    {
        var revision = Operations.CaptureRevision();
        string xml = FrxSerializer.Export(Session.Document);
        await host.SaveFileAsync(new(Session.Document.Name + ".frx", "application/xml", Encoding.UTF8.GetBytes(xml)));
        Operations.EnsureCurrent(revision);
        SetStatus("FRX subset exported. Bound row data is retained in native JSON, not embedded in this FRX export.");
    }
    private async Task ExportAsync(ExportFormat format)
    {
        var revision = Operations.CaptureRevision();
        var report = prepared ?? Prepare(); prepared = report;
        var file = new ReportExporter().Export(report, format, document: Session.Document); await host.SaveFileAsync(file);
        Operations.EnsureCurrent(revision); SetStatus($"Exported {file.Name} · {file.Content.Length:N0} bytes");
    }
    private async Task PrintAsync()
    {
        var revision = Operations.CaptureRevision(); var report = prepared ?? Prepare(); prepared = report;
        await host.PrintAsync(new ReportExporter().Export(report, ExportFormat.Html));
        Operations.EnsureCurrent(revision); SetStatus("Opened the prepared report for printing.");
    }
    private async Task CopyAsync()
    { int count = await Operations.CopyAsync(host.SetClipboardTextAsync); SetStatus("Copied " + count + " object(s)"); }
    private Task CutAsync() => Operations.CutAsync(host.SetClipboardTextAsync);
    private async Task PasteAsync()
    { if (await Operations.PasteAsync(host.GetClipboardTextAsync)) DesignView.ScrollToSelection(); }
}
