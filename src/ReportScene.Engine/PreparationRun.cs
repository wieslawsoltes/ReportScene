using System.Globalization;
using ReportScene.Expressions;
using ReportScene.Model;

namespace ReportScene.Engine;

/// <summary>One bounded pagination pass. Mutable state is never shared with another preparation.</summary>
internal sealed partial class PreparationRun
{
    private readonly ReportDocument document;
    private readonly EvaluationContext root;
    private readonly PreparationOptions options;
    private readonly ExpressionEngine expressions;
    private readonly ITextMeasurer measurer;
    private readonly CancellationToken cancellation;
    private readonly List<PreparedPage> pages = [];
    private readonly CultureInfo culture;
    private int itemCount;
    private sealed class PreparationBudget { public int BandInstances; public int SubreportInvocations; }
    private PreparationBudget budget = new();
    private int subreportDepth;
    private string invocationPrefix = "";
    private Func<int, FragmentViewport>? embeddedViewport;
    private double lastUsedHeight;
    public PreparationRun(ReportDocument document, EvaluationContext root, PreparationOptions options,
        ExpressionEngine expressions, ITextMeasurer measurer, CancellationToken cancellation)
    {
        this.document = document; this.root = root; this.options = options; this.expressions = expressions;
        this.measurer = measurer; this.cancellation = cancellation; culture = CultureInfo.GetCultureInfo(document.Culture);
    }
    private static ReportPreparationException Failure(string code, string message, string? id = null) => new([new(code, message, ObjectId: id)]);
    private sealed record Instance(Band Band, EvaluationContext Context, IReadOnlyList<Instance> Repeat, bool KeepWithNext = false);
    private sealed record FlowSection(double Top, double Bottom, double HeaderHeight, double[] RowEnds,
        List<PreparedItem> HeaderItems, bool RepeatHeaders = true, bool BreakBefore = false);
    private sealed record Layout(double Height, List<PreparedItem> Items, List<FlowSection>? Sections = null, List<SubreportItem>? SubreportItems = null)
    {
        public IReadOnlyList<FlowSection> Flows => Sections ?? (IReadOnlyList<FlowSection>)Array.Empty<FlowSection>();
        public IReadOnlyList<SubreportItem> Subreports => SubreportItems ?? (IReadOnlyList<SubreportItem>)Array.Empty<SubreportItem>();
    }
    private Value Evaluate(string text, EvaluationContext c, string id)
    {
        cancellation.ThrowIfCancellationRequested();
        try { return expressions.Evaluate(text, c); }
        catch (ExpressionException e) { throw Failure("RS2001", e.Message, id); }
    }
    private string Text(string text, EvaluationContext c, string id, string? format = null)
    {
        cancellation.ThrowIfCancellationRequested();
        try { return expressions.Interpolate(text, c, format, culture); }
        catch (ExpressionException e) { throw Failure("RS2001", e.Message, id); }
    }
    private bool Visible(string? expression, EvaluationContext c, string id) => string.IsNullOrWhiteSpace(expression) || Evaluate(expression, c, id).AsBoolean();
    public IReadOnlyList<PreparedPage> Prepare()
    {
        foreach (var page in SubreportGraph.PrintablePages(document)) Paginate(page);
        return pages;
    }
    private Instance Count(Instance instance)
    {
        if (++budget.BandInstances > options.MaximumBandInstances) throw Failure("RS2002", "Maximum band instance count exceeded, including subreports.");
        return instance;
    }
    private void Append(List<PreparedItem> items, PreparedItem item)
    {
        if (items.Count >= options.MaximumItems) throw Failure("RS2005", "One band exceeds the prepared-item limit.", item.Source.Id);
        items.Add(item);
    }
    private Layout Build(Band band, EvaluationContext context, double width)
        => band.Items.Any(i => i is SubreportItem) ? BuildComposedBand(band, context, width) : BuildCore(band, context, width);
    private Layout BuildCore(Band band, EvaluationContext context, double width)
    {
        if (!Visible(band.VisibleExpression, context, band.Id)) return new(0, []);
        var items = new List<PreparedItem>(); var flows = new List<FlowSection>(); double height = band.Height;
        foreach (var source in band.Items)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!Visible(source.VisibleExpression, context, source.Id)) continue;
            var item = source with
            {
                Hyperlink = source.Hyperlink is null ? null : Text(source.Hyperlink, context, source.Id),
                Bookmark = source.Bookmark is null ? null : Text(source.Bookmark, context, source.Id)
            };
            string text = item switch
            {
                TextItem t => Text(t.Text, context, item.Id, t.Format),
                BarcodeItem b => Text(b.Text, context, item.Id),
                CheckBoxItem check => Evaluate(check.Expression, context, item.Id).AsBoolean() ? "True" : "False", _ => ""
            };
            var bounds = item.Bounds;
            if (item is TextItem)
            {
                var lines = TextLayout.Wrap(text, bounds.Width - 2 * item.Style.Padding, item.Style, measurer);
                double required = lines.Count * measurer.LineHeight(item.Style) + 2 * item.Style.Padding;
                if (item.CanGrow) bounds = bounds with { Height = Math.Max(bounds.Height, required) };
                if (item.CanShrink) bounds = bounds with { Height = Math.Min(bounds.Height, required) };
                AddText(items, item, bounds, lines);
            }
            else if (item is MatrixItem matrix)
            {
                var layout = BuildMatrix(matrix, context, width);
                foreach (var prepared in layout.Items) Append(items, prepared);
                flows.AddRange(layout.Flows); bounds = bounds with { Height = layout.Height };
            }
            else if (item is TableItem table)
            {
                if (!root.Tables.TryGetValue(table.DataSource, out var data)) throw Failure("RS2003", $"Table data source '{table.DataSource}' is not bound.", item.Id);
                double y = bounds.Y; var columns = table.Columns; double totalWidth = columns.Sum(col => col.Width);
                var rowEnds = new List<double>(); var headerItems = new List<PreparedItem>();
                void TableRow(DataRow? row, bool header, int number)
                {
                    double x = bounds.X;
                    for (int ci = 0; ci < columns.Count; ci++)
                    {
                        var col = columns[ci]; double w = col.Width / totalWidth * bounds.Width;
                        string value = header ? col.Title : Evaluate(col.Expression, context with { Row = row!, TableName = data.Name, Rows = data.Rows, RowNumber = number, Parent = context }, item.Id).AsString(col.Format, culture);
                        var style = item.Style with { Bold = header || item.Style.Bold, Fill = header ? "#E1EAF4" : number % 2 == 0 ? "#F5F7FA" : "#FFFFFF", BorderWidth = Math.Max(.4, item.Style.BorderWidth) };
                        var cell = new TextItem { Id = item.Id + ":" + number + ":" + ci, Name = item.Name, Text = value, Style = style, Bounds = new(x, y, w, table.RowHeight) };
                        AddText(items, cell, cell.Bounds, TextLayout.Wrap(value, w - style.Padding * 2, style, measurer)); x += w;
                    }
                    y += table.RowHeight; if (!header) rowEnds.Add(y);
                }
                if (table.ShowHeader)
                {
                    int start = items.Count; TableRow(null, true, 0);
                    headerItems.AddRange(items.Skip(start).Select(i => i with { Bounds = i.Bounds.Offset(0, -bounds.Y), Clip = i.Clip?.Offset(0, -bounds.Y) }));
                }
                for (int n = 0; n < data.Rows.Count; n++) { cancellation.ThrowIfCancellationRequested(); TableRow(data.Rows[n], false, n + 1); }
                if (y > bounds.Y) flows.Add(new(bounds.Y, y, table.ShowHeader ? table.RowHeight : 0, rowEnds.ToArray(), headerItems));
                bounds = bounds with { Height = y - bounds.Y };
            }
            else if (item is ChartItem chart)
            {
                if (!root.Tables.TryGetValue(chart.DataSource, out var data)) throw Failure("RS2003", $"Chart data source '{chart.DataSource}' is not bound.", item.Id);
                if (data.Rows.Count > 10_000) throw Failure("RS2004", "Chart data exceeds 10,000 points; aggregate the data first.", item.Id);
                var points = data.Rows.Select(row => new ChartPoint(row.TryGetValue(chart.CategoryColumn, out var v) ? v.AsString() : "", Evaluate(chart.ValueExpression, context with { Row = row, TableName = data.Name, Rows = data.Rows }, item.Id).AsNumber())).ToArray();
                Append(items, new(item, bounds, chart.Title, points));
            }
            else if (item is SubreportItem) throw Failure("RS1501", "Subreport composition was not resolved before drawing.", item.Id);
            else Append(items, new(item, bounds, text));
            height = Math.Max(height, bounds.Bottom);
        }
        flows.Sort((a, b) => a.Top.CompareTo(b.Top));
        for (int i = 1; i < flows.Count; i++)
            if (flows[i].Top < flows[i - 1].Bottom - .001) throw Failure("RS1412", "Independently paginated tables or matrices overlap vertically. Place them in separate bands or independent subreports.", band.Id);
        if (band.AlternateFill is { } fill && context.RowNumber % 2 == 0)
        {
            if (items.Count >= options.MaximumItems) throw Failure("RS2005", "One band exceeds the prepared-item limit.", band.Id);
            items.Insert(0, new(new ShapeItem { Id = band.Id + ":alternate", Bounds = new(0, 0, width, height), Style = new() { Fill = fill } }, new(0, 0, width, height)));
        }
        return new(height, items, flows);
    }
    private void AddText(List<PreparedItem> output, ReportItem item, RectD bounds, IReadOnlyList<string> lines)
    {
        var style = item.Style;
        if (style.BorderWidth > 0 || style.Fill != "#00000000")
            Append(output, new(new ShapeItem { Id = item.Id + ":background", Name = item.Name, Style = style, Bounds = bounds }, bounds));
        double lineHeight = measurer.LineHeight(style), available = bounds.Height - style.Padding * 2;
        double offset = style.VerticalAlignment switch { VerticalAlign.Center => Math.Max(0, (available - lines.Count * lineHeight) / 2), VerticalAlign.Bottom => Math.Max(0, available - lines.Count * lineHeight), _ => 0 };
        for (int i = 0; i < lines.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var r = new RectD(bounds.X + style.Padding, bounds.Y + style.Padding + offset + i * lineHeight, Math.Max(0.01, bounds.Width - style.Padding * 2), lineHeight);
            if (r.Y >= bounds.Bottom) break;
            var source = item with { Bounds = r, Bookmark = i == 0 ? item.Bookmark : null, Style = style with { Padding = 0, BorderWidth = 0, Fill = "#00000000", VerticalAlignment = VerticalAlign.Top } };
            Append(output, new(source, r, lines[i], Clip: bounds));
        }
    }
}
