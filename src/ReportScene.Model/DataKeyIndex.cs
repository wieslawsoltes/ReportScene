namespace ReportScene.Model;

/// <summary>
/// Immutable column-key snapshot. Matches return original row ordinals in source order;
/// missing fields are distinct from explicit null. Rebuild after changing source keys.
/// </summary>
public sealed class DataKeyIndex
{
    private readonly (int Row, Value Key)[] entries;
    private readonly Dictionary<Value, IReadOnlyList<int>> lookup;
    private readonly ValueKind? homogeneousKind;
    private readonly bool mixed;
    public int SourceRowCount { get; }
    public int KeyedRowCount => entries.Length;
    public DataKeyIndex(IReadOnlyList<DataRow> rows, string column, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows); ArgumentException.ThrowIfNullOrWhiteSpace(column);
        cancellationToken.ThrowIfCancellationRequested(); SourceRowCount = rows.Count;
        var values = new List<(int, Value)>(); var buckets = new Dictionary<Value, List<int>>();
        for (int n = 0; n < rows.Count; n++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!rows[n].TryGetValue(column, out var value)) continue;
            values.Add((n, value));
            if (!value.IsNull)
            {
                if (homogeneousKind is null) homogeneousKind = value.Kind;
                else if (homogeneousKind != value.Kind) mixed = true;
            }
            if (!buckets.TryGetValue(value, out var bucket)) buckets.Add(value, bucket = []);
            bucket.Add(n);
        }
        entries = values.ToArray(); lookup = new();
        foreach (var pair in buckets)
        { cancellationToken.ThrowIfCancellationRequested(); lookup.Add(pair.Key, pair.Value.AsReadOnly()); }
    }
    /// <summary>
    /// True when indexed equality exactly matches Value.Compare without coercion or delayed
    /// comparison errors. Consumers interleaving filters with comparisons should otherwise
    /// retain their original scan to preserve exception/evaluation order.
    /// </summary>
    public bool CanUseLookup(Value key) => key.IsNull || !mixed && (homogeneousKind is null || homogeneousKind == key.Kind);
    public IReadOnlyList<int> Find(Value key, out int candidatesExamined, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (CanUseLookup(key))
        {
            var matches = lookup.GetValueOrDefault(key) ?? Array.Empty<int>(); candidatesExamined = matches.Count; return matches;
        }
        var found = new List<int>(); int examined = 0;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested(); examined++;
            if (Value.Compare(entry.Key, key) == 0) found.Add(entry.Row);
        }
        candidatesExamined = examined; return found.AsReadOnly();
    }
}
