using ReportScene.Model;

namespace ReportScene.Designer;

public sealed partial class DesignerSession
{
    /// <summary>Adds both the reference object and its new page as one reversible transaction.</summary>
    public SubreportItem AddSubreport(ReportPage target, RectD bounds, bool printOnParent = false, string? bandId = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        var owner = DocumentTree.AllBands(Document).FirstOrDefault(b => b.Id == (bandId ?? BandId)) ?? throw new InvalidOperationException("Select a band before inserting a subreport.");
        if (Document.Pages.Any(p => p.Name.Equals(target.Name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("The new subreport page name already exists.", nameof(target));
        var item = new SubreportItem { Name = UniqueName("Subreport"), PageName = target.Name, PrintOnParent = printOnParent, Bounds = bounds };
        var updated = MapDocument(Document, b => b.Id == owner.Id ? b with { Height = Math.Max(b.Height, bounds.Bottom), Items = [.. b.Items, item] } : b);
        updated.Pages.Add(target);
        var errors = DocumentValidator.Validate(updated);
        if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors.Select(e => e.Message)));
        ReplaceDocument("Insert subreport and page", updated, [item.Id]); return item;
    }
    public string UniquePageName(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        var names = Document.Pages.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int n = 1; while (names.Contains(prefix + n)) n++; return prefix + n;
    }
    /// <summary>Navigates without changing document history.</summary>
    public void OpenSubreportPage(string objectId)
    {
        var item = DocumentTree.AllItems(Document).OfType<SubreportItem>().FirstOrDefault(i => i.Id == objectId) ?? throw new ArgumentException("The selected subreport does not exist.", nameof(objectId));
        int index = Document.Pages.FindIndex(p => p.Name.Equals(item.PageName, StringComparison.OrdinalIgnoreCase));
        if (index < 0) throw new InvalidOperationException($"Target page '{item.PageName}' is missing.");
        SetPage(index);
    }
    private void ReplaceDocument(string name, ReportDocument updated, string[]? selection = null)
    {
        var old = Document;
        Execute(name, () => Document = updated, () => Document = old, selection);
    }
    private static void ValidateReferences(ReportDocument document)
    {
        var errors = SubreportGraph.Validate(document);
        if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors.Select(e => e.Message)));
    }
    // Copy report structure, never embedded row data. Structural transactions compose with the
    // existing item deltas because undo restores the corresponding structural revision first.
    private static ReportDocument MapDocument(ReportDocument document, Func<Band, Band> transform)
    {
        Band Map(Band b) => transform(b with
        {
            Items = b.Items.ToList(), Children = b.Children.Select(Map).ToList(),
            Header = b.Header is null ? null : Map(b.Header), Footer = b.Footer is null ? null : Map(b.Footer),
            Groups = b.Groups.Select(g => g with { Header = Map(g.Header), Footer = Map(g.Footer) }).ToList()
        });
        return document with { Pages = document.Pages.Select(p => p with { Bands = p.Bands.Select(Map).ToList() }).ToList() };
    }
}
