namespace ReportScene.Designer;

/// <summary>Optional host storage for the small settings record, separate from report recovery.</summary>
public interface IDesignerPreferenceStore
{
    Task<string?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(string json, CancellationToken cancellationToken = default);
}

/// <summary>
/// Owns immutable application preferences, not a report. Explicit flushes coalesce pending
/// updates and serialize writes. A failed/cancelled save remains pending for a later retry.
/// Events run on the calling context; a UI host must invoke this service on its dispatcher.
/// </summary>
public sealed class DesignerPreferenceSession : IDisposable
{
    private readonly object sync = new();
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly IDesignerPreferenceStore? store;
    private DesignerPreferences current;
    private long generation, completed, writeCount;
    private bool disposed, loadStarted;
    public event Action? Changed;
    public DesignerPreferences Current { get { lock (sync) return current; } }
    public long Generation { get { lock (sync) return generation; } }
    public long WriteCount { get { lock (sync) return writeCount; } }
    public bool CanPersist => store is not null;
    public bool HasPending { get { lock (sync) return store is not null && generation != completed; } }

    public DesignerPreferenceSession(IDesignerPreferenceStore? store = null, DesignerPreferences? initial = null)
    { this.store = store; current = (initial ?? new()).Validate(); }

    public bool Update(DesignerPreferences value, bool forceSave = false)
    {
        ArgumentNullException.ThrowIfNull(value); value.Validate();
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!forceSave && current == value) return false;
            current = value; generation++;
        }
        Changed?.Invoke(); return true;
    }
    public void RestoreDefaults() => Update(new(), forceSave: true);

    /// <summary>Load once; any prior or intervening local choice wins over stored settings.</summary>
    public async Task<bool> LoadAsync(CancellationToken cancellationToken = default)
    {
        using var linked = OperationToken(cancellationToken); var token = linked.Token;
        token.ThrowIfCancellationRequested();
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (loadStarted || generation != 0 || store is null) return false;
            loadStarted = true;
        }
        string? json = await store.LoadAsync(token); token.ThrowIfCancellationRequested();
        lock (sync) { if (generation != 0) return false; }
        var restored = json is null ? new DesignerPreferences() : DesignerPreferencesJson.Deserialize(json);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this); token.ThrowIfCancellationRequested();
            if (generation != 0 || restored == current) return false;
            current = restored;
        }
        Changed?.Invoke(); return true;
    }
    public Task FlushAsync(CancellationToken cancellationToken = default) => FlushWhenReadyAsync(static () => true, cancellationToken);

    /// <summary>
    /// Coalesce pending values and drain newer changes after each acknowledged write.
    /// The host readiness check runs before every snapshot, including after awaiting a
    /// write. A false result defers pending work; it never captures transient drag state.
    /// Failures require an explicit retry or another host-scheduled flush, not idle polling.
    /// </summary>
    public async Task FlushWhenReadyAsync(Func<bool> canSnapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(canSnapshot);
        using var linked = OperationToken(cancellationToken); var token = linked.Token;
        token.ThrowIfCancellationRequested(); if (store is null) return;
        await writeGate.WaitAsync(token);
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (!canSnapshot()) return;
                DesignerPreferences value; long requested;
                lock (sync)
                {
                    ObjectDisposedException.ThrowIf(disposed, this); token.ThrowIfCancellationRequested();
                    if (generation == completed) return;
                    requested = generation; value = current;
                }
                string json = DesignerPreferencesJson.Serialize(value);
                await store.SaveAsync(json, token); token.ThrowIfCancellationRequested();
                lock (sync) { completed = requested; writeCount++; }
            }
        }
        finally { writeGate.Release(); }
    }
    private CancellationTokenSource OperationToken(CancellationToken cancellationToken)
    {
        CancellationToken token;
        lock (sync) { ObjectDisposedException.ThrowIf(disposed, this); token = lifetime.Token; }
        return CancellationTokenSource.CreateLinkedTokenSource(token, cancellationToken);
    }
    public void Dispose()
    {
        lock (sync) { if (disposed) return; disposed = true; }
        lifetime.Cancel();
        // The gate can still belong to an awaited host callback; existing operations
        // observe cancellation and release it themselves. No new operation can start.
    }
}
