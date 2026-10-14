using System.Collections;
using ReportScene.Model;

namespace ReportScene.Export;

public sealed partial class ReportExporter
{
    /// <summary>
    /// Create the HTML document supplied to a host's print service. Selection uses
    /// displayed ordinals and preserves printed page numbers. Printer hardware,
    /// copies, paper trays and duplex settings remain the host's responsibility.
    /// </summary>
    public ExportFile CreatePrintDocument(PreparedReport report, ReportPageSelection selection,
        bool reverse = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report); ArgumentNullException.ThrowIfNull(selection);
        cancellationToken.ThrowIfCancellationRequested();
        var selected = selection.Apply(report, cancellationToken);
        // The selected list is already an immutable snapshot. Reverse uses a
        // read-only view, not a second array of page references or a report clone.
        IReadOnlyList<PreparedPage> pages = reverse ? new ReversePages(selected.Pages) : selected.Pages;
        return ExportPages(selected, ExportFormat.Html, new ExportOptions(), null, pages, cancellationToken);
    }
    private sealed class ReversePages(IReadOnlyList<PreparedPage> pages) : IReadOnlyList<PreparedPage>
    {
        public int Count => pages.Count;
        public PreparedPage this[int index] => (uint)index < (uint)Count
            ? pages[Count - 1 - index] : throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<PreparedPage> GetEnumerator()
        { for (int n = Count - 1; n >= 0; n--) yield return pages[n]; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
