using ReportScene.Expressions;
using ReportScene.Model;

namespace ReportScene.Engine;

internal sealed partial class PreparationRun
{
    private void Paginate(ReportPage template)
    {
        var pageHeaders = template.Bands.Where(b => b.Kind == BandKind.PageHeader).ToArray();
        var pageFooters = template.Bands.Where(b => b.Kind == BandKind.PageFooter).ToArray();
        var columnHeaders = template.Bands.Where(b => b.Kind == BandKind.ColumnHeader).ToArray();
        var columnFooters = template.Bands.Where(b => b.Kind == BandKind.ColumnFooter).ToArray();
        var overlays = template.Bands.Where(b => b.Kind == BandKind.Overlay).ToArray();
        using var content = template.Bands.Where(b => b.Kind is not (BandKind.PageHeader or BandKind.PageFooter or BandKind.ColumnHeader or BandKind.ColumnFooter or BandKind.Overlay))
            .SelectMany(b => Emit(b, root, [])).GetEnumerator();
        List<PreparedItem> output = [];
        int column = 0; double y = 0, top = 0, bottom = 0, pageTop = 0, pageBottom = 0;
        bool columnHasContent = false;
        EvaluationContext Context(EvaluationContext original) => original with { PageNumber = pages.Count, ColumnNumber = column + 1 };
        double X() => template.Margins.Left + column * (template.ColumnWidth + template.ColumnGap);
        void Place(Layout layout, double x, double at, double consumed = 0, double? sliceHeight = null)
        {
            var slice = new RectD(x, at, template.ColumnWidth, sliceHeight ?? layout.Height);
            foreach (var item in layout.Items)
            {
                cancellation.ThrowIfCancellationRequested();
                var bounds = item.Bounds.Offset(x, at - consumed);
                var clip = item.Clip?.Offset(x, at - consumed);
                if (sliceHeight is not null)
                {
                    if (bounds.Y >= slice.Bottom - .001 || bounds.Bottom <= slice.Y + .001 || bounds.X >= slice.Right || bounds.Right <= slice.X) continue;
                    clip = clip is { } existing ? Intersection(existing, slice) : slice;
                }
                if (clip is { } area && (area.Width <= .001 || area.Height <= .001)) continue;
                if (++itemCount > options.MaximumItems) throw Failure("RS2005", "Maximum prepared-item count exceeded.");
                output.Add(item with { Bounds = bounds, Clip = clip });
            }
        }
        void StartColumn(IReadOnlyList<Instance> repeated)
        {
            var c = Context(root);
            top = pageTop; bottom = pageBottom;
            foreach (var header in columnHeaders) { var layout = Build(header, c, template.ColumnWidth); Place(layout, X(), top); top += layout.Height; }
            var foot = columnFooters.Select(b => Build(b, c, template.ColumnWidth)).ToArray();
            bottom -= foot.Sum(l => l.Height);
            double footerY = bottom;
            foreach (var layout in foot) { Place(layout, X(), footerY); footerY += layout.Height; }
            y = top; columnHasContent = false;
            foreach (var header in repeated)
            {
                var layout = Build(header.Band, Context(header.Context), template.ColumnWidth);
                if (y + layout.Height >= bottom) throw Failure("RS2006", "Repeated headers leave no printable space.", header.Band.Id);
                Place(layout, X(), y); y += layout.Height;
            }
            top = y;
            if (bottom - top < .01) throw Failure("RS2006", "Headers and footers leave no printable space.");
        }
        void StartPage(IReadOnlyList<Instance> repeated)
        {
            cancellation.ThrowIfCancellationRequested();
            if (pages.Count >= options.MaximumPages) throw Failure("RS2007", "Maximum page count exceeded.");
            output = []; column = 0;
            pages.Add(new(pages.Count + 1, template.Width, template.Height, output));
            var c = Context(root);
            pageTop = template.Margins.Top;
            foreach (var overlay in overlays) Place(Build(overlay, c, template.ContentWidth), template.Margins.Left, pageTop);
            foreach (var header in pageHeaders) { var layout = Build(header, c, template.ContentWidth); Place(layout, template.Margins.Left, pageTop); pageTop += layout.Height; }
            var foot = pageFooters.Select(b => Build(b, c, template.ContentWidth)).ToArray();
            pageBottom = template.Height - template.Margins.Bottom - foot.Sum(l => l.Height);
            double footerY = pageBottom;
            foreach (var layout in foot) { Place(layout, template.Margins.Left, footerY); footerY += layout.Height; }
            StartColumn(repeated);
        }
        void NextColumn(IReadOnlyList<Instance> repeated, bool forcePage = false)
        {
            if (forcePage || column + 1 >= template.Columns) StartPage(repeated);
            else { column++; StartColumn(repeated); }
        }
        StartPage([]);
        bool has = content.MoveNext();
        while (has)
        {
            cancellation.ThrowIfCancellationRequested();
            var instance = content.Current;
            has = content.MoveNext();
            if (instance.Band.StartNewPage && columnHasContent) NextColumn(instance.Repeat, true);
            var layout = Build(instance.Band, Context(instance.Context), template.ColumnWidth);
            double keepHeight = layout.Height;
            if (instance.Band.CanSplit && layout.Flows.Count > 0)
                keepHeight = Math.Min(keepHeight, layout.Flows[0].RowEnds.FirstOrDefault(layout.Flows[0].Bottom));
            if ((instance.KeepWithNext || instance.Band.KeepWithNext) && has)
            {
                var next = content.Current;
                keepHeight = layout.Height + Math.Min(Build(next.Band, Context(next.Context), template.ColumnWidth).Height, bottom - top);
            }
            if (columnHasContent && y + keepHeight > bottom + .001)
            { NextColumn(instance.Repeat); layout = Build(instance.Band, Context(instance.Context), template.ColumnWidth); }
            bool horizontalTiles = layout.Flows.Any(f => f.BreakBefore);
            if (!instance.Band.CanSplit && (layout.Height > bottom - top + .001 || horizontalTiles))
                throw Failure("RS2008", $"Band '{instance.Band.Name}' requires pagination but cannot split.", instance.Band.Id);
            if (layout.Height <= .001) continue;
            if (layout.Height <= bottom - y + .001 && !horizontalTiles)
            { Place(layout, X(), y); y += layout.Height; columnHasContent = true; continue; }
            if (!instance.Band.CanSplit)
            { NextColumn(instance.Repeat); layout = Build(instance.Band, Context(instance.Context), template.ColumnWidth); Place(layout, X(), y); y += layout.Height; columnHasContent = true; continue; }
            double consumed = 0;
            bool repeatPending = false;
            while (consumed < layout.Height - .001)
            {
                cancellation.ThrowIfCancellationRequested();
                if (bottom - y < .01) { NextColumn(instance.Repeat); repeatPending = true; }
                if (repeatPending)
                {
                    var flow = layout.Flows.FirstOrDefault(f => f.RepeatHeaders && f.HeaderHeight > 0 && consumed >= f.Top + f.HeaderHeight - .001 && consumed < f.Bottom - .001);
                    if (flow is not null)
                    {
                        if (flow.HeaderHeight >= bottom - y - .001) throw Failure("RS1411", "Repeated table/matrix headers leave no room for a row.", instance.Band.Id);
                        Place(new(flow.HeaderHeight, flow.HeaderItems), X(), y); y += flow.HeaderHeight;
                    }
                    repeatPending = false;
                }
                double cut = Math.Min(layout.Height, consumed + bottom - y);
                var nextTile = layout.Flows.FirstOrDefault(f => f.BreakBefore && f.Top > consumed + .001);
                if (nextTile is not null) cut = Math.Min(cut, nextTile.Top);
                cut = SafeCut(layout, consumed, cut);
                if (cut <= consumed + .001)
                {
                    if (columnHasContent) { NextColumn(instance.Repeat); repeatPending = true; continue; }
                    throw Failure("RS1411", "A table/matrix row together with its headers is taller than a printable column. Increase the paper height or reduce row/text dimensions.", instance.Band.Id);
                }
                double amount = cut - consumed;
                Place(layout, X(), y, consumed, amount); y += amount; consumed = cut; columnHasContent = true;
                if (consumed < layout.Height - .001)
                {
                    bool forcePage = layout.Flows.Any(f => f.BreakBefore && Math.Abs(f.Top - consumed) < .001);
                    NextColumn(instance.Repeat, forcePage); repeatPending = true;
                }
            }
        }
    }
    private static double SafeCut(Layout layout, double consumed, double requested)
    {
        double cut = requested;
        while (true)
        {
            double previous = cut;
            foreach (var flow in layout.Flows)
            {
                if (cut <= flow.Top + .001 || cut >= flow.Bottom - .001) continue;
                double boundary = flow.Top >= consumed - .001 ? flow.Top : consumed;
                foreach (var rowEnd in flow.RowEnds)
                { if (rowEnd > cut + .001) break; if (rowEnd > consumed + .001) boundary = rowEnd; }
                cut = Math.Min(cut, boundary);
            }
            // Do not split a normal text line either. A second iteration rechecks atomic rows
            // if another overlapping non-table object moved the boundary upward.
            foreach (var item in layout.Items)
                if (item.Source is TextItem && item.Bounds.Y > consumed + .01 && item.Bounds.Y < cut - .001 && item.Bounds.Bottom > cut + .001)
                    cut = Math.Min(cut, item.Bounds.Y);
            if (previous - cut < .001) return cut;
        }
    }
    private static RectD Intersection(RectD a, RectD b)
    {
        double x = Math.Max(a.X, b.X), y = Math.Max(a.Y, b.Y);
        return new(x, y, Math.Max(0, Math.Min(a.Right, b.Right) - x), Math.Max(0, Math.Min(a.Bottom, b.Bottom) - y));
    }
}
