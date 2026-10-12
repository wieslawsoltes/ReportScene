using System.Collections;

namespace ReportScene.Designer;

/// <summary>
/// Immutable parent-first filtered metadata and an ordinal key index. Construction is
/// linear in the projected entry count; repeated selection membership/lookup does not
/// scan the projection. Does not retain report objects, data rows or UI controls.
/// </summary>
public sealed class DesignerOutlineProjection : IReadOnlyList<DesignerOutlineEntry>
{
    private readonly IReadOnlyList<DesignerOutlineEntry> entries;
    private readonly Dictionary<string, int> indices;
    public string Query { get; }
    public int Count => entries.Count;
    public DesignerOutlineEntry this[int index] => entries[index];
    public DesignerOutlineProjection(DesignerOutline source, string query = "", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(query);
        entries = source.Filter(query, cancellationToken); Query = query.Trim();
        indices = new(entries.Count, StringComparer.Ordinal);
        for (int n = 0; n < entries.Count; n++)
        {
            if ((n & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            indices.Add(entries[n].Key, n);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }
    public bool ContainsKey(string? key) => key is not null && indices.ContainsKey(key);
    public DesignerOutlineEntry? Find(string? key) => key is not null && indices.TryGetValue(key, out int index) ? entries[index] : null;
    public IEnumerator<DesignerOutlineEntry> GetEnumerator() => entries.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
