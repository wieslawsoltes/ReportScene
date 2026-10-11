using ReportScene.Expressions;
using ReportScene.Model;

namespace ReportScene.Engine;

internal sealed partial class PreparationRun
{
    private sealed record FragmentViewport(double Height, int PageNumber, int ColumnNumber);
    private sealed record EmbeddedResult(IReadOnlyList<PreparedPage> Pages, double UsedHeight);
    private (PreparationRun Run, ReportPage Page) ChildRun(SubreportItem item, EvaluationContext caller)
    {
        cancellation.ThrowIfCancellationRequested();
        if (subreportDepth >= options.MaximumSubreportDepth) throw Failure("RS1502", "Subreport nesting exceeds the configured depth limit.", item.Id);
        int invocation = ++budget.SubreportInvocations;
        if (invocation > options.MaximumSubreportInvocations) throw Failure("RS1503", "Subreport invocation/work limit exceeded.", item.Id);
        var page = document.Pages.FirstOrDefault(p => p.Name.Equals(item.PageName, StringComparison.OrdinalIgnoreCase))
            ?? throw Failure("RS1500", $"Subreport page '{item.PageName}' is missing.", item.Id);
        var parameters = new Dictionary<string, Value>(caller.Parameters, StringComparer.OrdinalIgnoreCase);
        // All bindings read the original caller scope, not values assigned by earlier bindings.
        foreach (var binding in item.ParameterBindings) parameters[binding.Name] = Evaluate(binding.Expression, caller, item.Id);
        var context = caller with { Parameters = parameters, Parent = caller };
        var run = new PreparationRun(document, context, options, expressions, measurer, cancellation)
        { budget = budget, subreportDepth = subreportDepth + 1, invocationPrefix = item.Id + "@" + invocation + ":" };
        return (run, page);
    }
    private EmbeddedResult PrepareEmbedded(SubreportItem item, EvaluationContext caller, Func<int, FragmentViewport> viewport)
    {
        var (run, source) = ChildRun(item, caller); run.embeddedViewport = viewport;
        // A subreport page supplies bands, not a second paper/margin system.
        var page = source with { Width = item.Bounds.Width, Margins = new(0, 0, 0, 0), Columns = 1, ColumnGap = 0 };
        run.Paginate(page);
        return new(run.pages, run.lastUsedHeight);
    }
    private Layout PrepareInline(SubreportItem item, EvaluationContext caller)
    {
        var (run, page) = ChildRun(item, caller);
        return run.FlattenPage(page, item.Bounds.Width);
    }
    private Layout FlattenPage(ReportPage page, double width)
    {
        var items = new List<PreparedItem>(); var sections = new List<FlowSection>(); double y = 0;
        // On-parent mode evaluates at the parent band's page/row context. Page/column decorations
        // occur once inside the parent; they are not additional physical page decorations.
        var ordered = page.Bands.Where(b => b.Kind is BandKind.PageHeader or BandKind.ColumnHeader)
            .Concat(page.Bands.Where(b => b.Kind is not (BandKind.PageHeader or BandKind.ColumnHeader or BandKind.PageFooter or BandKind.ColumnFooter or BandKind.Overlay)))
            .Concat(page.Bands.Where(b => b.Kind is BandKind.ColumnFooter or BandKind.PageFooter));
        foreach (var instance in ordered.SelectMany(b => Emit(b, root, [])))
        {
            if (instance.Band.StartNewPage) throw Failure("RS1504", "Explicit page breaks inside PrintOnParent cannot be honored. Use independent band-flow mode.", instance.Band.Id);
            var layout = Build(instance.Band, instance.Context, width);
            if (layout.Flows.Any(f => f.BreakBefore)) throw Failure("RS1504", "Horizontally tiled matrices require independent band-flow mode, not PrintOnParent.", instance.Band.Id);
            foreach (var item in layout.Items) Append(items, Offset(item, 0, y, invocationPrefix));
            foreach (var section in layout.Flows) sections.Add(Offset(section, 0, y, invocationPrefix) with { RepeatHeaders = false });
            double end = y + layout.Height;
            foreach (var subreport in layout.Subreports)
            {
                var nested = PrepareInline(subreport, instance.Context);
                double top = y + subreport.Bounds.Y;
                foreach (var item in nested.Items) Append(items, ClipItem(Offset(item, subreport.Bounds.X, top, invocationPrefix), new(subreport.Bounds.X, top, subreport.Bounds.Width, nested.Height)));
                foreach (var section in nested.Flows) sections.Add(Offset(section, subreport.Bounds.X, top, invocationPrefix) with { RepeatHeaders = false });
                end = Math.Max(end, top + nested.Height);
            }
            if (!instance.Band.CanSplit && end > y) sections.Add(new(y, end, 0, [end], [], false));
            y = end;
        }
        foreach (var overlay in page.Bands.Where(b => b.Kind == BandKind.Overlay))
        {
            var layout = Build(overlay, root, width);
            foreach (var item in layout.Items) Append(items, Offset(item, 0, 0, invocationPrefix));
            y = Math.Max(y, layout.Height);
        }
        return new(y, items, sections);
    }
    private Layout BuildComposedBand(Band band, EvaluationContext context, double width)
    {
        if (!Visible(band.VisibleExpression, context, band.Id)) return new(0, []);
        var inline = new Dictionary<string, (SubreportItem Item, Layout Layout)>();
        var displacements = new List<(double OriginalBottom, double Delta)>();
        double Shift(double originalY)
        {
            var eligible = displacements.Where(d => originalY >= d.OriginalBottom - .001).ToArray();
            return eligible.Length == 0 ? 0 : eligible.Max(d => d.Delta);
        }
        var visible = band.Items.OfType<SubreportItem>().Where(i => Visible(i.VisibleExpression, context, i.Id)).ToArray();
        foreach (var original in visible.Where(s => s.PrintOnParent).OrderBy(s => s.Bounds.Y))
        {
            if (original.Bounds.Right > width + .001) throw Failure("RS1505", "Subreport bounds exceed the printable column width.", original.Id);
            var item = original with { Bounds = original.Bounds.Offset(0, Shift(original.Bounds.Y)) };
            var content = PrepareInline(item, context);
            double height = original.Bounds.Height;
            if (original.CanGrow) height = Math.Max(height, content.Height);
            if (original.CanShrink) height = Math.Min(height, content.Height);
            item = item with { Bounds = item.Bounds with { Height = height } };
            inline.Add(item.Id, (item, content));
            if (original.ShiftFollowing) displacements.Add((original.Bounds.Bottom, item.Bounds.Bottom - original.Bounds.Bottom));
        }
        var streams = visible.Where(s => !s.PrintOnParent).Select(s => s with { Bounds = s.Bounds.Offset(0, Shift(s.Bounds.Y)) }).ToList();
        for (int i = 0; i < streams.Count; i++)
        {
            if (streams[i].Bounds.Right > width + .001) throw Failure("RS1505", "Subreport bounds exceed the printable column width.", streams[i].Id);
            for (int j = 0; j < i; j++)
                if (streams[i].Bounds.X < streams[j].Bounds.Right - .001 && streams[j].Bounds.X < streams[i].Bounds.Right - .001)
                    throw Failure("RS1505", "Independent subreport lanes overlap horizontally. Place sequential subreports in separate bands.", streams[i].Id);
        }
        double heightBase = streams.Count > 0 ? streams.Min(s => s.Bounds.Y) : Math.Max(0, band.Height + Shift(band.Height));
        var regular = band.Items.Where(i => i is not SubreportItem).Select(i => i with { Bounds = i.Bounds.Offset(0, Shift(i.Bounds.Y)) }).ToList();
        var layout = BuildCore(band with { Height = heightBase, Items = regular, AlternateFill = null }, context, width);
        var items = layout.Items; var flows = layout.Flows.ToList(); double heightFinal = layout.Height;
        foreach (var original in band.Items.OfType<SubreportItem>())
        {
            if (!inline.TryGetValue(original.Id, out var entry)) continue;
            var bounds = entry.Item.Bounds;
            if (bounds.Height <= .001) continue;
            if (entry.Item.Style.BorderWidth > 0 || entry.Item.Style.Fill != "#00000000" || entry.Item.Bookmark is not null)
            {
                var background = new ShapeItem { Id = entry.Item.Id + ":frame", Name = entry.Item.Name, Bounds = bounds, Style = entry.Item.Style,
                    Bookmark = entry.Item.Bookmark is null ? null : Text(entry.Item.Bookmark, context, entry.Item.Id), Hyperlink = entry.Item.Hyperlink };
                Append(items, new(background, bounds));
            }
            foreach (var item in entry.Layout.Items)
            {
                var shifted = ClipItem(Offset(item, bounds.X, bounds.Y), bounds);
                if (shifted.Clip is { Width: > .001, Height: > .001 }) Append(items, shifted);
            }
            foreach (var flow in entry.Layout.Flows)
            {
                var moved = Offset(flow, bounds.X, bounds.Y);
                if (moved.Bottom <= bounds.Bottom + .001) flows.Add(moved with { RepeatHeaders = false });
            }
            heightFinal = Math.Max(heightFinal, bounds.Bottom);
        }
        if (streams.Count > 0 && heightFinal > heightBase + .001)
            throw Failure("RS1506", "Objects extend below an independent subreport's anchor. Move them to a Child band or use PrintOnParent.", band.Id);
        if (band.AlternateFill is { } fill && context.RowNumber % 2 == 0 && heightFinal > 0)
        {
            if (items.Count >= options.MaximumItems) throw Failure("RS2005", "Prepared item limit exceeded.", band.Id);
            items.Insert(0, new(new ShapeItem { Id = band.Id + ":alternate", Bounds = new(0, 0, width, heightFinal), Style = new() { Fill = fill } }, new(0, 0, width, heightFinal)));
        }
        flows.Sort((a, b) => a.Top.CompareTo(b.Top));
        return new(heightFinal, items, flows, streams);
    }
    private static PreparedItem Offset(PreparedItem item, double x, double y, string prefix = "") => item with
    { Bounds = item.Bounds.Offset(x, y), Clip = item.Clip?.Offset(x, y), Source = prefix.Length == 0 ? item.Source : item.Source with { Id = prefix + item.Source.Id } };
    private static FlowSection Offset(FlowSection flow, double x, double y, string prefix = "") => flow with
    {
        Top = flow.Top + y, Bottom = flow.Bottom + y, RowEnds = flow.RowEnds.Select(r => r + y).ToArray(),
        HeaderItems = flow.HeaderItems.Select(i => Offset(i, x, 0, prefix)).ToList()
    };
    private static PreparedItem ClipItem(PreparedItem item, RectD clip) => item with { Clip = item.Clip is { } existing ? Intersection(existing, clip) : clip };
}
