using System.Globalization;
using System.Text;
using System.Text.Json;
using ReportScene.Model;

namespace ReportScene.Data;

public sealed record DataReadOptions(int MaximumRows = 100_000, int MaximumColumns = 1024, int MaximumCharacters = 32_000_000, bool InferNumbers = false);
public interface IDataProvider
{
    string Name { get; }
    ValueTask<DataTable> ReadAsync(CancellationToken cancellationToken = default);
}
public sealed class InMemoryDataProvider(DataTable table) : IDataProvider
{
    public string Name => table.Name;
    public ValueTask<DataTable> ReadAsync(CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(table); }
}

public static class DataReaders
{
    public static DataTable Json(string text, string name = "Data", DataReadOptions? options = null)
    {
        options ??= new(); CheckLength(text, options);
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new FormatException("JSON data must be an array of objects.");
        var table = new DataTable { Name = name };
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in document.RootElement.EnumerateArray())
        {
            if (record.ValueKind != JsonValueKind.Object) throw new FormatException("Each JSON data row must be an object.");
            var row = new DataRow();
            foreach (var cell in record.EnumerateObject())
            {
                if (row.ContainsKey(cell.Name)) throw new FormatException($"Duplicate column '{cell.Name}'.");
                row.Add(cell.Name, cell.Value.ValueKind switch
                {
                    JsonValueKind.Null => Value.Null, JsonValueKind.True => true, JsonValueKind.False => false,
                    JsonValueKind.Number => Value.From(cell.Value.GetDecimal()), JsonValueKind.String => Value.From(cell.Value.GetString()),
                    _ => throw new FormatException($"Nested data in '{cell.Name}' must be normalized into a related table.")
                });
                if (columns.Add(cell.Name)) table.Columns.Add(cell.Name);
                if (columns.Count > options.MaximumColumns) throw new FormatException("Maximum column count exceeded.");
            }
            table.Rows.Add(row);
            if (table.Rows.Count > options.MaximumRows) throw new FormatException("Maximum row count exceeded.");
        }
        foreach (var row in table.Rows) foreach (var column in table.Columns) row.TryAdd(column, Value.Null);
        return table;
    }

    public static DataTable Csv(string text, string name = "Data", char delimiter = ',', DataReadOptions? options = null)
    {
        options ??= new(); CheckLength(text, options);
        if (delimiter is '\r' or '\n' or '"') throw new ArgumentException("Invalid CSV delimiter.");
        var records = ParseCsv(text.TrimStart('\uFEFF'), delimiter, options);
        if (records.Count == 0) return new() { Name = name };
        var headers = records[0];
        if (headers.Any(string.IsNullOrWhiteSpace) || headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
            throw new FormatException("CSV headers must be nonempty and unique.");
        var table = new DataTable { Name = name, Columns = headers.ToList() };
        for (int r = 1; r < records.Count; r++)
        {
            if (records[r].Length != headers.Length) throw new FormatException($"CSV row {r + 1} has {records[r].Length} fields; expected {headers.Length}.");
            var row = new DataRow();
            for (int c = 0; c < headers.Length; c++)
            {
                string cell = records[r][c];
                row.Add(headers[c], options.InferNumbers && decimal.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? Value.From(n) : Value.From(cell));
            }
            table.Rows.Add(row);
        }
        return table;
    }
    private static List<string[]> ParseCsv(string text, char delimiter, DataReadOptions options)
    {
        var records = new List<string[]>(); var fields = new List<string>(); var cell = new StringBuilder();
        bool quoted = false, afterQuote = false, any = false;
        void Field() { fields.Add(cell.ToString()); cell.Clear(); afterQuote = false; if (fields.Count > options.MaximumColumns) throw new FormatException("Maximum column count exceeded."); }
        void Row() { Field(); records.Add(fields.ToArray()); fields.Clear(); any = false; if (records.Count > options.MaximumRows + 1) throw new FormatException("Maximum row count exceeded."); }
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i]; any = true;
            if (quoted)
            {
                if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else { quoted = false; afterQuote = true; } }
                else cell.Append(c);
                continue;
            }
            if (c == delimiter) Field();
            else if (c is '\r' or '\n') { if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; Row(); }
            else if (afterQuote) throw new FormatException("Unexpected character after a quoted CSV field.");
            else if (c == '"') { if (cell.Length > 0) throw new FormatException("Quote inside an unquoted CSV field."); quoted = true; }
            else cell.Append(c);
        }
        if (quoted) throw new FormatException("Unclosed CSV quoted field.");
        if (any || fields.Count > 0 || cell.Length > 0) Row();
        return records;
    }
    private static void CheckLength(string text, DataReadOptions options)
    { if (text.Length > options.MaximumCharacters) throw new FormatException("Data input exceeds the configured size limit."); }

    public static string WriteCsv(DataTable table, bool protectSpreadsheetFormulas = true)
    {
        string Escape(string s)
        {
            if (protectSpreadsheetFormulas && s.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@') s = "'" + s;
            return s.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
        var output = new StringBuilder().AppendLine(string.Join(',', table.Columns.Select(Escape)));
        foreach (var row in table.Rows)
            output.AppendLine(string.Join(',', table.Columns.Select(c => row.TryGetValue(c, out var v)
                ? v.Kind == ValueKind.Number ? v.AsString() : Escape(v.AsString()) : "")));
        return output.ToString();
    }
}
