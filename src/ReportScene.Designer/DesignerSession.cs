using ReportScene.Model;
using ReportScene.Serialization;

namespace ReportScene.Designer;

public enum AlignmentCommand { Left, Center, Right, Top, Middle, Bottom, SameWidth, SameHeight, DistributeHorizontal, DistributeVertical }

/// <summary>Delta-based history keeps data tables out of per-pointer-move snapshots. One drag creates one undo entry.</summary>
public sealed class DesignerSession
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
    public void Load(ReportDocument document)
    {
        CancelDrag(); Document = document; PageIndex = 0; history.Clear(); position = 0; revision = savedRevision = ++sequence; selected.Clear(); SelectDefaultBand(); Notify();
    }
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
    private void Execute(string name, Action redo, Action undo, string[]? afterSelection = null)
    {
        if (dragOriginal is not null) throw new InvalidOperationException("Complete or cancel the active drag before another edit.");
        var before = selected.ToArray(); var after = afterSelection ?? before;
        redo();
        if (position < history.Count) history.RemoveRange(position, history.Count - position);
        var edit = new Edit(name, redo, undo, revision, ++sequence, before, after);
        history.Add(edit); position++; revision = edit.After;
        if (history.Count > 256) { history.RemoveAt(0); position--; }
        selected.Clear(); selected.UnionWith(after); Notify();
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
        foreach (var b in DocumentTree.AllBands(Document))
        { int index = b.Items.FindIndex(i => i.Id == id); if (index >= 0) { b.Items[index] = item; return; } }
        throw new InvalidOperationException($"Object '{id}' no longer exists.");
    }
    public void UpdateSelection(string description, Func<ReportItem, ReportItem> change)
    {
        var before = Selection.Where(i => !i.Locked).ToArray();
        if (before.Length == 0) return;
        var after = before.Select(change).ToArray();
        foreach (var item in after)
            if (!double.IsFinite(item.Bounds.X) || !double.IsFinite(item.Bounds.Y) || !double.IsFinite(item.Bounds.Width) || !double.IsFinite(item.Bounds.Height) || item.Bounds.X < 0 || item.Bounds.Y < 0 || item.Bounds.Width <= 0 || item.Bounds.Height <= 0)
                throw new ArgumentException("Invalid object bounds.");
        if (before.SequenceEqual(after)) return;
        Execute(description, () => { foreach (var i in after) Replace(i.Id, i); }, () => { foreach (var i in before) Replace(i.Id, i); });
    }
    public ReportItem Add(ReportItem item, string? bandId = null)
    {
        var band = DocumentTree.AllBands(Document).FirstOrDefault(b => b.Id == (bandId ?? BandId)) ?? throw new InvalidOperationException("Select a band before inserting an object.");
        var added = item with { Id = Guid.NewGuid().ToString("N"), Name = UniqueName(item.Name) };
        Execute("Insert " + added.Name, () => band.Items.Add(added), () => band.Items.RemoveAll(i => i.Id == added.Id), [added.Id]);
        return added;
    }
    private string UniqueName(string prefix)
    {
        var names = DocumentTree.AllItems(Document).Select(i => i.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(prefix)) return prefix;
        int n = 1; while (names.Contains(prefix + n)) n++; return prefix + n;
    }
    public void DeleteSelection()
    {
        var entries = DocumentTree.AllBands(Document).SelectMany(b => b.Items.Select((item, index) => (Band: b, Item: item, Index: index))).Where(e => selected.Contains(e.Item.Id) && !e.Item.Locked).ToArray();
        if (entries.Length == 0) return;
        Execute("Delete objects", () => { foreach (var e in entries) e.Band.Items.RemoveAll(i => i.Id == e.Item.Id); }, () => { foreach (var e in entries.OrderBy(e => e.Index)) e.Band.Items.Insert(e.Index, e.Item); }, []);
    }
    public string Copy() => ReportJson.SerializeItems(Selection);
    public void Paste(string json)
    {
        var band = SelectedBand ?? throw new InvalidOperationException("Select a band before pasting.");
        var source = ReportJson.DeserializeItems(json);
        if (source.Count == 0) return;
        var added = source.Select(i => i with { Id = Guid.NewGuid().ToString("N"), Name = UniqueName(i.Name + "Copy"), Bounds = i.Bounds.Offset(8, 8), Locked = false }).ToArray();
        Execute("Paste objects", () => band.Items.AddRange(added), () => band.Items.RemoveAll(i => added.Any(a => a.Id == i.Id)), added.Select(i => i.Id).ToArray());
    }
    public void Move(double dx, double dy)
    {
        var items = Selection.Where(i => !i.Locked).ToArray(); if (items.Length == 0) return;
        dx = Math.Max(dx, -items.Min(i => i.Bounds.X)); dy = Math.Max(dy, -items.Min(i => i.Bounds.Y));
        UpdateSelection("Move objects", i => i with { Bounds = i.Bounds.Offset(dx, dy) });
    }
    public void BeginDrag() { if (dragOriginal is not null) CancelDrag(); dragOriginal = Selection.Where(i => !i.Locked).ToDictionary(i => i.Id); }
    public void PreviewDrag(double dx, double dy, bool resize = false)
    {
        if (dragOriginal is null || dragOriginal.Count == 0) return;
        if (SnapToGrid && GridSize > 0) { dx = Math.Round(dx / GridSize) * GridSize; dy = Math.Round(dy / GridSize) * GridSize; }
        if (!resize) { dx = Math.Max(dx, -dragOriginal.Values.Min(i => i.Bounds.X)); dy = Math.Max(dy, -dragOriginal.Values.Min(i => i.Bounds.Y)); }
        foreach (var item in dragOriginal.Values)
        {
            var b = item.Bounds;
            Replace(item.Id, item with { Bounds = resize ? b with { Width = Math.Max(4, b.Width + dx), Height = Math.Max(4, b.Height + dy) } : b.Offset(dx, dy) });
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void CommitDrag()
    {
        if (dragOriginal is null) return;
        var before = dragOriginal.Values.ToArray(); var after = Selection.Where(i => dragOriginal.ContainsKey(i.Id)).ToArray(); dragOriginal = null;
        if (before.SequenceEqual(after)) return;
        Execute("Transform objects", () => { foreach (var i in after) Replace(i.Id, i); }, () => { foreach (var i in before) Replace(i.Id, i); });
    }
    public void CancelDrag()
    { if (dragOriginal is null) return; foreach (var i in dragOriginal.Values) Replace(i.Id, i); dragOriginal = null; Changed?.Invoke(this, EventArgs.Empty); }
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
            double start = horizontal ? sorted[0].Bounds.X : sorted[0].Bounds.Y;
            double end = horizontal ? sorted[^1].Bounds.Right : sorted[^1].Bounds.Bottom;
            double sum = sorted.Sum(i => horizontal ? i.Bounds.Width : i.Bounds.Height);
            double gap = (end - start - sum) / (sorted.Length - 1), cursor = start;
            foreach (var item in sorted) { changes[item.Id] = horizontal ? item.Bounds with { X = Math.Max(0, cursor) } : item.Bounds with { Y = Math.Max(0, cursor) }; cursor += (horizontal ? item.Bounds.Width : item.Bounds.Height) + gap; }
        }
        UpdateSelection(command.ToString(), i => i with { Bounds = changes[i.Id] });
    }
    public void Reorder(bool front)
    {
        var bands = DocumentTree.AllBands(Document).Where(b => b.Items.Any(i => selected.Contains(i.Id))).ToArray();
        var before = bands.ToDictionary(b => b.Id, b => b.Items.ToArray());
        var after = bands.ToDictionary(b => b.Id, b => (front ? b.Items.Where(i => !selected.Contains(i.Id)).Concat(b.Items.Where(i => selected.Contains(i.Id))) : b.Items.Where(i => selected.Contains(i.Id)).Concat(b.Items.Where(i => !selected.Contains(i.Id)))).ToArray());
        Execute(front ? "Bring to front" : "Send to back", () => { foreach (var b in bands) { b.Items.Clear(); b.Items.AddRange(after[b.Id]); } }, () => { foreach (var b in bands) { b.Items.Clear(); b.Items.AddRange(before[b.Id]); } });
    }
    public void UpdateBand(Band band)
    {
        var old = SelectedBand ?? throw new InvalidOperationException("No band selected.");
        void ReplaceBand(Band from, Band to)
        {
            foreach (var p in Document.Pages) { int index = p.Bands.FindIndex(b => b.Id == from.Id); if (index >= 0) { p.Bands[index] = to; return; } }
            throw new NotSupportedException("Editing nested band settings is not available in this command; edit the native report document.");
        }
        Execute("Edit band", () => ReplaceBand(old, band), () => ReplaceBand(band, old));
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
        Execute("Page setup", () => Document.Pages[index] = page, () => Document.Pages[index] = old);
    }
    public void AddPage()
    {
        var page = new ReportPage { Name = "Page" + (Document.Pages.Count + 1), Bands = [new() { Kind = BandKind.ReportTitle, Name = "ReportTitle", Height = 60 }] };
        Execute("Add page", () => Document.Pages.Add(page), () => Document.Pages.Remove(page), []); SetPage(Document.Pages.Count - 1);
    }
    public void ReplaceData(DataTable table)
    {
        int index = Document.Data.FindIndex(t => t.Name.Equals(table.Name, StringComparison.OrdinalIgnoreCase));
        DataTable? old = index >= 0 ? Document.Data[index] : null;
        Execute("Import data", () => { if (index >= 0) Document.Data[index] = table; else Document.Data.Add(table); }, () => { if (index >= 0) Document.Data[index] = old!; else Document.Data.Remove(table); });
    }
    public void UpdateParameters(List<ReportParameter> parameters)
    {
        var old = Document.Parameters.ToArray();
        Execute("Edit parameters", () => { Document.Parameters.Clear(); Document.Parameters.AddRange(parameters); }, () => { Document.Parameters.Clear(); Document.Parameters.AddRange(old); });
    }
}
