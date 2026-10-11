using System.Globalization;
using System.Text.RegularExpressions;

namespace ReportScene.Model;

public static partial class DocumentValidator
{
    [GeneratedRegex("^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$")]
    private static partial Regex ColorPattern();
    public static IReadOnlyList<Diagnostic> Validate(ReportDocument document)
    {
        var errors = new List<Diagnostic>(); var ids = new HashSet<string>(StringComparer.Ordinal);
        void Error(string message, string? id = null) => errors.Add(new("RS1001", message, ObjectId: id));
        bool Positive(double value) => double.IsFinite(value) && value > 0;
        bool Nonnegative(double value) => double.IsFinite(value) && value >= 0;
        if (document.FormatVersion != 1) Error($"Unsupported report schema version {document.FormatVersion}.");
        if (document.Pages.Count == 0) Error("A report needs at least one page.");
        if (document.Pages.Count > 256) Error("A report may contain at most 256 page templates.");
        if (document.Pages.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != document.Pages.Count) Error("Page names must be unique.");
        if (document.Data.Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != document.Data.Count) Error("Data table names must be unique.");
        if (document.Parameters.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != document.Parameters.Count) Error("Parameter names must be unique.");
        try { _ = CultureInfo.GetCultureInfo(document.Culture); } catch (CultureNotFoundException) { Error("Invalid report culture."); }
        var visited = new HashSet<Band>(ReferenceEqualityComparer.Instance);
        void BandCheck(Band band, int depth)
        {
            if (depth > 32 || !visited.Add(band)) { Error("Band graph contains a cycle, shared node or excessive nesting.", band.Id); return; }
            if (!ids.Add(band.Id) || string.IsNullOrWhiteSpace(band.Id)) Error("Band/object IDs must be nonempty and unique.", band.Id);
            if (!Nonnegative(band.Height)) Error("Band height must be finite and nonnegative.", band.Id);
            if ((band.ParentKey is null) != (band.ChildKey is null)) Error("A detail band needs both ParentKey and ChildKey.", band.Id);
            if (band.DataSource is { Length: > 0 } source && !document.Data.Any(t => t.Name.Equals(source, StringComparison.OrdinalIgnoreCase))) Error($"Data source '{source}' is not bound.", band.Id);
            foreach (var item in band.Items)
            {
                if (!ids.Add(item.Id) || string.IsNullOrWhiteSpace(item.Id)) Error("Band/object IDs must be nonempty and unique.", item.Id);
                var b = item.Bounds;
                if (!Nonnegative(b.X) || !Nonnegative(b.Y) || !Positive(b.Width) || !Positive(b.Height)) Error("Object bounds must be finite, nonnegative and nonempty.", item.Id);
                var s = item.Style;
                if (!Positive(s.FontSize) || !Positive(s.LineHeight) || !Nonnegative(s.Padding) || !Nonnegative(s.BorderWidth)) Error("Invalid text/border metrics.", item.Id);
                if (!ColorPattern().IsMatch(s.Foreground) || !ColorPattern().IsMatch(s.Fill) || !ColorPattern().IsMatch(s.BorderColor)) Error("Colors must be #RRGGBB or #AARRGGBB.", item.Id);
                if (item is TableItem table && (!Positive(table.RowHeight) || table.Columns.Count == 0 || table.Columns.Any(c => !Positive(c.Width)))) Error("A table needs positive row heights and column widths.", item.Id);
                if (item is MatrixItem matrix)
                {
                    errors.AddRange(MatrixDefinitionValidator.Validate(matrix));
                    if (!document.Data.Any(t => t.Name.Equals(matrix.DataSource, StringComparison.OrdinalIgnoreCase))) Error($"Matrix data source '{matrix.DataSource}' is not bound.", item.Id);
                }
                if (item is ImageItem image && image.Data.Length > 16_000_000) Error("Embedded image exceeds 16 MB.", item.Id);
            }
            if (band.Header is not null) BandCheck(band.Header, depth + 1);
            foreach (var group in band.Groups) { BandCheck(group.Header, depth + 1); BandCheck(group.Footer, depth + 1); }
            foreach (var child in band.Children) BandCheck(child, depth + 1);
            if (band.Footer is not null) BandCheck(band.Footer, depth + 1);
        }
        foreach (var page in document.Pages)
        {
            var m = page.Margins;
            if (!Positive(page.Width) || !Positive(page.Height) || !Nonnegative(m.Left) || !Nonnegative(m.Right) || !Nonnegative(m.Top) || !Nonnegative(m.Bottom)
                || !Positive(page.ContentWidth) || page.Height <= m.Top + m.Bottom || page.Columns < 1 || page.Columns > 64 || !Nonnegative(page.ColumnGap) || !Positive(page.ColumnWidth))
                Error($"Page '{page.Name}' has invalid paper, margin or column dimensions.");
            foreach (var band in page.Bands) BandCheck(band, 0);
        }
        foreach (var table in document.Data)
        {
            if (table.Columns.Distinct(StringComparer.OrdinalIgnoreCase).Count() != table.Columns.Count) Error($"Table '{table.Name}' has duplicate columns.");
            if (table.Rows.Count > 1_000_000) Error($"Table '{table.Name}' exceeds one million rows.");
        }
        return errors;
    }
}
