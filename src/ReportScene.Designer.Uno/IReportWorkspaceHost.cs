using ReportScene.Export;

namespace ReportScene.Designer.Uno;

public sealed record WorkspaceFile(string Name, byte[] Content);

/// <summary>Platform integration is injected; the designer never opens database connections or executes report scripts.</summary>
public interface IReportWorkspaceHost
{
    Task<WorkspaceFile?> OpenFileAsync(IReadOnlyList<string> extensions);
    Task SaveFileAsync(ExportFile file);
    Task PrintAsync(ExportFile html);
    Task<string?> LoadRecoveryAsync();
    Task SaveRecoveryAsync(string json);
    Task SetClipboardTextAsync(string text);
    Task<string?> GetClipboardTextAsync();
}
