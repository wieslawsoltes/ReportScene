using ReportScene.Model;
using ReportScene.Serialization;

namespace ReportScene.Designer;

public enum AlignmentCommand { Left, Center, Right, Top, Middle, Bottom, SameWidth, SameHeight, DistributeHorizontal, DistributeVertical }

/// <summary>Delta-based history keeps data tables out of per-pointer-move snapshots. One drag creates one undo entry.</summary>
public sealed partial class DesignerSession
{
    private sealed record Edit(string Name, Action Redo, Action Undo, long Before, long After, string[] BeforeSelection, string[] AfterSelection);
    private readonly List<Edit> history = [];
    private readonly HashSet<string> selected = new(StringComparer.Ordinal);
    private int position;
    private long sequence, revision, savedRevision;
    private Dictionary<string, ReportItem>? dragOriginal;
    public ReportDocument Document { get; private set; }
    public int PageIndex { get; private set; }
    public string? BandId { get; private set; }
    public IReadOnlySet<string> SelectedIds => selected;
    public bool CanUndo => position > 0;
    public bool CanRedo => position < history.Count;
    public bool IsDirty => revision != savedRevision;
    public string UndoName => CanUndo ? history[position - 1].Name : "";
    public string RedoName => CanRedo ? history[position].Name : "";
    public int HistoryCount => history.Count;
    public bool SnapToGrid { get; set; } = true;
    public double GridSize { get; set; } = 4;
    public event EventHandler? Changed;
    public event EventHandler? SelectionChanged;
    public ReportPage Page => Document.Pages[PageIndex];
    public IEnumerable<ReportItem> Selection => DocumentTree.AllItems(Document).Where(i => selected.Contains(i.Id));
    public Band? SelectedBand => DocumentTree.AllBands(Document).FirstOrDefault(b => b.Id == BandId);
    public DesignerSession(ReportDocument document) { Document = document; SelectDefaultBand(); }
    private void SelectDefaultBand() => BandId = Page.Bands.FirstOrDefault(b => b.Kind == BandKind.Data)?.Id ?? Page.Bands.FirstOrDefault()?.Id;
    public void SetPage(int index)
    {
        if (index < 0 || index >= Document.Pages.Count) throw new ArgumentOutOfRangeException(nameof(index));
        CancelDrag(); PageIndex = index; selected.Clear(); SelectDefaultBand(); Notify();
    }
    public void MarkSaved() { savedRevision = revision; Notify(); }
    public void Select(string? id, bool toggle = false)
    {
        if (!toggle) selected.Clear();
        if (id is not null)
        {
            if (toggle && selected.Contains(id)) selected.Remove(id); else selected.Add(id);
            var owner = DocumentTree.AllBands(Document).FirstOrDefault(b => b.Items.Any(i => i.Id == id));
            if (owner is not null) BandId = owner.Id;
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    public void SelectBand(string id) { selected.Clear(); BandId = id; SelectionChanged?.Invoke(this, EventArgs.Empty); }
    public void SelectAll()
    { selected.Clear(); foreach (var b in Page.Bands.SelectMany(DocumentTree.Descendants)) foreach (var item in b.Items) selected.Add(item.Id); SelectionChanged?.Invoke(this, EventArgs.Empty); }
    public void SelectMany(IEnumerable<string> ids, bool additive = false)
    { if (!additive) selected.Clear(); foreach (var id in ids) selected.Add(id); SelectionChanged?.Invoke(this, EventArgs.Empty); }
    private void Notify() { Changed?.Invoke(this, EventArgs.Empty); SelectionChanged?.Invoke(this, EventArgs.Empty); }
    private void Execute(string name, Action redo, Action undo, string[]? afterSelection = null, Action<DesignerRevision>? committed = null)
    {
        if (dragOriginal is not null) throw new InvalidOperationException("Complete or cancel the active drag before another edit.");
        var before = selected.ToArray(); var after = afterSelection ?? before; redo();
        if (position < history.Count) history.RemoveRange(position, history.Count - position);
        var edit = new Edit(name, redo, undo, revision, ++sequence, before, after);
        history.Add(edit); position++; revision = edit.After;
        if (history.Count > 256) { history.RemoveAt(0); position--; }
        selected.Clear(); selected.UnionWith(after);
        // An internal operation owner receives only this exact committed identity,
        // before public observers can make a different edit. Never retain the
        // callback in history: undo/redo must not revive the original tool.
        committed?.Invoke(CaptureRevision());
        Notify();
    }
    public void Undo()
    {
        CancelDrag(); if (!CanUndo) return;
        var edit = history[--position]; edit.Undo(); revision = edit.Before;
        selected.Clear(); selected.UnionWith(edit.BeforeSelection); PageIndex = Math.Min(PageIndex, Document.Pages.Count - 1); Notify();
    }
    public void Redo()
    {
        CancelDrag(); if (!CanRedo) return;
        var edit = history[position++]; edit.Redo(); revision = edit.After;
        selected.Clear(); selected.UnionWith(edit.AfterSelection); PageIndex = Math.Min(PageIndex, Document.Pages.Count - 1); Notify();
    }
    private void Replace(string id, ReportItem item)
    {
        if (item.Id != id) throw new ArgumentException("Object edits must retain their identifiers.");
        ReplaceItems([item]);
    }
    public void UpdateSelection(string description, Func<ReportItem, ReportItem> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var before = Selection.Where(i => !i.Locked).ToArray(); if (before.Length == 0) return;
        var after = before.Select(change).ToArray();
        for (int n = 0; n < after.Length; n++)
        {
            if (after[n] is null || before[n].Id != after[n].Id) throw new ArgumentException("Object edits must retain their identifiers.");
            DesignerGeometry.ValidateBounds(after[n].Bounds);
        }
        if (before.SequenceEqual(after)) return;
        ValidateRenames(before, after);
        if (after.Any(i => i is SubreportItem))
        {
            var replacements = after.ToDictionary(i => i.Id);
            ValidateReferences(MapDocument(Document, b => b with { Items = b.Items.Select(i => replacements.GetValueOrDefault(i.Id, i)).ToList() }));
        }
        Execute(description, () => ReplaceItems(after), () => ReplaceItems(before));
    }
    public ReportItem Add(ReportItem item, string? bandId = null)
    {
        ArgumentNullException.ThrowIfNull(item); DesignerGeometry.ValidateBounds(item.Bounds);
        var band = DocumentTree.AllBands(Document).FirstOrDefault(b => b.Id == (bandId ?? BandId)) ?? throw new InvalidOperationException("Select a band before inserting an object.");
        var added = item with { Id = Guid.NewGuid().ToString("N"), Name = UniqueName(item.Name) };
        if (added is SubreportItem) ValidateReferences(MapDocument(Document, b => b.Id == band.Id ? b with { Items = [.. b.Items, added] } : b));
        Execute("Insert " + added.Name, () => band.Items.Add(added), () => band.Items.RemoveAll(i => i.Id == added.Id), [added.Id]); return added;
    }
    private string UniqueName(string prefix) => ReserveName(prefix, DocumentTree.AllItems(Document).Select(i => i.Name).ToHashSet(StringComparer.OrdinalIgnoreCase));
    public void DeleteSelection()
    {
        var entries = DocumentTree.AllBands(Document).SelectMany(b => b.Items.Select((item, index) => (Band: b, Item: item, Index: index))).Where(e => selected.Contains(e.Item.Id) && !e.Item.Locked).ToArray();
        if (entries.Length == 0) return;
        var ids = entries.Select(e => e.Item.Id).ToHashSet(StringComparer.Ordinal);
        var bands = entries.Select(e => e.Band).DistinctBy(b => b.Id).ToArray();
        Execute("Delete objects", () => { foreach (var b in bands) b.Items.RemoveAll(i => ids.Contains(i.Id)); }, () => { foreach (var e in entries) e.Band.Items.Insert(e.Index, e.Item); }, []);
    }
    public string Copy() => ReportJson.SerializeItems(Selection);
    public void Paste(string json)
    {
        var band = SelectedBand ?? throw new InvalidOperationException("Select a band before pasting.");
        var source = ReportJson.DeserializeItems(json); if (source.Count == 0) return;
        var names = DocumentTree.AllItems(Document).Select(i => i.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = source.Select(i => i with { Id = Guid.NewGuid().ToString("N"), Name = ReserveName(i.Name + "Copy", names), Bounds = i.Bounds.Offset(8, 8), Locked = false }).ToArray();
        foreach (var item in added) DesignerGeometry.ValidateBounds(item.Bounds);
        if (added.Any(i => i is SubreportItem)) ValidateReferences(MapDocument(Document, b => b.Id == band.Id ? b with { Items = [.. b.Items, .. added] } : b));
        var ids = added.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        Execute("Paste objects", () => band.Items.AddRange(added), () => band.Items.RemoveAll(i => ids.Contains(i.Id)), added.Select(i => i.Id).ToArray());
    }
    public void Move(double dx, double dy)
    {
        var items = Selection.Where(i => !i.Locked).ToArray(); if (items.Length == 0) return;
        dx = Math.Max(dx, -items.Min(i => i.Bounds.X)); dy = Math.Max(dy, -items.Min(i => i.Bounds.Y)); UpdateSelection("Move objects", i => i with { Bounds = i.Bounds.Offset(dx, dy) });
    }
    public void BeginDrag()
    {
        if (dragOriginal is not null) CancelDrag();
        var originals = Selection.Where(i => !i.Locked).ToDictionary(i => i.Id); dragOriginal = originals.Count == 0 ? null : originals;
    }
    public void PreviewDrag(double dx, double dy, bool resize = false)
    {
        if (resize) { PreviewResize(dx, dy, ResizeHandle.BottomRight); return; }
        if (dragOriginal is null) return; (dx, dy) = SnapDeltas(dx, dy);
        dx = Math.Max(dx, -dragOriginal.Values.Min(i => i.Bounds.X)); dy = Math.Max(dy, -dragOriginal.Values.Min(i => i.Bounds.Y));
        var after = dragOriginal.Values.Select(i => i with { Bounds = i.Bounds.Offset(dx, dy) }).ToArray();
        if (ReplaceItems(after)) Changed?.Invoke(this, EventArgs.Empty);
    }
    public void PreviewResize(double dx, double dy, ResizeHandle handle)
    {
        if (dragOriginal is null) return; (dx, dy) = SnapDeltas(dx, dy);
        var after = dragOriginal.Values.Select(i => i with { Bounds = DesignerGeometry.Resize(i.Bounds, dx, dy, handle) }).ToArray();
        if (ReplaceItems(after)) Changed?.Invoke(this, EventArgs.Empty);
    }
    public void CommitDrag()
    {
        if (dragOriginal is null) return;
        var before = dragOriginal.Values.ToArray(); var current = DocumentTree.AllItems(Document).Where(i => dragOriginal.ContainsKey(i.Id)).ToDictionary(i => i.Id);
        var after = before.Select(i => current[i.Id]).ToArray(); dragOriginal = null;
        if (before.SequenceEqual(after)) return;
        Execute("Transform objects", () => ReplaceItems(after), () => ReplaceItems(before));
    }
    public void CancelDrag()
    { if (dragOriginal is null) return; ReplaceItems(dragOriginal.Values.ToArray()); dragOriginal = null; Changed?.Invoke(this, EventArgs.Empty); }
    public void Align(AlignmentCommand command)
    {
        var items = Selection.Where(i => !i.Locked).ToArray(); if (items.Length < 2) return;
        var anchor = items[0].Bounds;
        var sorted = items.OrderBy(i => command == AlignmentCommand.DistributeVertical ? i.Bounds.Y : i.Bounds.X).ToArray();
        var changes = new Dictionary<string, RectD>();
        for (int n = 0; n < items.Length; n++)
        {
            var item = items[n]; var b = item.Bounds;
            changes[item.Id] = command switch
            {
                AlignmentCommand.Left => b with { X = anchor.X }, AlignmentCommand.Right => b with { X = Math.Max(0, anchor.Right - b.Width) }, AlignmentCommand.Center => b with { X = Math.Max(0, anchor.X + (anchor.Width - b.Width) / 2) },
                AlignmentCommand.Top => b with { Y = anchor.Y }, AlignmentCommand.Bottom => b with { Y = Math.Max(0, anchor.Bottom - b.Height) }, AlignmentCommand.Middle => b with { Y = Math.Max(0, anchor.Y + (anchor.Height - b.Height) / 2) },
                AlignmentCommand.SameWidth => b with { Width = anchor.Width }, AlignmentCommand.SameHeight => b with { Height = anchor.Height }, _ => b
            };
        }
        if (command is AlignmentCommand.DistributeHorizontal or AlignmentCommand.DistributeVertical)
        {
            bool horizontal = command == AlignmentCommand.DistributeHorizontal;
            double start = horizontal ? sorted[0].Bounds.X : sorted[0].Bounds.Y, end = horizontal ? sorted[^1].Bounds.Right : sorted[^1].Bounds.Bottom;
            double sum = sorted.Sum(i => horizontal ? i.Bounds.Width : i.Bounds.Height), gap = (end - start - sum) / (sorted.Length - 1), cursor = start;
            foreach (var item in sorted) { changes[item.Id] = horizontal ? item.Bounds with { X = Math.Max(0, cursor) } : item.Bounds with { Y = Math.Max(0, cursor) }; cursor += (horizontal ? item.Bounds.Width : item.Bounds.Height) + gap; }
        }
        UpdateSelection(command.ToString(), i => i with { Bounds = changes[i.Id] });
    }
    public void Reorder(bool front)
    {
        var movable = Selection.Where(i => !i.Locked).Select(i => i.Id).ToHashSet(StringComparer.Ordinal); if (movable.Count == 0) return;
        var bands = DocumentTree.AllBands(Document).Where(b => b.Items.Any(i => movable.Contains(i.Id))).ToArray();
        var before = bands.ToDictionary(b => b.Id, b => b.Items.ToArray());
        var after = bands.ToDictionary(b => b.Id, b => (front ? b.Items.Where(i => !movable.Contains(i.Id)).Concat(b.Items.Where(i => movable.Contains(i.Id))) : b.Items.Where(i => movable.Contains(i.Id)).Concat(b.Items.Where(i => !movable.Contains(i.Id)))).ToArray());
        if (bands.All(b => before[b.Id].SequenceEqual(after[b.Id]))) return;
        Execute(front ? "Bring to front" : "Send to back", () => { foreach (var b in bands) { b.Items.Clear(); b.Items.AddRange(after[b.Id]); } }, () => { foreach (var b in bands) { b.Items.Clear(); b.Items.AddRange(before[b.Id]); } });
    }
    public void SetSelectionLocked(bool locked)
    {
        var before = Selection.Where(i => i.Locked != locked).ToArray(); if (before.Length == 0) return;
        var after = before.Select(i => i with { Locked = locked }).ToArray();
        Execute(locked ? "Lock objects" : "Unlock objects", () => ReplaceItems(after), () => ReplaceItems(before));
    }
    public void UpdateBand(Band band)
    {
        var old = SelectedBand ?? throw new InvalidOperationException("No band selected.");
        if (band.Id != old.Id) throw new ArgumentException("Band edits must retain the band identifier.", nameof(band));
        var updated = MapDocument(Document, b => b.Id == old.Id ? band : b);
        ValidateReferences(updated); ReplaceDocument("Edit band", updated);
    }
    public void AddBand(BandKind kind)
    {
        var page = Page; var band = new Band { Name = kind + (page.Bands.Count + 1).ToString(), Kind = kind, Height = 36, DataSource = kind == BandKind.Data ? Document.Data.FirstOrDefault()?.Name : null };
        Execute("Insert band", () => page.Bands.Add(band), () => page.Bands.Remove(band), []); BandId = band.Id; SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    public void DeleteBand()
    {
        var band = SelectedBand; if (band is null) return;
        var page = Page; int index = page.Bands.IndexOf(band);
        if (index < 0) throw new NotSupportedException("Remove nested bands through the native document editor.");
        Execute("Delete band", () => page.Bands.Remove(band), () => page.Bands.Insert(index, band), []); SelectDefaultBand(); Notify();
    }
    public void UpdatePage(ReportPage page)
    {
        int index = PageIndex; var old = Page;
        var updated = Document with { Pages = Document.Pages.Select((p, n) => n == index ? page : p).ToList() };
        if (page.Name != old.Name) updated = MapDocument(updated, b => b with
        { Items = b.Items.Select(i => i is SubreportItem s && s.PageName.Equals(old.Name, StringComparison.OrdinalIgnoreCase) ? s with { PageName = page.Name } : i).ToList() });
        ValidateReferences(updated); ReplaceDocument("Page setup", updated);
    }
    public void AddPage()
    {
        string name = UniquePageName("Page");
        var page = new ReportPage { Name = name, Bands = [new() { Kind = BandKind.ReportTitle, Name = "ReportTitle", Height = 60 }] };
        Execute("Add page", () => Document.Pages.Add(page), () => Document.Pages.Remove(page), []); SetPage(Document.Pages.Count - 1);
    }
    public void ReplaceData(DataTable table)
    {
        int index = Document.Data.FindIndex(t => t.Name.Equals(table.Name, StringComparison.OrdinalIgnoreCase)); DataTable? old = index >= 0 ? Document.Data[index] : null;
        Execute("Import data", () => { if (index >= 0) Document.Data[index] = table; else Document.Data.Add(table); }, () => { if (index >= 0) Document.Data[index] = old!; else Document.Data.Remove(table); });
    }
    public void UpdateParameters(List<ReportParameter> parameters)
    {
        var old = Document.Parameters.ToArray();
        Execute("Edit parameters", () => { Document.Parameters.Clear(); Document.Parameters.AddRange(parameters); }, () => { Document.Parameters.Clear(); Document.Parameters.AddRange(old); });
    }
}
