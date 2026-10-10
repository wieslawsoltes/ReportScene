using ReportScene.Model;
using SkiaSharp;

namespace ReportScene.Rendering.Skia;

/// <summary>Expression-based authoring representation; actual source values are only shown after preparation.</summary>
public static class MatrixDesignRenderer
{
    public static void Draw(SKCanvas canvas, MatrixItem matrix, RectD bounds, ReportRenderer renderer)
    {
        canvas.Save();
        try
        {
            canvas.ClipRect(ReportRenderer.Rect(bounds));
            double rowHeight = Math.Max(18, matrix.RowHeight);
            double rowWidth = Math.Min(matrix.RowHeaderWidth, bounds.Width * .5);
            int levels = Math.Max(1, matrix.ColumnDimensions.Count);
            double columnWidth = Math.Max(35, Math.Min(matrix.CellWidth, bounds.Width - rowWidth));
            int columns = Math.Max(1, Math.Min(6, (int)Math.Ceiling((bounds.Width - rowWidth) / columnWidth)));
            var normal = matrix.Style with { FontSize = Math.Min(10, matrix.Style.FontSize), BorderWidth = .5, BorderColor = "#AEBCC8", Fill = "#FFFFFF", VerticalAlignment = VerticalAlign.Center };
            var header = normal with { Bold = true, Fill = "#E1EAF4", Alignment = TextAlign.Center };
            void Cell(string text, RectD rect, ItemStyle style)
            {
                using var fill = new SKPaint { Color = ReportRenderer.Color(style.Fill) }; canvas.DrawRect(ReportRenderer.Rect(rect), fill);
                using var border = new SKPaint { Color = ReportRenderer.Color(style.BorderColor), Style = SKPaintStyle.Stroke, StrokeWidth = .5f }; canvas.DrawRect(ReportRenderer.Rect(rect), border);
                renderer.DrawText(canvas, text, rect, style);
            }
            string rowLabel = matrix.RowDimensions.Count == 0 ? "Values" : string.Join(" / ", matrix.RowDimensions.Select(d => "[" + d.Expression + "]"));
            Cell(rowLabel, new(bounds.X, bounds.Y, rowWidth, (levels + 1) * rowHeight), header);
            for (int c = 0; c < columns; c++)
            {
                double x = bounds.X + rowWidth + c * columnWidth;
                for (int level = 0; level < levels; level++)
                {
                    string label = matrix.ColumnDimensions.Count == 0 ? "Values" : "[" + matrix.ColumnDimensions[level].Expression + "]";
                    if (matrix.ShowColumnGrandTotal && c == columns - 1) label = level == 0 ? matrix.GrandTotalLabel : "";
                    Cell(label, new(x, bounds.Y + level * rowHeight, columnWidth, rowHeight), header);
                }
                var measure = matrix.Measures.Count == 0 ? new MatrixMeasure() : matrix.Measures[c % matrix.Measures.Count];
                Cell(measure.Title, new(x, bounds.Y + levels * rowHeight, columnWidth, rowHeight), header);
                for (int r = 0; r < 3; r++)
                {
                    bool total = matrix.ShowRowGrandTotal && r == 2;
                    string expression = measure.Aggregate == MatrixAggregate.CountRows ? "CountRows()" : measure.Aggregate + "([" + measure.Expression + "])";
                    Cell(expression, new(x, bounds.Y + (levels + 1 + r) * rowHeight, columnWidth, rowHeight), normal with { Fill = total ? "#D9E4EF" : "#FFFFFF", Bold = total });
                }
            }
            for (int r = 0; r < 3; r++)
            {
                bool total = matrix.ShowRowGrandTotal && r == 2;
                Cell(total ? matrix.GrandTotalLabel : rowLabel, new(bounds.X, bounds.Y + (levels + 1 + r) * rowHeight, rowWidth, rowHeight), normal with { Bold = total, Fill = total ? "#D9E4EF" : "#F5F7FA" });
            }
        }
        finally { canvas.Restore(); }
    }
}
