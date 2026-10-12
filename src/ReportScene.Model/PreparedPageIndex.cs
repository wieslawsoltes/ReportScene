namespace ReportScene.Model;

/// <summary>
/// An immutable spatial snapshot for a prepared page. Queries retain painter order, intersect
/// object clips, and expose deterministic candidate counts for profiling. Rebuild after edits;
/// no global cache retains reports and no mutable input list is assumed to be immutable.
/// </summary>
public sealed class PreparedPageIndex
{
    private readonly record struct Entry(int Index, RectD Bounds);
    private sealed record Node(RectD Bounds, int Start, int Count, Node? Left = null, Node? Right = null);
    private readonly PreparedItem[] items;
    private readonly Entry[] entries;
    private readonly Node? root;
    public PreparedPage Page { get; }
    public int Count => items.Length;
    public PreparedPageIndex(PreparedPage page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!double.IsFinite(page.Width) || !double.IsFinite(page.Height) || page.Width <= 0 || page.Height <= 0) throw new ArgumentException("Page dimensions must be finite and positive.", nameof(page));
        cancellationToken.ThrowIfCancellationRequested();
        items = page.Items.ToArray(); Page = page with { Items = Array.AsReadOnly(items) };
        var visible = new List<Entry>(items.Length);
        for (int n = 0; n < items.Length; n++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (PreparedPaintBounds.VisibleBounds(page, items[n]) is { } bounds) visible.Add(new(n, bounds));
        }
        entries = visible.ToArray();
        Array.Sort(entries, (a, b) => { int order = a.Bounds.Y.CompareTo(b.Bounds.Y); return order != 0 ? order : a.Index.CompareTo(b.Index); });
        Node Build(int start, int count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (count <= 8)
            {
                var bounds = entries[start].Bounds;
                for (int n = start + 1; n < start + count; n++) bounds = Union(bounds, entries[n].Bounds);
                return new(bounds, start, count);
            }
            int leftCount = count / 2; var left = Build(start, leftCount); var right = Build(start + leftCount, count - leftCount);
            return new(Union(left.Bounds, right.Bounds), start, count, left, right);
        }
        root = entries.Length == 0 ? null : Build(0, entries.Length);
    }
    private static RectD Union(RectD a, RectD b)
    { double x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y); return new(x, y, Math.Max(a.Right, b.Right) - x, Math.Max(a.Bottom, b.Bottom) - y); }
    private static bool Overlaps(RectD a, RectD b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;
    public IReadOnlyList<PreparedItem> Query(RectD viewport) => Query(viewport, out _);
    public IReadOnlyList<PreparedItem> Query(RectD viewport, out int candidatesExamined, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(viewport.X) || !double.IsFinite(viewport.Y) || !double.IsFinite(viewport.Right) || !double.IsFinite(viewport.Bottom)) throw new ArgumentOutOfRangeException(nameof(viewport));
        cancellationToken.ThrowIfCancellationRequested(); candidatesExamined = 0;
        // A private recursive method avoids a per-call captured local-function
        // object. Empty/disjoint viewports return before allocating hit storage.
        if (root is null || viewport.Width <= 0 || viewport.Height <= 0 || !Overlaps(root.Bounds, viewport)) return Array.Empty<PreparedItem>();
        var found = new List<int>(8);
        Visit(root, viewport, found, ref candidatesExamined, cancellationToken);
        if (found.Count == 0) return Array.Empty<PreparedItem>();
        found.Sort(); var result = new PreparedItem[found.Count];
        for (int n = 0; n < result.Length; n++) result[n] = items[found[n]];
        return Array.AsReadOnly(result);
    }
    private void Visit(Node node, RectD viewport, List<int> found, ref int examined, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); if (!Overlaps(node.Bounds, viewport)) return;
        if (node.Left is { } left)
        { Visit(left, viewport, found, ref examined, cancellationToken); Visit(node.Right!, viewport, found, ref examined, cancellationToken); return; }
        for (int n = node.Start; n < node.Start + node.Count; n++)
        { examined++; if (Overlaps(entries[n].Bounds, viewport)) found.Add(entries[n].Index); }
    }
}
