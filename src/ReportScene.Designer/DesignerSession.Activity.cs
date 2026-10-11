namespace ReportScene.Designer;

public sealed partial class DesignerSession
{
    /// <summary>Content-history revision; navigation, selection and MarkSaved do not change it.</summary>
    public long Revision => revision;
    public bool HasActiveDrag => dragOriginal is not null;
}

public readonly record struct DesignerActivity(bool InvalidatePrepared, bool RefreshStructure, bool SaveRecovery);

/// <summary>Separates document mutations from view navigation and transient drag notifications.</summary>
public sealed class DesignerActivityTracker
{
    private long revision;
    private bool dragging;
    public DesignerActivityTracker(DesignerSession session)
    { ArgumentNullException.ThrowIfNull(session); revision = session.Revision; dragging = session.HasActiveDrag; }
    public DesignerActivity Observe(DesignerSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        bool committed = revision != session.Revision, active = session.HasActiveDrag;
        var result = new DesignerActivity(committed || active || dragging, !active, committed);
        revision = session.Revision; dragging = active; return result;
    }
}

/// <summary>
/// Serializes recovery writes and drains edits requested during an awaited save. Revisions
/// are request generations (not undo positions), so undo/redo cannot hide a pending save.
/// A failed or deferred save remains pending and can be retried explicitly by the host.
/// </summary>
public sealed class RecoverySaveCoordinator
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private long requested, completed;
    public bool HasPending => Interlocked.Read(ref requested) != Interlocked.Read(ref completed);
    public void Request() => Interlocked.Increment(ref requested);
    public Task FlushAsync(Func<string> snapshot, Func<string, Task> save, CancellationToken cancellationToken = default)
        => FlushWhenReadyAsync(static () => true, snapshot, save, cancellationToken);

    /// <summary>
    /// The host's synchronous readiness check runs immediately before each snapshot, also
    /// after an awaited write. Returning false defers remaining requests without capturing
    /// transient drag state. Call again after the host is ready. Callbacks retain the caller's
    /// synchronization context; hosts must synchronize concurrent edits with snapshotting.
    /// </summary>
    public async Task FlushWhenReadyAsync(Func<bool> canSnapshot, Func<string> snapshot, Func<string, Task> save, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(canSnapshot); ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(save);
        await gate.WaitAsync(cancellationToken);
        try
        {
            while (HasPending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!canSnapshot()) return;
                long generation = Interlocked.Read(ref requested); string json = snapshot();
                await save(json); Interlocked.Exchange(ref completed, generation);
            }
        }
        finally { gate.Release(); }
    }
}
