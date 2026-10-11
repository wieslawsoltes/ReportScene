using ReportScene.Model;
using ReportScene.Serialization;

namespace ReportScene.Designer;

public sealed record DesignerSaveSnapshot(string Name, string Json, DesignerRevision Revision);
public sealed record DesignerSaveResult(bool Written, bool Current, string Name);

/// <summary>
/// Awaited workspace operations for UI-thread-owned sessions. Call on the session's owning
/// synchronization context; callbacks resume on that context. This is not a concurrent model.
/// Host writes must complete only after success, return false for cancellation, or throw.
/// </summary>
public sealed class DesignerOperations : IDisposable
{
    private readonly DesignerSession session;
    private readonly Func<bool> isAlive;
    private readonly SemaphoreSlim saves = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private bool disposed;
    private string clipboard = "";
    public DesignerOperations(DesignerSession session, Func<bool>? isAlive = null)
    { this.session = session ?? throw new ArgumentNullException(nameof(session)); this.isAlive = isAlive ?? (static () => true); }
    private void Check() => ObjectDisposedException.ThrowIf(disposed || !isAlive(), this);
    public DesignerRevision CaptureRevision() { Check(); return session.CaptureRevision(); }
    public DesignerTarget CaptureTarget() { Check(); return session.CaptureTarget(); }
    public void EnsureCurrent(DesignerRevision token) { Check(); session.EnsureCurrent(token); }
    public void EnsureCurrent(DesignerTarget token) { Check(); session.EnsureCurrent(token); }

    public async Task<DesignerSaveResult> SaveAsync(Func<DesignerSaveSnapshot, CancellationToken, Task<bool>> write, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(write); var requested = CaptureRevision();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        await saves.WaitAsync(linked.Token);
        try
        {
            // Revalidate before serialization and before touching the host. A queued command
            // from a superseded document/revision must not overwrite a later save.
            EnsureCurrent(requested); linked.Token.ThrowIfCancellationRequested();
            var problems = DocumentValidator.Validate(session.Document);
            if (problems.Any(p => p.Severity == DiagnosticSeverity.Error)) throw new FormatException(string.Join("\n", problems.Select(p => p.Message)));
            var snapshot = new DesignerSaveSnapshot(session.Document.Name, ReportJson.Serialize(session.Document), requested);
            bool written = await write(snapshot, linked.Token);
            Check();
            // An acknowledged write is real even if cancellation was requested just before
            // acknowledgement. Record its identity accurately; cancelled/failed writes do not.
            if (!written) return new(false, false, snapshot.Name);
            return new(true, session.MarkSaved(requested), snapshot.Name);
        }
        finally { saves.Release(); }
    }
    public async Task<int> CopyAsync(Func<string, Task> write)
    {
        ArgumentNullException.ThrowIfNull(write); var target = CaptureTarget();
        string text = session.Copy(); clipboard = text;
        await write(text); EnsureCurrent(target); return target.ObjectIds.Count;
    }
    public async Task CutAsync(Func<string, Task> write)
    {
        ArgumentNullException.ThrowIfNull(write); var target = CaptureTarget();
        string text = session.Copy(); clipboard = text;
        await write(text); EnsureCurrent(target); session.DeleteSelection();
    }
    public async Task<bool> PasteAsync(Func<Task<string?>> read)
    {
        ArgumentNullException.ThrowIfNull(read); var target = CaptureTarget(); string? text;
        try { text = await read(); }
        catch (Exception e) when (e is not OperationCanceledException and not ObjectDisposedException and not StaleDesignerOperationException)
        { text = clipboard; }
        // Outside the clipboard-permission fallback: cancellation or a changed target must
        // never become permission to paste the internal clipboard into another document.
        EnsureCurrent(target);
        if (string.IsNullOrWhiteSpace(text)) text = clipboard;
        if (string.IsNullOrWhiteSpace(text)) return false;
        session.Paste(text); return true;
    }
    /// <summary>Capture before starting the host read; apply synchronously only to the captured content/target.</summary>
    public async Task<bool> ReadAndApplyAsync<T>(Func<Task<T?>> read, Action<T> apply, bool selectionSensitive = false) where T : class
    {
        ArgumentNullException.ThrowIfNull(read); ArgumentNullException.ThrowIfNull(apply);
        var target = selectionSensitive ? CaptureTarget() : null;
        var content = target?.Content ?? CaptureRevision();
        var value = await read();
        Check(); if (value is null) return false;
        if (target is not null) EnsureCurrent(target); else EnsureCurrent(content);
        apply(value); return true;
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; lifetime.Cancel();
        // Do not dispose the semaphore while an in-flight host callback still owns it.
        // No WaitHandle is created; the managed gate dies with this coordinator.
    }
}
