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
        style = source[0].Style; SourceName = source[0].Name; revision = session.CaptureRevision(); repeating = repeat;
        StateChanged?.Invoke(); return true;
    }
    public DesignerFormattingResult Apply(IReadOnlyCollection<string> objectIds)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ArgumentNullException.ThrowIfNull(objectIds);
        if (!IsActive) { Cancel(); return default; }
        applying = true;
        try
        {
            var result = session.ApplyFormatting(style!, objectIds);
            // Equal formatting consumes the one-shot tool without inventing an edit.
            // Missing/locked targets cannot silently consume a captured tool.
            if (result.EditableObjects > 0 && revision is not null)
            {
                if (repeating) revision = session.CaptureRevision(); else Cancel();
            }
            return result;
        }
        finally { applying = false; }
    }
    public DesignerFormattingResult ApplyToSelection() => Apply(session.SelectedIds.ToArray());
    public void Cancel()
    {
        if (revision is null) return;
        revision = null; style = null; repeating = false; SourceName = ""; StateChanged?.Invoke();
    }
    private void SessionChanged(object? sender, EventArgs e)
    {
        if (revision is null || session.IsCurrent(revision)) return;
        if (applying && repeating && !session.HasActiveDrag) revision = session.CaptureRevision();
        else Cancel();
    }
    public void Dispose()
    {
        if (disposed) return;
        session.Changed -= SessionChanged; disposed = true; Cancel(); StateChanged = null;
    }
}
