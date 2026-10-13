using ReportScene.Model;

namespace ReportScene.Designer;

/// <summary>
/// UI-independent, document-scoped formatting tool. Capture never edits the report.
/// A single click consumes one-shot mode; repeated mode survives its own edits only.
/// External edits, undo/redo, reload and disposal invalidate captured formatting.
/// </summary>
public sealed class DesignerFormatPainter : IDisposable
{
    private readonly DesignerSession session;
    private DesignerRevision? revision;
    private ItemStyle? style;
    private bool repeating, applying, disposed;
    private long generation, applyingGeneration;
    public bool IsActive => !disposed && revision is not null && session.IsCurrent(revision);
    public bool IsRepeating => IsActive && repeating;
    public string SourceName { get; private set; } = "";
    public event Action? StateChanged;
    public DesignerFormatPainter(DesignerSession session)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        session.Changed += SessionChanged;
    }
    public bool Begin(bool repeat = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (session.HasActiveDrag) return false;
        var source = session.Selection.Take(2).ToArray();
        if (source.Length != 1) return false;
        DesignerSession.ValidateFormatting(source[0].Style);
        // ItemStyle is an immutable sealed record; no document serialization or data copy.
        style = source[0].Style; SourceName = source[0].Name; revision = session.CaptureRevision(); repeating = repeat; generation++;
        StateChanged?.Invoke(); return true;
    }
    public DesignerFormattingResult Apply(IReadOnlyCollection<string> objectIds)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ArgumentNullException.ThrowIfNull(objectIds);
        if (applying) throw new InvalidOperationException("A formatting operation is already notifying its observers.");
        if (!IsActive) { Cancel(); return default; }
        var originalRevision = revision!;
        applying = true; applyingGeneration = generation;
        try
        {
            var result = session.ApplyFormattingCore(style!, objectIds, FormattingCommitted);
            // Equal formatting consumes the one-shot tool without inventing an edit.
            // Missing/locked targets cannot silently consume a captured tool.
            if (revision is not null && generation == applyingGeneration
                && (!session.IsCurrent(revision) || result.EditableObjects > 0 && !repeating)) Cancel();
            return result;
        }
        catch
        {
            // Validation errors before mutation retain the capture. A throwing
            // observer after a commit must not leave an ambiguous active tool.
            if (generation == applyingGeneration && !session.IsCurrent(originalRevision)) Cancel();
            throw;
        }
        finally { applying = false; }
    }
    public DesignerFormattingResult ApplyToSelection() => Apply(session.SelectedIds.ToArray());
    public void Cancel()
    {
        if (revision is null) return;
        revision = null; style = null; repeating = false; SourceName = ""; generation++; StateChanged?.Invoke();
    }
    private void FormattingCommitted(DesignerRevision committed)
    {
        if (!disposed && applying && revision is not null && generation == applyingGeneration)
            revision = committed;
    }
    private void SessionChanged(object? sender, EventArgs e)
    {
        if (revision is not null && !session.IsCurrent(revision)) Cancel();
    }
    public void Dispose()
    {
        if (disposed) return;
        session.Changed -= SessionChanged; disposed = true; Cancel(); StateChanged = null;
    }
}
