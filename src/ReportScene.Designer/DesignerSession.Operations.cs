using ReportScene.Model;

namespace ReportScene.Designer;

/// <summary>A session-issued content identity. Loading even the same document object starts a new identity.</summary>
public sealed class DesignerRevision
{
    internal DesignerSession Owner { get; }
    internal Guid Epoch { get; }
    internal long Sequence { get; }
    public long Revision { get; }
    internal DesignerRevision(DesignerSession owner, Guid epoch, long revision, long sequence)
        => (Owner, Epoch, Revision, Sequence) = (owner, epoch, revision, sequence);
}

public sealed class DesignerTarget
{
    public DesignerRevision Content { get; }
    public int PageIndex { get; }
    public string? BandId { get; }
    public IReadOnlyList<string> ObjectIds { get; }
    internal DesignerTarget(DesignerRevision content, int pageIndex, string? bandId, IEnumerable<string> ids)
        => (Content, PageIndex, BandId, ObjectIds) = (content, pageIndex, bandId, Array.AsReadOnly(ids.ToArray()));
}

public sealed class StaleDesignerOperationException(string message) : InvalidOperationException(message);

public sealed partial class DesignerSession
{
    private Guid documentEpoch = Guid.NewGuid();
    public void Load(ReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        CancelDrag(); documentEpoch = Guid.NewGuid(); Document = document; PageIndex = 0;
        history.Clear(); position = 0; revision = savedRevision = ++sequence; selected.Clear(); SelectDefaultBand(); Notify();
    }
    public DesignerRevision CaptureRevision()
    {
        if (HasActiveDrag) throw new InvalidOperationException("Complete or cancel the active drag before starting this operation.");
        return new(this, documentEpoch, revision, sequence);
    }
    public DesignerTarget CaptureTarget() => new(CaptureRevision(), PageIndex, BandId, selected);
    public bool IsCurrent(DesignerRevision token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return ReferenceEquals(token.Owner, this) && token.Epoch == documentEpoch && token.Revision == revision && token.Sequence == sequence && !HasActiveDrag;
    }
    public bool IsCurrent(DesignerTarget token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return IsCurrent(token.Content) && token.PageIndex == PageIndex && token.BandId == BandId && selected.SetEquals(token.ObjectIds);
    }
    public void EnsureCurrent(DesignerRevision token)
    {
        if (!IsCurrent(token)) throw new StaleDesignerOperationException("The report changed while the operation was pending. No delayed result was applied; repeat the command for the current report.");
    }
    public void EnsureCurrent(DesignerTarget token)
    {
        if (!IsCurrent(token)) throw new StaleDesignerOperationException("The report, page, band or selection changed while the operation was pending. No delayed result was applied; repeat the command for the intended target.");
    }
    /// <summary>
    /// Record the revision whose bytes were successfully written, not the current revision.
    /// Newer edits stay dirty, and undoing back to the written revision becomes clean.
    /// A completion from another loaded document cannot alter this document's save state.
    /// </summary>
    public bool MarkSaved(DesignerRevision written)
    {
        ArgumentNullException.ThrowIfNull(written);
        if (!ReferenceEquals(written.Owner, this) || written.Epoch != documentEpoch) return false;
        savedRevision = written.Revision; Notify();
        return written.Epoch == documentEpoch && revision == written.Revision && !HasActiveDrag;
    }
}
