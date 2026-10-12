using ReportScene.Model;

namespace ReportScene.Designer;

public sealed partial class DesignerSession
{
    /// <summary>Select the current page itself without changing content, history or its default band.</summary>
    public void SelectPage()
    {
        CancelDrag(); selected.Clear(); BandId = null; SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>Validate page geometry and its name before a reversible page-property edit.</summary>
    public void UpdatePageProperties(ReportPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!ReferenceEquals(page.Bands, Page.Bands))
            throw new ArgumentException("Page-property edits must retain the band collection. Use structural editing APIs to change bands.", nameof(page));
        if (string.IsNullOrWhiteSpace(page.Name) || Document.Pages.Where((_, i) => i != PageIndex)
            .Any(p => p.Name.Equals(page.Name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Page names must be nonempty and unique (ignoring case).", nameof(page));
        var validation = DocumentValidator.Validate(new ReportDocument { Pages = [page with { Bands = [] }] });
        if (validation.Count > 0) throw new ArgumentException(string.Join("\n", validation.Select(d => d.Message)), nameof(page));
        if (page == Page) return;
        // UpdatePage already propagates target-page renames through subreport references.
        UpdatePage(page);
    }
}
