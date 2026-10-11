namespace ReportScene.Model;

/// <summary>An immutable ordered interval index for unbounded band layouts and line ranges.</summary>
public sealed class VerticalIntervalIndex
{
    private readonly record struct Entry(int Index, double Top, double Bottom);
    private sealed record Node(double Top, double Bottom, int Start, int Count, Node? Left = null, Node? Right = null);
    private readonly Entry[] entries;
    private readonly Node? root;
    public int Count => entries.Length;
    /// <summary>Returned indices refer to the original input order, not the spatial order.</summary>
    public VerticalIntervalIndex(IEnumerable<(double Top, double Bottom)> intervals, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intervals); cancellationToken.ThrowIfCancellationRequested();
        var values = new List<Entry>();
        foreach (var (top, bottom) in intervals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!double.IsFinite(top) || !double.IsFinite(bottom) || bottom < top) throw new ArgumentException("Intervals require finite, nondecreasing endpoints.", nameof(intervals));
            values.Add(new(values.Count, top, bottom));
        }
        entries = values.ToArray();
        Array.Sort(entries, (a, b) => { int order = a.Top.CompareTo(b.Top); return order != 0 ? order : a.Index.CompareTo(b.Index); });
        Node Build(int start, int count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (count <= 8)
            {
                double bottom = entries[start].Bottom;
                for (int n = start + 1; n < start + count; n++) bottom = Math.Max(bottom, entries[n].Bottom);
                return new(entries[start].Top, bottom, start, count);
            }
            int half = count / 2; var left = Build(start, half); var right = Build(start + half, count - half);
            return new(left.Top, Math.Max(left.Bottom, right.Bottom), start, count, left, right);
        }
        root = entries.Length == 0 ? null : Build(0, entries.Length);
    }
    /// <summary>Queries strict interval overlap. Empty intervals never produce a result.</summary>
    public IReadOnlyList<int> Query(double top, double bottom, out int candidatesExamined, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(top) || !double.IsFinite(bottom)) throw new ArgumentOutOfRangeException(nameof(top));
        cancellationToken.ThrowIfCancellationRequested(); int examined = 0; var found = new List<int>();
        void Visit(Node node)
        {
            cancellationToken.ThrowIfCancellationRequested(); if (node.Top >= bottom || node.Bottom <= top) return;
            if (node.Left is { } left) { Visit(left); Visit(node.Right!); return; }
            for (int n = node.Start; n < node.Start + node.Count; n++)
            {
                var entry = entries[n]; examined++;
                if (entry.Top < bottom && entry.Bottom > top && entry.Bottom > entry.Top) found.Add(entry.Index);
            }
        }
        if (bottom > top && root is not null) Visit(root);
        found.Sort(); candidatesExamined = examined; return found.AsReadOnly();
    }
}
