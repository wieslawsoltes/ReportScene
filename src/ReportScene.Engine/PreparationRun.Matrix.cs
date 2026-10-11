using ReportScene.Expressions;
using ReportScene.Matrix;
using ReportScene.Model;

namespace ReportScene.Engine;

internal sealed partial class PreparationRun
{
    private sealed record MatrixCell(double X, double Width, string Text, ItemStyle Style, string Kind, int Column);

    private Layout BuildMatrix(MatrixItem matrix, EvaluationContext context, double width)
    {
        if (!root.Tables.TryGetValue(matrix.DataSource, out var data)) throw Failure("RS1401", $"Matrix source '{matrix.DataSource}' is not bound.", matrix.Id);
        IReadOnlyList<DataRow> source = data.Rows;
        if (data.Name.Equals(context.TableName, StringComparison.OrdinalIgnoreCase)) source = context.Rows;
        else
        {
            var relation = document.Relations.FirstOrDefault(r => r.ParentTable.Equals(context.TableName, StringComparison.OrdinalIgnoreCase) && r.ChildTable.Equals(data.Name, StringComparison.OrdinalIgnoreCase));
            if (relation is not null)
            {
                var key = context.Resolve(relation.ParentColumn); var related = new List<DataRow>();
                foreach (var row in source)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (row.TryGetValue(relation.ChildColumn, out var value) && Value.Compare(value, key) == 0) related.Add(row);
                }
                source = related;
            }
        }
        try
        {
            var result = new MatrixEngine().BuildIndexed(matrix, source,
                (row, number, expression) => Evaluate(expression, context with { Row = row, RowNumber = number, Rows = source, TableName = data.Name, Parent = context }, matrix.Id),
                new(MaximumOutputValues: options.MaximumItems, MaximumAccumulatorValues: Math.Min(1_000_000, options.MaximumItems)), cancellation);
            return LayoutMatrix(matrix, result, width);
        }
        catch (MatrixException e) { throw Failure("RS1401", e.Message, matrix.Id); }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentException)
        { throw Failure("RS1402", "Matrix value or layout error: " + e.Message, matrix.Id); }
    }

    private Layout LayoutMatrix(MatrixItem matrix, MatrixResult result, double width)
    {
        var items = new List<PreparedItem>(); var sections = new List<FlowSection>();
        double usable = Math.Min(matrix.Bounds.Width, width - matrix.Bounds.X);
        int count = checked(result.Columns.Count * result.Measures.Count);
        var normal = matrix.Style with { BorderWidth = Math.Max(.4, matrix.Style.BorderWidth), VerticalAlignment = VerticalAlign.Center };
        var heading = normal with { Bold = true, Fill = "#E1EAF4", Alignment = TextAlign.Center };
        var total = normal with { Bold = true, Fill = "#D9E4EF" };
        if (result.Rows.Count == 0 || count == 0)
        {
            var bounds = matrix.Bounds with { Height = matrix.RowHeight };
            var item = new TextItem { Id = matrix.Id + ":empty", Name = matrix.Name + ".Empty", Text = "No data", Style = normal, Bounds = bounds };
            AddText(items, item, bounds, TextLayout.Wrap(item.Text, bounds.Width - 2 * normal.Padding, normal, measurer));
            return new(matrix.RowHeight, items);
        }
        double slots = Math.Floor((usable - matrix.RowHeaderWidth) / matrix.CellWidth + .000001);
        if (slots < 1) throw Failure("RS1403", "The matrix row header and at least one value column must fit inside its width and printable column.", matrix.Id);
        int perPanel = (int)Math.Min(count, slots);
        double y = matrix.Bounds.Y;
        int rowLevels = Math.Max(1, matrix.RowDimensions.Count), columnLevels = Math.Max(1, matrix.ColumnDimensions.Count);
        double rowLabelWidth = matrix.RowHeaderWidth / rowLevels;
        string DimensionTitle(MatrixDimension d) => string.IsNullOrWhiteSpace(d.Title) ? d.Expression : d.Title;
        string corner = matrix.RowDimensions.Count == 0 ? "Values" : string.Join(" / ", matrix.RowDimensions.Select(DimensionTitle));
        string AxisLabel(MatrixAxisEntry entry, int level, bool emptyAxis)
        {
            if (entry.Kind == MatrixAxisKind.GrandTotal) return level == 0 ? matrix.GrandTotalLabel : "";
            if (emptyAxis) return level == 0 ? "Values" : "";
            if (level < entry.Values.Count) return entry.Values[level].IsNull ? matrix.NullLabel : entry.Values[level].AsString(culture: culture);
            return entry.Kind == MatrixAxisKind.Subtotal && level == entry.Values.Count ? matrix.TotalLabel : "";
        }
        bool SameHeader(int a, int b, int level)
        {
            var left = result.Columns[a / result.Measures.Count]; var right = result.Columns[b / result.Measures.Count];
            if (left.Kind != right.Kind || left.Depth != right.Depth) return false;
            int prefix = Math.Min(level + 1, left.Depth);
            for (int i = 0; i < prefix; i++) if (left.Values[i] != right.Values[i]) return false;
            return true;
        }
        double TextHeight(string text, double w, ItemStyle style) => TextLayout.Wrap(text, Math.Max(.01, w - 2 * style.Padding), style, measurer).Count * measurer.LineHeight(style) + style.Padding * 2;
        void Cell(MatrixCell plan, double at, double height, int panel, int row, string? bookmark = null)
        {
            var bounds = new RectD(plan.X, at, plan.Width, height);
            var item = new TextItem
            {
                Id = $"{matrix.Id}:p{panel}:r{row}:{plan.Kind}:c{plan.Column}", Name = matrix.Name + "." + plan.Kind,
                Text = plan.Text, Bounds = bounds, Style = plan.Style, Bookmark = bookmark, Hyperlink = matrix.Hyperlink
            };
            AddText(items, item, bounds, TextLayout.Wrap(plan.Text, Math.Max(.01, bounds.Width - 2 * plan.Style.Padding), plan.Style, measurer));
        }
        int panelNumber = 0;
        for (int first = 0; first < count; first += perPanel)
        {
            cancellation.ThrowIfCancellationRequested();
            int end = Math.Min(count, first + perPanel), panel = panelNumber++;
            double sectionTop = y; int headerStart = items.Count;
            var headerRows = new List<List<MatrixCell>>(); var headerHeights = new List<double>();
            for (int level = 0; level < columnLevels; level++)
            {
                var plans = new List<MatrixCell>();
                int current = first;
                while (current < end)
                {
                    int last = current + 1;
                    while (last < end && SameHeader(current, last, level)) last++;
                    var entry = result.Columns[current / result.Measures.Count];
                    plans.Add(new(matrix.Bounds.X + matrix.RowHeaderWidth + (current - first) * matrix.CellWidth,
                        (last - current) * matrix.CellWidth, AxisLabel(entry, level, matrix.ColumnDimensions.Count == 0), heading, "ColumnHeader", current));
                    current = last;
                }
                headerRows.Add(plans); headerHeights.Add(Math.Max(matrix.RowHeight, plans.Max(c => TextHeight(c.Text, c.Width, c.Style))));
            }
            var measureHeaders = new List<MatrixCell>();
            for (int column = first; column < end; column++)
            {
                var measure = result.Measures[column % result.Measures.Count];
                string title = string.IsNullOrWhiteSpace(measure.Title) ? measure.Aggregate + "(" + measure.Expression + ")" : measure.Title;
                measureHeaders.Add(new(matrix.Bounds.X + matrix.RowHeaderWidth + (column - first) * matrix.CellWidth, matrix.CellWidth, title, heading, "MeasureHeader", column));
            }
            headerRows.Add(measureHeaders); headerHeights.Add(Math.Max(matrix.RowHeight, measureHeaders.Max(c => TextHeight(c.Text, c.Width, c.Style))));
            double headerHeight = headerHeights.Sum();
            double cornerHeight = TextHeight(corner, matrix.RowHeaderWidth, heading);
            if (cornerHeight > headerHeight) { headerHeights[^1] += cornerHeight - headerHeight; headerHeight = cornerHeight; }
            Cell(new(matrix.Bounds.X, matrix.RowHeaderWidth, corner, heading, "Corner", -1), y, headerHeight, panel, -1, panel == 0 ? matrix.Bookmark : null);
            for (int level = 0; level < headerRows.Count; level++)
            {
                foreach (var cell in headerRows[level]) Cell(cell, y, headerHeights[level], panel, -2 - level);
                y += headerHeights[level];
            }
            var headers = items.Skip(headerStart).Select(i => i with
            {
                Bounds = i.Bounds.Offset(0, -sectionTop), Clip = i.Clip?.Offset(0, -sectionTop),
                Source = i.Source with { Bookmark = null }
            }).ToList();
            var rowEnds = new List<double>();
            for (int row = 0; row < result.Rows.Count; row++)
            {
                cancellation.ThrowIfCancellationRequested();
                var entry = result.Rows[row]; var plans = new List<MatrixCell>();
                bool isTotal = entry.Kind != MatrixAxisKind.Leaf;
                var rowStyle = isTotal ? total : normal with { Fill = matrix.Style.Fill == "#00000000" ? row % 2 == 0 ? "#FFFFFF" : "#F5F7FA" : matrix.Style.Fill };
                for (int level = 0; level < rowLevels; level++)
                    plans.Add(new(matrix.Bounds.X + level * rowLabelWidth, rowLabelWidth, AxisLabel(entry, level, matrix.RowDimensions.Count == 0), rowStyle with { Alignment = TextAlign.Left }, "RowHeader", level));
                for (int column = first; column < end; column++)
                {
                    int c = column / result.Measures.Count, m = column % result.Measures.Count;
                    var value = result.GetValue(row, c, m); var measure = result.Measures[m];
                    string? format = measure.Format ?? (measure.Display == MatrixDisplay.Value ? null : "P2");
                    string text = value.AsString(format, culture);
                    var style = (isTotal || result.Columns[c].Kind != MatrixAxisKind.Leaf ? total : rowStyle) with { Alignment = value.Kind == ValueKind.Number ? TextAlign.Right : matrix.Style.Alignment };
                    plans.Add(new(matrix.Bounds.X + matrix.RowHeaderWidth + (column - first) * matrix.CellWidth, matrix.CellWidth, text, style, "Value", column));
                }
                double rowHeight = Math.Max(matrix.RowHeight, plans.Max(c => TextHeight(c.Text, c.Width, c.Style)));
                foreach (var cell in plans) Cell(cell, y, rowHeight, panel, row);
                y += rowHeight; rowEnds.Add(y);
            }
            sections.Add(new(sectionTop, y, headerHeight, rowEnds.ToArray(), headers, matrix.RepeatHeaders, panel > 0));
        }
        return new(y - matrix.Bounds.Y, items, sections);
    }
}
