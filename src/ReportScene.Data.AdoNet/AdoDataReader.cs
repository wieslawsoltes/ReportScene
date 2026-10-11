using System.Data.Common;
using System.Globalization;
using ReportScene.Model;
using ReportTable = ReportScene.Model.DataTable;

namespace ReportScene.Data.AdoNet;

public sealed record AdoDataOptions(int MaximumRows = 100_000, int MaximumColumns = 1024,
    int MaximumCells = 1_000_000, int MaximumCharacters = 32_000_000, int MaximumBinaryBytes = 16_000_000)
{
    internal void Validate()
    {
        if (MaximumRows < 1 || MaximumColumns < 1 || MaximumCells < 1 || MaximumCharacters < 1 || MaximumBinaryBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(AdoDataOptions), "Materialization budgets must be positive; the binary allowance may be zero.");
    }
}
public sealed record ReportDataSet(IReadOnlyList<ReportTable> Tables, IReadOnlyList<DataRelation> Relations);

/// <summary>Provider-independent snapshots. Reader methods consume only the current result set and leave the reader open.</summary>
public static class AdoDataReader
{
    public static ReportTable Read(DbDataReader reader, string name = "Data", AdoDataOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader); cancellationToken.ThrowIfCancellationRequested();
        var buffer = new Buffer(reader, name, options ?? new());
        while (reader.Read()) { cancellationToken.ThrowIfCancellationRequested(); buffer.Add(reader, cancellationToken); }
        cancellationToken.ThrowIfCancellationRequested(); return buffer.Table;
    }
    public static async ValueTask<ReportTable> ReadAsync(DbDataReader reader, string name = "Data", AdoDataOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader); cancellationToken.ThrowIfCancellationRequested();
        var buffer = new Buffer(reader, name, options ?? new());
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        { cancellationToken.ThrowIfCancellationRequested(); buffer.Add(reader, cancellationToken); }
        cancellationToken.ThrowIfCancellationRequested(); return buffer.Table;
    }
    public static ReportTable FromDataTable(System.Data.DataTable table, string? name = null, AdoDataOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table); cancellationToken.ThrowIfCancellationRequested();
        using var reader = table.CreateDataReader(); return Read(reader, name ?? table.TableName, options, cancellationToken);
    }
    /// <summary>Single-column relations map directly to the report model. Composite relations are diagnosed, never partially copied.</summary>
    public static ReportDataSet FromDataSet(System.Data.DataSet source, AdoDataOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source); cancellationToken.ThrowIfCancellationRequested(); options ??= new(); options.Validate();
        var input = source.Tables.Cast<System.Data.DataTable>().ToArray();
        if (input.Select(t => t.TableName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != input.Length)
            throw new FormatException("Report data table names must be unique, irrespective of ADO.NET namespaces.");
        var relations = new List<DataRelation>();
        foreach (System.Data.DataRelation relation in source.Relations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (relation.ParentColumns.Length != 1 || relation.ChildColumns.Length != 1)
                throw new NotSupportedException($"Composite relation '{relation.RelationName}' cannot be represented by the report model. Materialize a single composite key first.");
            relations.Add(new(relation.ParentTable.TableName, relation.ParentColumns[0].ColumnName, relation.ChildTable.TableName, relation.ChildColumns[0].ColumnName));
        }
        var result = new List<ReportTable>(); long rows = 0, cells = 0, characters = 0;
        foreach (var table in input)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var converted = FromDataTable(table, options: options, cancellationToken: cancellationToken);
            rows += converted.Rows.Count; cells += (long)converted.Rows.Count * converted.Columns.Count;
            characters += converted.Rows.Sum(r => r.Values.Sum(v => (long)(v.Text?.Length ?? 0)));
            if (rows > options.MaximumRows || cells > options.MaximumCells || characters > options.MaximumCharacters)
                throw new FormatException("The combined DataSet exceeds its materialization limit.");
            result.Add(converted);
        }
        cancellationToken.ThrowIfCancellationRequested(); return new(result.AsReadOnly(), relations.AsReadOnly());
    }
    private sealed class Buffer
    {
        public ReportTable Table { get; }
        private readonly AdoDataOptions options;
        private long characters;
        public Buffer(DbDataReader reader, string name, AdoDataOptions options)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name); this.options = options; options.Validate();
            if (reader.FieldCount > options.MaximumColumns) throw new FormatException("ADO.NET column limit exceeded.");
            var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
            if (columns.Any(string.IsNullOrWhiteSpace) || columns.Distinct(StringComparer.OrdinalIgnoreCase).Count() != columns.Count)
                throw new FormatException("ADO.NET result column names must be nonempty and unique. Alias duplicate SQL columns explicitly.");
            Table = new() { Name = name, Columns = columns };
        }
        public void Add(DbDataReader reader, CancellationToken cancellation)
        {
            if (Table.Rows.Count >= options.MaximumRows || (long)(Table.Rows.Count + 1) * Table.Columns.Count > options.MaximumCells)
                throw new FormatException("ADO.NET row or cell limit exceeded.");
            var row = new DataRow();
            for (int i = 0; i < Table.Columns.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var value = ConvertValue(reader.GetValue(i), options.MaximumBinaryBytes);
                characters += value.Text?.Length ?? 0;
                if (characters > options.MaximumCharacters) throw new FormatException("ADO.NET character limit exceeded.");
                row.Add(Table.Columns[i], value);
            }
            Table.Rows.Add(row);
        }
    }
    public static Value ConvertValue(object? value, int maximumBinaryBytes = 16_000_000)
    {
        if (maximumBinaryBytes < 0) throw new ArgumentOutOfRangeException(nameof(maximumBinaryBytes));
        return value switch
        {
            null or DBNull => Value.Null,
            string text => text, char character => character.ToString(), bool boolean => boolean,
            decimal number => number,
            byte n => (decimal)n, sbyte n => (decimal)n, short n => (decimal)n, ushort n => (decimal)n,
            int n => (decimal)n, uint n => (decimal)n, long n => (decimal)n, ulong n => (decimal)n,
            float n => Floating(n), double n => Floating(n),
            DateTime date => Value.From(date), DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeOnly time => time.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
            TimeSpan time => time.ToString("c", CultureInfo.InvariantCulture), Guid guid => guid.ToString("D"),
            byte[] bytes when bytes.Length <= maximumBinaryBytes => Convert.ToBase64String(bytes),
            byte[] => throw new FormatException("ADO.NET binary field limit exceeded."),
            System.Data.SqlTypes.INullable sql when sql.IsNull => Value.Null,
            _ => throw new NotSupportedException($"ADO.NET value type '{value.GetType().FullName}' is not a report scalar. Convert it explicitly in the query or adapter.")
        };
    }
    private static Value Floating(double value)
    {
        if (!double.IsFinite(value) || !decimal.TryParse(value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var result) || (double)result != value)
            throw new FormatException("Floating-point value cannot round-trip through the decimal report model.");
        return result;
    }
    private static Value Floating(float value)
    {
        if (!float.IsFinite(value) || !decimal.TryParse(value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var result) || (float)result != value)
            throw new FormatException("Floating-point value cannot round-trip through the decimal report model.");
        return result;
    }
}
