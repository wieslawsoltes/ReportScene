using System.Diagnostics;
using System.Globalization;
using ReportScene.Expressions;
using ReportScene.Model;

namespace ReportScene.Engine;

public sealed record PreparationOptions(int MaximumPages = 10_000, int MaximumItems = 1_000_000, int MaximumBandInstances = 1_000_000, int MaximumPasses = 5, DateTime? ReportDate = null);
public sealed class ReportPreparationException(IReadOnlyList<Diagnostic> diagnostics) : Exception(string.Join("\n", diagnostics.Select(d => d.Message)))
{ public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics; }

/// <summary>Stateless per preparation; only immutable expression ASTs are cached between reports.</summary>
public sealed class ReportPreparer(ITextMeasurer? textMeasurer = null, ExpressionEngine? expressions = null)
{
    private readonly ITextMeasurer measurer = textMeasurer ?? new ApproximateTextMeasurer();
    private readonly ExpressionEngine expressions = expressions ?? new();
    public PreparedReport Prepare(ReportDocument document, IReadOnlyDictionary<string, Value>? parameters = null, PreparationOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        if (options.MaximumPages < 1 || options.MaximumItems < 1 || options.MaximumBandInstances < 1 || options.MaximumPasses < 1) throw new ArgumentOutOfRangeException(nameof(options));
        var validation = DocumentValidator.Validate(document);
        if (validation.Any(d => d.Severity == DiagnosticSeverity.Error)) throw new ReportPreparationException(validation);
        var values = new Dictionary<string, Value>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in document.Parameters) values.Add(p.Name, p.DefaultValue);
        if (parameters is not null) foreach (var p in parameters) values[p.Key] = p.Value;
        foreach (var p in document.Parameters.Where(p => p.Required))
            if (!values.TryGetValue(p.Name, out var v) || v.IsNull || v.Kind == ValueKind.String && string.IsNullOrWhiteSpace(v.Text))
                throw Failure("RS1002", $"Required parameter '{p.Name}' has no value.");
        var context = new EvaluationContext
        {
            Parameters = values,
            Tables = document.Data.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase),
            ReportDate = options.ReportDate ?? DateTime.UtcNow
        };
        var watch = Stopwatch.StartNew();
        int totalPages = 1;
        for (int pass = 0; pass < options.MaximumPasses; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var run = new Run(document, context with { TotalPages = totalPages }, options, expressions, measurer, cancellationToken);
            var pages = run.Prepare();
            if (pages.Count == totalPages) return new(document.Name, pages, [], watch.Elapsed);
            totalPages = pages.Count;
        }
        throw Failure("RS2032", "Page-count dependent layout did not converge within the configured pass limit.");
    }
    private static ReportPreparationException Failure(string code, string message, string? id = null) => new([new(code, message, ObjectId: id)]);
    private sealed record Instance(Band Band, EvaluationContext Context, IReadOnlyList<Instance> Repeat, bool KeepWithNext = false);
    private sealed record Layout(double Height, List<PreparedItem> Items);

    private sealed class Run(ReportDocument document, EvaluationContext root, PreparationOptions options, ExpressionEngine expressions, ITextMeasurer measurer, CancellationToken cancellation)
    {
        private readonly List<PreparedPage> pages = [];
        private readonly CultureInfo culture = CultureInfo.GetCultureInfo(document.Culture);
        private int instances, itemCount;
        private Value Evaluate(string text, EvaluationContext c, string id)
        {
            cancellation.ThrowIfCancellationRequested();
            try { return expressions.Evaluate(text, c); }
            catch (ExpressionException e) { throw Failure("RS2001", e.Message, id); }
        }
        private string Text(string text, EvaluationContext c, string id, string? format = null)
        {
            try { return expressions.Interpolate(text, c, format, culture); }
            catch (ExpressionException e) { throw Failure("RS2001", e.Message, id); }
        }
        private bool Visible(string? expression, EvaluationContext c, string id) => string.IsNullOrWhiteSpace(expression) || Evaluate(expression, c, id).AsBoolean();
        public IReadOnlyList<PreparedPage> Prepare()
        {
            foreach (var page in document.Pages) Paginate(page);
            return pages;
        }
        private IEnumerable<Instance> Emit(Band band, EvaluationContext parent, IReadOnlyList<Instance> inherited)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!Visible(band.VisibleExpression, parent, band.Id)) yield break;
            if (band.Kind != BandKind.Data)
            {
                yield return Count(new(band, parent, inherited, band.KeepWithNext));
                foreach (var child in band.Children) foreach (var item in Emit(child, parent, inherited)) yield return item;
                yield break;
            }
            IReadOnlyList<DataRow> source = band.DataSource is { Length: > 0 } name ? root.Tables[name].Rows : [parent.Row];
            string table = band.DataSource ?? parent.TableName;
            var relation = document.Relations.FirstOrDefault(r => r.ParentTable.Equals(parent.TableName, StringComparison.OrdinalIgnoreCase) && r.ChildTable.Equals(table, StringComparison.OrdinalIgnoreCase));
            string? parentKey = band.ParentKey ?? relation?.ParentColumn, childKey = band.ChildKey ?? relation?.ChildColumn;
            var candidates = new List<(DataRow Row, Value[] Keys, int Index)>();
            var sort = band.Groups.Select(g => new SortKey(g.Expression)).Concat(band.Sort).ToArray();
            int index = 0;
            foreach (var row in source)
            {
                cancellation.ThrowIfCancellationRequested();
                var context = parent with { Row = row, Rows = source, TableName = table, Parent = parent, RowNumber = ++index };
                if (parentKey is not null && childKey is not null && (!row.TryGetValue(childKey, out var childValue) || Value.Compare(childValue, parent.Resolve(parentKey)) != 0)) continue;
                if (!Visible(band.Filter, context, band.Id)) continue;
                candidates.Add((row, sort.Select(s => Evaluate(s.Expression, context, band.Id)).ToArray(), index));
            }
            if (sort.Length > 0) candidates.Sort((a, b) =>
            {
                for (int i = 0; i < sort.Length; i++) { int n = Value.Compare(a.Keys[i], b.Keys[i]); if (n != 0) return sort[i].Descending ? -n : n; }
                return a.Index.CompareTo(b.Index);
            });
            var rows = candidates.Select(x => x.Row).ToArray();
            var baseContext = parent with { TableName = table, Rows = rows, Row = rows.FirstOrDefault() ?? new(), Parent = parent };
            var repeat = inherited.ToList();
            if (band.Header is { } header)
            {
                var head = new Instance(header, baseContext, inherited, true);
                yield return Count(head);
                if (band.RepeatHeader) repeat.Add(head);
            }
            foreach (var item in EmitGroups(band, baseContext, rows, 0, repeat)) yield return item;
            if (band.Footer is { } footer) yield return Count(new(footer, baseContext with { Row = rows.LastOrDefault() ?? new() }, inherited));
        }
        private IEnumerable<Instance> EmitGroups(Band data, EvaluationContext context, IReadOnlyList<DataRow> rows, int depth, IReadOnlyList<Instance> repeat)
        {
            if (depth == data.Groups.Count)
            {
                int index = 0;
                foreach (var row in rows)
                {
                    var c = context with { Row = row, RowNumber = ++index };
                    yield return Count(new(data, c, repeat, data.KeepWithNext));
                    foreach (var child in data.Children) foreach (var instance in Emit(child, c, repeat)) yield return instance;
                }
                yield break;
            }
            var group = data.Groups[depth];
            int start = 0;
            while (start < rows.Count)
            {
                var value = Evaluate(group.Expression, context with { Row = rows[start] }, data.Id);
                int end = start + 1;
                while (end < rows.Count && Value.Compare(value, Evaluate(group.Expression, context with { Row = rows[end] }, data.Id)) == 0) end++;
                var subset = rows.Skip(start).Take(end - start).ToArray();
                var c = context with { Row = subset[0], Rows = subset };
                yield return Count(new(group.Header, c, repeat, group.KeepHeaderWithFirstRow));
                foreach (var instance in EmitGroups(data, c, subset, depth + 1, repeat)) yield return instance;
                yield return Count(new(group.Footer, c with { Row = subset[^1] }, repeat));
                start = end;
            }
        }
        private Instance Count(Instance instance)
        {
            if (++instances > options.MaximumBandInstances) throw Failure("RS2002", "Maximum band instance count exceeded.");
            return instance;
        }

        private Layout Build(Band band, EvaluationContext context, double width)
        {
            if (!Visible(band.VisibleExpression, context, band.Id)) return new(0, []);
            var items = new List<PreparedItem>(); double height = band.Height;
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
                    CheckBoxItem check => Evaluate(check.Expression, context, item.Id).AsBoolean() ? "True" : "False",
                    _ => ""
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
                else if (item is TableItem table)
                {
                    if (!root.Tables.TryGetValue(table.DataSource, out var data)) throw Failure("RS2003", $"Table data source '{table.DataSource}' is not bound.", item.Id);
                    double y = bounds.Y;
                    var columns = table.Columns;
                    double totalWidth = columns.Sum(col => col.Width);
                    void TableRow(DataRow? row, bool header, int number)
                    {
                        double x = bounds.X;
                        foreach (var col in columns)
                        {
                            double w = col.Width / totalWidth * bounds.Width;
                            string value = header ? col.Title : Evaluate(col.Expression, context with { Row = row!, TableName = data.Name, Rows = data.Rows, RowNumber = number, Parent = context }, item.Id).AsString(col.Format, culture);
                            var style = item.Style with { Bold = header || item.Style.Bold, Fill = header ? "#E1EAF4" : number % 2 == 0 ? "#F5F7FA" : "#FFFFFF", BorderWidth = Math.Max(.4, item.Style.BorderWidth) };
                            var cell = new TextItem { Id = item.Id + ":" + number + ":" + columns.IndexOf(col), Name = item.Name, Text = value, Style = style, Bounds = new(x, y, w, table.RowHeight) };
                            AddText(items, cell, cell.Bounds, TextLayout.Wrap(value, w - style.Padding * 2, style, measurer));
                            x += w;
                        }
                        y += table.RowHeight;
                    }
                    if (table.ShowHeader) TableRow(null, true, 0);
                    for (int n = 0; n < data.Rows.Count; n++) { cancellation.ThrowIfCancellationRequested(); TableRow(data.Rows[n], false, n + 1); }
                    bounds = bounds with { Height = y - bounds.Y };
                }
                else if (item is ChartItem chart)
                {
                    if (!root.Tables.TryGetValue(chart.DataSource, out var data)) throw Failure("RS2003", $"Chart data source '{chart.DataSource}' is not bound.", item.Id);
                    if (data.Rows.Count > 10_000) throw Failure("RS2004", "Chart data exceeds 10,000 points; aggregate the data first.", item.Id);
                    var points = data.Rows.Select(row => new ChartPoint(row.TryGetValue(chart.CategoryColumn, out var v) ? v.AsString() : "", Evaluate(chart.ValueExpression, context with { Row = row, TableName = data.Name, Rows = data.Rows }, item.Id).AsNumber())).ToArray();
                    items.Add(new(item, bounds, chart.Title, points));
                }
                else items.Add(new(item, bounds, text));
                height = Math.Max(height, bounds.Bottom);
                if (items.Count > options.MaximumItems) throw Failure("RS2005", "One band exceeds the prepared-item limit.", band.Id);
            }
            if (band.AlternateFill is { } fill && context.RowNumber % 2 == 0)
                items.Insert(0, new(new ShapeItem { Id = band.Id + ":alternate", Bounds = new(0, 0, width, height), Style = new() { Fill = fill } }, new(0, 0, width, height)));
            return new(height, items);
        }
        private void AddText(List<PreparedItem> output, ReportItem item, RectD bounds, IReadOnlyList<string> lines)
        {
            var style = item.Style;
            if (style.BorderWidth > 0 || style.Fill != "#00000000")
                output.Add(new(new ShapeItem { Id = item.Id + ":background", Name = item.Name, Style = style, Bounds = bounds }, bounds));
            double lineHeight = measurer.LineHeight(style), available = bounds.Height - style.Padding * 2;
            double offset = style.VerticalAlignment switch { VerticalAlign.Center => Math.Max(0, (available - lines.Count * lineHeight) / 2), VerticalAlign.Bottom => Math.Max(0, available - lines.Count * lineHeight), _ => 0 };
            for (int i = 0; i < lines.Count; i++)
            {
                var r = new RectD(bounds.X + style.Padding, bounds.Y + style.Padding + offset + i * lineHeight, Math.Max(0.01, bounds.Width - style.Padding * 2), lineHeight);
                if (r.Y >= bounds.Bottom) break;
                var source = item with { Bounds = r, Bookmark = i == 0 ? item.Bookmark : null, Style = style with { Padding = 0, BorderWidth = 0, Fill = "#00000000", VerticalAlignment = VerticalAlign.Top } };
                output.Add(new(source, r, lines[i], Clip: bounds));
            }
        }

        private void Paginate(ReportPage template)
        {
            var pageHeaders = template.Bands.Where(b => b.Kind == BandKind.PageHeader).ToArray();
            var pageFooters = template.Bands.Where(b => b.Kind == BandKind.PageFooter).ToArray();
            var columnHeaders = template.Bands.Where(b => b.Kind == BandKind.ColumnHeader).ToArray();
            var columnFooters = template.Bands.Where(b => b.Kind == BandKind.ColumnFooter).ToArray();
            var overlays = template.Bands.Where(b => b.Kind == BandKind.Overlay).ToArray();
            var content = template.Bands.Where(b => b.Kind is not (BandKind.PageHeader or BandKind.PageFooter or BandKind.ColumnHeader or BandKind.ColumnFooter or BandKind.Overlay))
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
                    var bounds = item.Bounds.Offset(x, at - consumed);
                    var clip = item.Clip?.Offset(x, at - consumed);
                    if (sliceHeight is not null)
                    {
                        if (!bounds.Intersects(slice)) continue;
                        clip = clip is { } existing ? Intersection(existing, slice) : slice;
                    }
                    output.Add(item with { Bounds = bounds, Clip = clip });
                    if (++itemCount > options.MaximumItems) throw Failure("RS2005", "Maximum prepared-item count exceeded.");
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
                if ((instance.KeepWithNext || instance.Band.KeepWithNext) && has)
                {
                    var next = content.Current;
                    keepHeight += Math.Min(Build(next.Band, Context(next.Context), template.ColumnWidth).Height, bottom - top);
                }
                if (columnHasContent && y + keepHeight > bottom + .001)
                { NextColumn(instance.Repeat); layout = Build(instance.Band, Context(instance.Context), template.ColumnWidth); }
                if (!instance.Band.CanSplit && layout.Height > bottom - top + .001)
                    throw Failure("RS2008", $"Band '{instance.Band.Name}' is taller than a printable column and cannot split.", instance.Band.Id);
                if (layout.Height <= .001) continue;
                if (layout.Height <= bottom - y + .001)
                { Place(layout, X(), y); y += layout.Height; columnHasContent = true; continue; }
                if (!instance.Band.CanSplit)
                { NextColumn(instance.Repeat); layout = Build(instance.Band, Context(instance.Context), template.ColumnWidth); Place(layout, X(), y); y += layout.Height; columnHasContent = true; continue; }
                double consumed = 0;
                while (consumed < layout.Height - .001)
                {
                    double available = bottom - y;
                    if (available < .01) { NextColumn(instance.Repeat); available = bottom - y; }
                    double amount = Math.Min(available, layout.Height - consumed);
                    // Keep an individual text line on one page when it fits on a fresh column.
                    var crossing = layout.Items.Where(i => i.Source is TextItem && i.Bounds.Y < consumed + amount && i.Bounds.Bottom > consumed + amount + .001)
                        .Select(i => i.Bounds.Y - consumed).Where(v => v > .01).DefaultIfEmpty(amount).Min();
                    amount = Math.Min(amount, crossing);
                    if (amount < .01) throw Failure("RS2009", "Pagination made no forward progress.", instance.Band.Id);
                    Place(layout, X(), y, consumed, amount); y += amount; consumed += amount; columnHasContent = true;
                    if (consumed < layout.Height - .001) NextColumn(instance.Repeat);
                }
            }
            content.Dispose();
        }
        private static RectD Intersection(RectD a, RectD b)
        {
            double x = Math.Max(a.X, b.X), y = Math.Max(a.Y, b.Y);
            return new(x, y, Math.Max(0, Math.Min(a.Right, b.Right) - x), Math.Max(0, Math.Min(a.Bottom, b.Bottom) - y));
        }
    }
}
