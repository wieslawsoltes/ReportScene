using ReportScene.Export;

namespace ReportScene.Designer.Uno;

public sealed record WorkspaceFile(string Name, byte[] Content);

/// <summary>Platform integration is injected; the designer never opens database connections or executes report scripts.</summary>
public interface IReportWorkspaceHost
{
    /// <summary>Return null for cancellation. A delayed result is applied only if its initiating document/target is still current.</summary>
    Task<WorkspaceFile?> OpenFileAsync(IReadOnlyList<string> extensions);
    /// <summary>
    /// Complete successfully only when the host accepted the supplied immutable bytes. Throw
    /// OperationCanceledException for cancellation, and an exception for failure; never silently
    /// return success for a cancelled picker. Browser download acceptance is not disk durability.
    /// Normal native-report saves are serialized by the designer's operations coordinator.
    /// </summary>
    Task SaveFileAsync(ExportFile file);
    Task PrintAsync(ExportFile html);
    Task<string?> LoadRecoveryAsync();
    Task SaveRecoveryAsync(string json);
    Task SetClipboardTextAsync(string text);
    Task<string?> GetClipboardTextAsync();
}
