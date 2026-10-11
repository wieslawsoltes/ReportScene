using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReportScene.Model;

namespace ReportScene.Controls.Uno;

/// <summary>A bounded, read-only typed data preview. Preview limits do not truncate the bound data.</summary>
public sealed class DataTablePreview : UserControl
{
    public int MaximumPreviewRows { get; }
    public int MaximumPreviewColumns { get; }
    public DataTablePreview(int maximumPreviewRows = 20, int maximumPreviewColumns = 12)
    {
        if (maximumPreviewRows is < 1 or > 200 || maximumPreviewColumns is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(maximumPreviewRows));
        MaximumPreviewRows = maximumPreviewRows; MaximumPreviewColumns = maximumPreviewColumns;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
    }
    public void SetTable(DataTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var outer = new StackPanel { Spacing = 5 };
        outer.Children.Add(ControlTheme.Label($"{table.Name}: {table.Rows.Count:N0} rows × {table.Columns.Count:N0} columns. Showing up to {MaximumPreviewRows} rows and {MaximumPreviewColumns} columns; import retains all rows.", 11));
        var rows = new StackPanel(); var columns = table.Columns.Take(MaximumPreviewColumns).ToArray();
        double[] widths = columns.Select(c => Math.Clamp(Math.Max(c.Length, table.Rows.Take(MaximumPreviewRows).Select(r => r.TryGetValue(c, out var v) ? v.AsString().Length : 4).DefaultIfEmpty(0).Max()) * 7d + 16, 85, 220)).ToArray();
        Grid Row(int index)
        {
            var grid = new Grid();
            for (int n = 0; n < columns.Length; n++)
            {
                grid.ColumnDefinitions.Add(new() { Width = new(widths[n]) });
                var value = index < 0 ? Value.From(columns[n]) : table.Rows[index].TryGetValue(columns[n], out var found) ? found : Value.Null;
                var label = ControlTheme.Label(value.IsNull ? "(null)" : value.AsString(), 11, index < 0);
                label.Margin = new(7, 4, 7, 4); label.TextTrimming = TextTrimming.CharacterEllipsis;
                if (value.IsNull) label.Foreground = ControlTheme.Brush("#7D8791");
                var cell = new Border { Child = label, BorderThickness = new(0, 0, 1, 1), BorderBrush = ControlTheme.Brush("#CDD5DC"), Background = ControlTheme.Brush(index < 0 ? "#E1EAF4" : index % 2 == 0 ? "#FFFFFF" : "#F5F7FA") };
                Grid.SetColumn(cell, n); grid.Children.Add(cell);
            }
            return grid;
        }
        rows.Children.Add(Row(-1));
        for (int r = 0; r < Math.Min(table.Rows.Count, MaximumPreviewRows); r++) rows.Children.Add(Row(r));
        outer.Children.Add(new ScrollViewer { Content = rows, Height = 230, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = outer;
    }
}
