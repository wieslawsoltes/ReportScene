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
        FragmentViewport Geometry(int localPage) => embeddedViewport?.Invoke(localPage) ?? new(template.Height, localPage + 1, 1);
        EvaluationContext At(EvaluationContext original, int localPage, int col)
        {
            var geometry = Geometry(localPage);
            return original with { PageNumber = geometry.PageNumber, ColumnNumber = embeddedViewport is null ? col + 1 : geometry.ColumnNumber };
        }
        EvaluationContext Context(EvaluationContext original) => At(original, pages.Count - 1, column);
        bool ShortFirst() => embeddedViewport is not null && pages.Count == 1 && Geometry(1).Height > Geometry(0).Height + .01;
        double X() => template.Margins.Left + column * (template.ColumnWidth + template.ColumnGap);
        Layout Fixed(Band band, EvaluationContext c, double width)
        {
            var layout = Build(band, c, width);
            if (layout.Subreports.Count > 0) throw Failure("RS1506", "Repeated headers, footers and overlays require PrintOnParent subreports.", band.Id);
            return layout;
        }
        void Place(Layout layout, double x, double at, double consumed = 0, double? sliceHeight = null)
        {
            var slice = new RectD(x, at, template.ColumnWidth, sliceHeight ?? layout.Height);
            var candidates = sliceHeight is { } amount ? SliceCandidates(layout, consumed, amount) : layout.Items;
            foreach (var item in candidates)
            {
                cancellation.ThrowIfCancellationRequested();
                var bounds = item.Bounds.Offset(x, at - consumed); var clip = item.Clip?.Offset(x, at - consumed);
                if (sliceHeight is not null)
                {
                    if (bounds.Y >= slice.Bottom - .001 || bounds.Bottom <= slice.Y + .001 || bounds.X >= slice.Right || bounds.Right <= slice.X) continue;
                    clip = clip is { } existing ? Intersection(existing, slice) : slice;
                }
                if (clip is { } area && (area.Width <= .001 || area.Height <= .001)) continue;
                if (++itemCount > options.MaximumItems) throw Failure("RS2005", "Maximum prepared-item count exceeded.");
                var source = invocationPrefix.Length == 0 ? item.Source : item.Source with { Id = invocationPrefix + item.Source.Id };
                output.Add(item with { Bounds = bounds, Clip = clip, Source = source });
            }
        }
        void StartColumn(IReadOnlyList<Instance> repeated)
        {
            var c = Context(root); top = pageTop; bottom = pageBottom;
            foreach (var header in columnHeaders) { var layout = Fixed(header, c, template.ColumnWidth); Place(layout, X(), top); top += layout.Height; }
            var foot = columnFooters.Select(b => Fixed(b, c, template.ColumnWidth)).ToArray();
            bottom -= foot.Sum(l => l.Height); double footerY = bottom;
            foreach (var layout in foot) { Place(layout, X(), footerY); footerY += layout.Height; }
            y = top; columnHasContent = false;
            foreach (var header in repeated)
            {
                var layout = Fixed(header.Band, Context(header.Context), template.ColumnWidth);
                Place(layout, X(), y); y += layout.Height;
            }
            top = y;
            if (bottom - top < .01)
            {
                if (ShortFirst()) { itemCount -= output.Count; output.Clear(); StartPage(repeated); return; }
                throw Failure("RS2006", "Headers and footers leave no printable space.");
            }
        }
        void StartPage(IReadOnlyList<Instance> repeated)
        {
            cancellation.ThrowIfCancellationRequested();
            var geometry = Geometry(pages.Count);
            // Child fragments are parent-column viewports, not necessarily physical pages.
            // Apply the public page budget to the final physical page number in both modes.
            if (geometry.PageNumber > options.MaximumPages) throw Failure("RS2007", "Maximum page count exceeded.");
            if (!double.IsFinite(geometry.Height) || geometry.Height <= 0) throw Failure("RS1507", "A subreport has no positive printable viewport.");
            output = []; column = 0;
            pages.Add(new(geometry.PageNumber, template.Width, geometry.Height, output));
            var c = Context(root); pageTop = template.Margins.Top;
            foreach (var overlay in overlays) Place(Fixed(overlay, c, template.ContentWidth), template.Margins.Left, pageTop);
            foreach (var header in pageHeaders) { var layout = Fixed(header, c, template.ContentWidth); Place(layout, template.Margins.Left, pageTop); pageTop += layout.Height; }
            var foot = pageFooters.Select(b => Fixed(b, c, template.ContentWidth)).ToArray();
            pageBottom = geometry.Height - template.Margins.Bottom - foot.Sum(l => l.Height); double footerY = pageBottom;
            foreach (var layout in foot) { Place(layout, template.Margins.Left, footerY); footerY += layout.Height; }
            StartColumn(repeated);
        }
        void NextColumn(IReadOnlyList<Instance> repeated, bool forcePage = false)
        {
            if (forcePage && embeddedViewport is not null)
            {
                int physicalPage = Geometry(pages.Count - 1).PageNumber, skipped = 0;
                // Keep an empty fragment for each unused parent column in this lane. Other
                // parallel lanes can still use that column; the explicit break affects only
                // this child flow. Nested children inherit the same physical-page schedule.
                while (Geometry(pages.Count).PageNumber == physicalPage)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (++skipped > 64) throw Failure("RS1508", "The subreport viewport schedule did not advance to another physical page.");
                    var gap = Geometry(pages.Count);
                    pages.Add(new(gap.PageNumber, template.Width, gap.Height, []));
                }
            }
            if (forcePage || column + 1 >= template.Columns) StartPage(repeated);
            else { column++; StartColumn(repeated); }
        }
        (double Top, double Bottom, FragmentViewport Geometry) MeasureBody(int localPage, int col, IReadOnlyList<Instance> repeated)
        {
            var c = At(root, localPage, col); var geometry = Geometry(localPage);
            double start = template.Margins.Top, end = geometry.Height - template.Margins.Bottom;
            foreach (var header in pageHeaders) start += Fixed(header, c, template.ContentWidth).Height;
            foreach (var header in columnHeaders) start += Fixed(header, c, template.ColumnWidth).Height;
            foreach (var footer in pageFooters) end -= Fixed(footer, c, template.ContentWidth).Height;
            foreach (var footer in columnFooters) end -= Fixed(footer, c, template.ColumnWidth).Height;
            foreach (var header in repeated) start += Fixed(header.Band, At(header.Context, localPage, col), template.ColumnWidth).Height;
            if (end - start < .01) throw Failure("RS2006", "Repeated headers leave no subreport continuation space.");
            return (start, end, geometry with { ColumnNumber = c.ColumnNumber });
        }
        void PrintSubreports(Layout layout, Instance instance)
        {
            if (layout.Subreports.Count == 0) return;
            if (bottom - y < .01) NextColumn(instance.Repeat);
            double anchor = y, firstBottom = bottom; int firstPage = pages.Count - 1, firstColumn = column;
            var firstContext = Context(instance.Context);
            var continuation = new Dictionary<int, (double Top, double Bottom, FragmentViewport Geometry)>();
            (double Top, double Bottom, FragmentViewport Geometry) Body(int slot)
            {
                if (continuation.TryGetValue(slot, out var cached)) return cached;
                int absolute = firstColumn + slot;
                var result = MeasureBody(firstPage + absolute / template.Columns, absolute % template.Columns, instance.Repeat);
                continuation.Add(slot, result); return result;
            }
            var lanes = new List<(SubreportItem Item, double Gap, EmbeddedResult Report)>(); long generated = 0;
            foreach (var item in layout.Subreports)
            {
                double gap = item.Bounds.Y - layout.Height; double firstHeight = firstBottom - anchor - gap;
                if (firstHeight <= .001) throw Failure("RS1507", "Subreport anchors must fit in the current printable band. Align parallel subreports at the lower band edge.", item.Id);
                FragmentViewport Viewport(int slot)
                {
                    if (slot == 0) return new(firstHeight, firstContext.PageNumber, firstContext.ColumnNumber);
                    var body = Body(slot); return body.Geometry with { Height = body.Bottom - body.Top };
                }
                var report = PrepareEmbedded(item, firstContext, Viewport);
                generated += report.Pages.Sum(p => (long)p.Items.Count);
                if (generated > options.MaximumItems - itemCount) throw Failure("RS2005", "Combined subreport lanes exceed the prepared-item limit.", item.Id);
                lanes.Add((item, gap, report));
            }
            int count = lanes.Max(l => l.Report.Pages.Count);
            for (int slot = 0; slot < count; slot++)
            {
                cancellation.ThrowIfCancellationRequested(); if (slot > 0) NextColumn(instance.Repeat);
                double start = slot == 0 ? anchor : y, last = start;
                foreach (var lane in lanes)
                {
                    if (slot >= lane.Report.Pages.Count) continue;
                    var fragment = lane.Report.Pages[slot]; double at = start + (slot == 0 ? lane.Gap : 0);
                    var clip = new RectD(0, 0, lane.Item.Bounds.Width, fragment.Height);
                    var items = fragment.Items.Select(i => ClipItem(i, clip)).ToList();
                    if (slot == 0 && lane.Item.Bookmark is { } bookmark && items.Count > 0)
                        items[0] = items[0] with { Source = items[0].Source with { Bookmark = Text(bookmark, firstContext, lane.Item.Id) } };
                    Place(new(fragment.Height, items), X() + lane.Item.Bounds.X, at);
                    double used = slot == lane.Report.Pages.Count - 1 ? lane.Report.UsedHeight : fragment.Height;
                    last = Math.Max(last, at + used);
                }
                if (last > bottom + .01) throw Failure("RS1508", "Parent printable geometry changed during subreport composition.", instance.Band.Id);
                y = last; columnHasContent |= last > start + .001;
            }
        }
        StartPage([]); bool has = content.MoveNext();
        while (has)
        {
            cancellation.ThrowIfCancellationRequested(); var instance = content.Current; has = content.MoveNext();
            if (instance.Band.StartNewPage && columnHasContent) NextColumn(instance.Repeat, true);
            var layout = Build(instance.Band, Context(instance.Context), template.ColumnWidth);
            double keepHeight = layout.Height;
            if (instance.Band.CanSplit && layout.Flows.Count > 0) keepHeight = Math.Min(keepHeight, layout.Flows[0].RowEnds.FirstOrDefault(layout.Flows[0].Bottom));
            if ((instance.KeepWithNext || instance.Band.KeepWithNext) && has)
            {
                var next = content.Current;
                keepHeight = layout.Height + Math.Min(Build(next.Band, Context(next.Context), template.ColumnWidth).Height, bottom - top);
            }
            if (columnHasContent && y + keepHeight > bottom + .001)
            { NextColumn(instance.Repeat); layout = Build(instance.Band, Context(instance.Context), template.ColumnWidth); }
            bool horizontalTiles = layout.Flows.Any(f => f.BreakBefore);
            if (!instance.Band.CanSplit && layout.Height > bottom - top + .001 && ShortFirst())
            { NextColumn(instance.Repeat); layout = Build(instance.Band, Context(instance.Context), template.ColumnWidth); }
            if (!instance.Band.CanSplit && (layout.Height > bottom - top + .001 || horizontalTiles))
                throw Failure("RS2008", $"Band '{instance.Band.Name}' requires pagination but cannot split.", instance.Band.Id);
            if (layout.Height <= .001) { PrintSubreports(layout, instance); continue; }
            if (layout.Height <= bottom - y + .001 && !horizontalTiles)
            { Place(layout, X(), y); y += layout.Height; columnHasContent = true; PrintSubreports(layout, instance); continue; }
            if (!instance.Band.CanSplit)
            {
                NextColumn(instance.Repeat); layout = Build(instance.Band, Context(instance.Context), template.ColumnWidth);
                Place(layout, X(), y); y += layout.Height; columnHasContent = true; PrintSubreports(layout, instance); continue;
            }
            double consumed = 0; bool repeatPending = false;
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
                    if (columnHasContent || ShortFirst()) { NextColumn(instance.Repeat); repeatPending = true; continue; }
                    throw Failure("RS1411", "An atomic row or group is taller than the printable column. Increase paper height, reduce row sizes, or use independent subreports for parallel flows.", instance.Band.Id);
                }
                double amount = cut - consumed;
                Place(layout, X(), y, consumed, amount); y += amount; consumed = cut; columnHasContent = true;
                if (consumed < layout.Height - .001)
                {
                    bool forcePage = layout.Flows.Any(f => f.BreakBefore && Math.Abs(f.Top - consumed) < .001);
                    NextColumn(instance.Repeat, forcePage); repeatPending = true;
                }
            }
            PrintSubreports(layout, instance);
        }
        lastUsedHeight = pageFooters.Length > 0 || columnFooters.Length > 0 ? Geometry(pages.Count - 1).Height : y;
    }
    private double SafeCut(Layout layout, double consumed, double requested)
    {
        double cut = requested;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested(); double previous = cut;
            foreach (var flow in layout.Flows)
            {
                if (cut <= flow.Top + .001 || cut >= flow.Bottom - .001) continue;
                cut = Math.Min(cut, RowBoundary(flow, consumed, cut));
            }
            foreach (var item in CutCandidates(layout, cut))
                if (item.Source is TextItem && item.Bounds.Y > consumed + .01 && item.Bounds.Y < cut - .001 && item.Bounds.Bottom > cut + .001) cut = Math.Min(cut, item.Bounds.Y);
            if (previous - cut < .001) return cut;
        }
    }
    private static RectD Intersection(RectD a, RectD b)
    {
        double x = Math.Max(a.X, b.X), y = Math.Max(a.Y, b.Y);
        return new(x, y, Math.Max(0, Math.Min(a.Right, b.Right) - x), Math.Max(0, Math.Min(a.Bottom, b.Bottom) - y));
    }
}
