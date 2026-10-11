using System.Runtime.CompilerServices;
using ReportScene.Model;

namespace ReportScene.Engine;

internal sealed partial class PreparationRun
{
    private sealed record LayoutIndexes(VerticalIntervalIndex All, VerticalIntervalIndex Text, PreparedItem[] TextItems);
    private readonly ConditionalWeakTable<Layout, LayoutIndexes> layoutIndexes = new();
    private LayoutIndexes Index(Layout layout) => layoutIndexes.GetValue(layout, value =>
    {
        var text = value.Items.Where(i => i.Source is TextItem).ToArray();
        return new(new(value.Items.Select(i => (i.Bounds.Y, i.Bounds.Bottom)), cancellation),
            new(text.Select(i => (i.Bounds.Y, i.Bounds.Bottom)), cancellation), text);
    });
    private IEnumerable<PreparedItem> SliceCandidates(Layout layout, double consumed, double amount)
    {
        // Small ordinary bands do not pay for an index. Raw bounds, not final visual clips,
        // are indexed: slicing and atomic line decisions retain their original semantics.
        if (layout.Items.Count < 64) return layout.Items;
        var index = Index(layout);
        return index.All.Query(consumed, consumed + amount, out _, cancellation).Select(n => layout.Items[n]);
    }
    private IEnumerable<PreparedItem> CutCandidates(Layout layout, double cut)
    {
        if (layout.Items.Count < 64) return layout.Items;
        var index = Index(layout);
        return index.Text.Query(cut - .001, cut + .001, out _, cancellation).Select(n => index.TextItems[n]);
    }
    private static double RowBoundary(FlowSection flow, double consumed, double cut)
    {
        double boundary = flow.Top >= consumed - .001 ? flow.Top : consumed;
        int low = 0, high = flow.RowEnds.Length;
        // Upper bound in the engine-generated, sorted row-end sequence. The previous
        // implementation scanned from row zero on every physical page.
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (flow.RowEnds[mid] <= cut + .001) low = mid + 1; else high = mid;
        }
        if (low > 0 && flow.RowEnds[low - 1] > consumed + .001) boundary = flow.RowEnds[low - 1];
        return boundary;
    }
}
