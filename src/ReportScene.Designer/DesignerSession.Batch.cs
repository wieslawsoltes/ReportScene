using ReportScene.Model;

namespace ReportScene.Designer;

public sealed partial class DesignerSession
{
    /// <summary>Items examined by the last batch replacement, excluding selection gathering/validation. Diagnostic work count, not elapsed time.</summary>
    public int LastReplacementScanCount { get; private set; }
    private bool ReplaceItems(IReadOnlyList<ReportItem> items)
    {
        LastReplacementScanCount = 0;
        if (items.Count == 0) return false;
        var replacements = new Dictionary<string, ReportItem>(items.Count, StringComparer.Ordinal);
        foreach (var item in items) { DesignerGeometry.ValidateBounds(item.Bounds); replacements.Add(item.Id, item); }
        var destinations = new List<(List<ReportItem> Items, int Index, ReportItem Value)>(items.Count);
        int found = 0;
        foreach (var band in DocumentTree.AllBands(Document))
            for (int n = 0; n < band.Items.Count; n++)
            {
                LastReplacementScanCount++;
                if (!replacements.TryGetValue(band.Items[n].Id, out var replacement)) continue;
                found++;
                if (band.Items[n] != replacement) destinations.Add((band.Items, n, replacement));
            }
        // Resolve every target before changing any item, so a failed edit cannot partly apply.
        if (found != items.Count) throw new InvalidOperationException("One or more edited objects no longer exist or have duplicate identifiers.");
        foreach (var destination in destinations) destination.Items[destination.Index] = destination.Value;
        return destinations.Count > 0;
    }
    private static string ReserveName(string prefix, HashSet<string> names)
    {
        if (string.IsNullOrWhiteSpace(prefix)) throw new ArgumentException("An object name cannot be empty.");
        string name = prefix; int suffix = 1;
        while (!names.Add(name)) name = prefix + suffix++;
        return name;
    }
    private void ValidateRenames(ReportItem[] before, ReportItem[] after)
    {
        var renamed = new HashSet<string>(StringComparer.Ordinal);
        for (int n = 0; n < before.Length; n++) if (before[n].Name != after[n].Name) renamed.Add(before[n].Id);
        if (renamed.Count == 0) return;
        var names = DocumentTree.AllItems(Document).Where(i => !renamed.Contains(i.Id)).Select(i => i.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in after.Where(i => renamed.Contains(i.Id)))
            if (string.IsNullOrWhiteSpace(item.Name) || !names.Add(item.Name)) throw new ArgumentException("Object names must be nonempty and unique (ignoring case).");
    }
    private (double X, double Y) SnapDeltas(double dx, double dy)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) throw new ArgumentException("Transform deltas must be finite.");
        if (SnapToGrid)
        {
            if (!double.IsFinite(GridSize) || GridSize < 0) throw new ArgumentException("Grid size must be finite and nonnegative.");
            if (GridSize > 0) { dx = Math.Round(dx / GridSize) * GridSize; dy = Math.Round(dy / GridSize) * GridSize; }
        }
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) throw new ArgumentException("The snapped transform is outside the supported range.");
        return (dx, dy);
    }
}
