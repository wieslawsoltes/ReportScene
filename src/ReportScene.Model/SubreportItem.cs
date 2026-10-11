namespace ReportScene.Model;

/// <summary>A parameter expression is evaluated in the caller's row scope, once per invocation.</summary>
public sealed record SubreportParameter(string Name, string Expression);

/// <summary>
/// References a page in this document. By default its bands flow independently through the
/// parent's printable columns. PrintOnParent materializes the objects inside the containing band.
/// Referenced pages are templates, not additional top-level printed pages.
/// </summary>
public sealed record SubreportItem : ReportItem
{
    public SubreportItem() { Bounds = new(0, 0, 180, 24); CanGrow = true; CanShrink = true; }
    public string PageName { get; init; } = "";
    public bool PrintOnParent { get; init; }
    public bool ShiftFollowing { get; init; } = true;
    public List<SubreportParameter> ParameterBindings { get; init; } = [];
}

/// <summary>Pure reference-graph operations, reusable by headless hosts and designers.</summary>
public static class SubreportGraph
{
    public static IReadOnlyList<ReportPage> PrintablePages(ReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var targets = References(document).Select(p => p.Item.PageName).Where(n => !string.IsNullOrWhiteSpace(n)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return document.Pages.Where(p => !targets.Contains(p.Name)).ToArray();
    }
    public static IReadOnlyList<Diagnostic> Validate(ReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors = new List<Diagnostic>();
        void Error(string message, string? id = null) => errors.Add(new("RS1500", message, ObjectId: id));
        var pages = new Dictionary<string, ReportPage>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in document.Pages)
            if (string.IsNullOrWhiteSpace(page.Name) || !pages.TryAdd(page.Name, page)) Error("Subreport page names must be nonempty and unique.");
        if (errors.Count > 0) return errors;
        var references = References(document).ToArray();
        foreach (var (page, band, item) in references)
        {
            if (string.IsNullOrWhiteSpace(item.PageName) || !pages.TryGetValue(item.PageName, out var target))
                Error($"Subreport '{item.Name}' references missing page '{item.PageName}'.", item.Id);
            else if (target.Columns != 1) Error($"Subreport page '{target.Name}' must use one template column. The parent page controls continuation columns.", item.Id);
            if (!item.PrintOnParent && band.Kind is BandKind.PageHeader or BandKind.PageFooter or BandKind.ColumnHeader or BandKind.ColumnFooter or BandKind.Overlay)
                Error("A subreport in a repeated page/column band must use PrintOnParent.", item.Id);
            if (item.ParameterBindings is null) { Error("Subreport parameter bindings cannot be null.", item.Id); continue; }
            if (item.ParameterBindings.Count > 256) Error("A subreport may bind at most 256 parameters.", item.Id);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in item.ParameterBindings)
            {
                if (parameter is null || string.IsNullOrWhiteSpace(parameter.Name) || parameter.Name.Length > 256 || !names.Add(parameter.Name) ||
                    string.IsNullOrWhiteSpace(parameter.Expression) || parameter.Expression.Length > 8192)
                    Error("Subreport parameter names must be nonempty and unique, with valid bounded names and expressions.", item.Id);
            }
        }
        var edges = references.Where(r => !string.IsNullOrWhiteSpace(r.Item.PageName) && pages.ContainsKey(r.Item.PageName)).GroupBy(r => r.Page.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Item.PageName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var depths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int Depth(string name)
        {
            if (depths.TryGetValue(name, out int cached)) return cached;
            if (!visiting.Add(name)) { Error($"Subreport reference cycle reaches page '{name}'."); return 0; }
            if (visiting.Count > 33) { Error("Subreport reference nesting exceeds 32 pages."); visiting.Remove(name); return 33; }
            int depth = 0;
            if (edges.TryGetValue(name, out var children)) foreach (var child in children) depth = Math.Max(depth, 1 + Depth(child));
            visiting.Remove(name); depths[name] = depth;
            return depth;
        }
        // Cache longest suffix lengths, not just 'visited'. A shared target may be reached
        // through a much deeper path than the first traversal of the graph.
        if (pages.Keys.Any(name => Depth(name) > 32)) Error("Subreport reference nesting exceeds 32 pages.");
        if (PrintablePages(document).Count == 0) Error("A report must have at least one page that is not a subreport target.");
        return errors;
    }
    public static IEnumerable<(ReportPage Page, Band Band, SubreportItem Item)> References(ReportDocument document)
    {
        foreach (var page in document.Pages)
        {
            var pending = new Stack<Band>(page.Bands.AsEnumerable().Reverse()); var seen = new HashSet<Band>(ReferenceEqualityComparer.Instance);
            while (pending.Count > 0)
            {
                var band = pending.Pop(); if (!seen.Add(band)) continue;
                foreach (var item in band.Items.OfType<SubreportItem>()) yield return (page, band, item);
                if (band.Footer is { } footer) pending.Push(footer);
                foreach (var child in band.Children.AsEnumerable().Reverse()) pending.Push(child);
                foreach (var group in band.Groups.AsEnumerable().Reverse()) { pending.Push(group.Footer); pending.Push(group.Header); }
                if (band.Header is { } header) pending.Push(header);
            }
        }
    }
}
