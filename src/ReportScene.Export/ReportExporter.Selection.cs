using ReportScene.Model;

namespace ReportScene.Export;

public sealed partial class ReportExporter
{
    /// <summary>
    /// Export displayed ordinal pages without treating physical page numbers as indices.
    /// The older Export API keeps its physical FirstPage/LastPage filter unchanged.
    /// This API rejects combining those filters with an ordinal selection. CSV exports
    /// source tables, so only All is meaningful for that format.
    /// </summary>
    public ExportFile ExportSelection(PreparedReport report, ExportFormat format, ReportPageSelection selection,
        ExportOptions? options = null, ReportDocument? document = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report); ArgumentNullException.ThrowIfNull(selection);
        options ??= new(); ValidateOptions(options); cancellationToken.ThrowIfCancellationRequested();
        if (options.FirstPage != 1 || options.LastPage != int.MaxValue)
            throw new ArgumentException("Do not combine ordinal selection with physical FirstPage/LastPage filters.", nameof(options));
        if (format == ExportFormat.Csv)
        {
            if (selection.Mode != PageSelectionMode.All)
                throw new ArgumentException("CSV exports bound tables, not selected report pages.", nameof(selection));
            if (selection.SourcePageCount != report.Pages.Count)
                throw new ArgumentException("The prepared page count changed; select pages again.", nameof(selection));
            // The selection's count contract still applies, but a data-only export
            // must not copy or enumerate the unrelated prepared drawing pages.
            return ExportPages(report, format, options, document, report.Pages, cancellationToken);
        }
        var selected = selection.Apply(report, cancellationToken);
        return ExportPages(selected, format, options, document, selected.Pages, cancellationToken);
    }
}
