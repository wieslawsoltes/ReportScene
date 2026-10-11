using ReportScene.Model;

namespace ReportScene.Designer;

public enum OutlineKind { Group, Page, Band, Object, Table, Field, Parameter, SystemVariable }
public sealed record DesignerOutlineEntry(string Key, string? ParentKey, string Label, OutlineKind Kind,
    int PageIndex = -1, string? BandId = null, string? ObjectId = null, string? Expression = null, string? Detail = null);

/// <summary>Immutable, parent-first outline with linear filtering. It never retains source rows or mutable report objects.</summary>
public sealed class DesignerOutline
{
    private readonly DesignerOutlineEntry[] entries;
    private readonly Dictionary<string, int> indices;
    private readonly int[] parents;
    private string? cachedQuery;
    private IReadOnlyList<DesignerOutlineEntry>? cachedResult;
    public IReadOnlyList<DesignerOutlineEntry> Entries { get; }
    public int QueryScanCount { get; private set; }
    public DesignerOutline(IEnumerable<DesignerOutlineEntry> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        entries = source.Take(262145).ToArray();
        if (entries.Length > 262144) throw new ArgumentException("An outline may contain at most 262144 entries.", nameof(source));
        Entries = Array.AsReadOnly(entries); indices = new(entries.Length, StringComparer.Ordinal); parents = new int[entries.Length];
        for (int n = 0; n < entries.Length; n++)
        {
            var e = entries[n];
            if (e is null || string.IsNullOrEmpty(e.Key) || e.Label is null) throw new ArgumentException("Outline entries need a key and label.");
            parents[n] = -1;
            if (e.ParentKey is not null)
            {
                if (!indices.TryGetValue(e.ParentKey, out int p)) throw new ArgumentException("Outline parents must precede their children.");
                parents[n] = p;
            }
            if (!indices.TryAdd(e.Key, n)) throw new ArgumentException("Outline keys must be unique.");
        }
    }
    public DesignerOutlineEntry? Find(string key) => indices.TryGetValue(key, out int n) ? entries[n] : null;
    public bool HasSameEntries(DesignerOutline other) => entries.AsSpan().SequenceEqual(other.entries);
    public IReadOnlyList<DesignerOutlineEntry> Filter(string query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query); cancellationToken.ThrowIfCancellationRequested();
        if (query.Length > 1024) throw new ArgumentException("Outline search is limited to 1024 characters.", nameof(query));
        query = query.Trim();
        if (query.Length == 0) return Entries;
        if (query == cachedQuery && cachedResult is not null) return cachedResult;
        var included = new bool[entries.Length];
        for (int n = 0; n < entries.Length; n++)
        {
            if ((n & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            // A matched table/group exposes its subtree, while a matched leaf keeps its ancestors.
            included[n] = (parents[n] >= 0 && included[parents[n]]) || entries[n].Label.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (entries[n].Expression?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
        }
        for (int n = entries.Length - 1; n >= 0; n--)
        { if ((n & 255) == 0) cancellationToken.ThrowIfCancellationRequested(); if (included[n] && parents[n] >= 0) included[parents[n]] = true; }
        var result = new List<DesignerOutlineEntry>();
        for (int n = 0; n < entries.Length; n++) if (included[n]) result.Add(entries[n]);
        cancellationToken.ThrowIfCancellationRequested(); QueryScanCount++; cachedQuery = query; cachedResult = result.AsReadOnly(); return cachedResult;
    }
    public static DesignerOutline Report(ReportDocument document)
    {
        var result = new List<DesignerOutlineEntry>(); var visited = new HashSet<string>(StringComparer.Ordinal);
        void BandNode(Band band, string parent, int page, int depth)
        {
            if (depth > 128 || !visited.Add(band.Id)) throw new ArgumentException("The report band graph is cyclic, shared or too deep.");
            string key = "band:" + band.Id;
            result.Add(new(key, parent, band.Name, OutlineKind.Band, page, band.Id, Detail: band.Kind.ToString()));
            foreach (var item in band.Items) result.Add(new("item:" + item.Id, key, item.Name, OutlineKind.Object, page, band.Id, item.Id, Detail: item.GetType().Name));
            if (band.Header is not null) BandNode(band.Header, key, page, depth + 1);
            foreach (var group in band.Groups) BandNode(group.Header, key, page, depth + 1);
            foreach (var child in band.Children) BandNode(child, key, page, depth + 1);
            foreach (var group in band.Groups.AsEnumerable().Reverse()) BandNode(group.Footer, key, page, depth + 1);
            if (band.Footer is not null) BandNode(band.Footer, key, page, depth + 1);
        }
        for (int p = 0; p < document.Pages.Count; p++)
        {
            string key = "page:" + p; result.Add(new(key, null, document.Pages[p].Name, OutlineKind.Page, p));
            foreach (var band in document.Pages[p].Bands) BandNode(band, key, p, 0);
        }
        return new(result);
    }
    public static DesignerOutline Data(ReportDocument document)
    {
        var result = new List<DesignerOutlineEntry> { new("data", null, "Data Sources", OutlineKind.Group) };
        for (int t = 0; t < document.Data.Count; t++)
        {
            var table = document.Data[t]; string key = "table:" + t;
            result.Add(new(key, "data", table.Name + " (" + table.Rows.Count + " rows)", OutlineKind.Table, Detail: table.Name));
            for (int c = 0; c < table.Columns.Count; c++)
                result.Add(new(key + "/field:" + c, key, table.Columns[c], OutlineKind.Field, Expression: "[" + table.Name + "." + table.Columns[c] + "]"));
        }
        result.Add(new("system", null, "System Variables", OutlineKind.Group));
        foreach (var name in new[] { "PageN", "TotalPages", "RowNumber", "ColumnNumber", "Date", "Now" }) result.Add(new("system:" + name, "system", name, OutlineKind.SystemVariable, Expression: "[" + name + "]"));
        result.Add(new("parameters", null, "Parameters", OutlineKind.Group));
        foreach (var parameter in document.Parameters) result.Add(new("parameter:" + parameter.Name, "parameters", parameter.Name, OutlineKind.Parameter, Expression: "[Parameters." + parameter.Name + "]"));
        return new(result);
    }
}
