using ReportScene.Model;

namespace ReportScene.Expressions;

public sealed record EvaluationContext
{
    public DataRow Row { get; init; } = new();
    public string TableName { get; init; } = "";
    public IReadOnlyList<DataRow> Rows { get; init; } = [];
    public IReadOnlyDictionary<string, DataTable> Tables { get; init; } = new Dictionary<string, DataTable>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, Value> Parameters { get; init; } = new Dictionary<string, Value>(StringComparer.OrdinalIgnoreCase);
    public EvaluationContext? Parent { get; init; }
    public int RowNumber { get; init; } = 1;
    public int PageNumber { get; init; } = 1;
    public int TotalPages { get; init; } = 1;
    public int ColumnNumber { get; init; } = 1;
    public DateTime ReportDate { get; init; } = DateTime.UtcNow;

    public Value Resolve(string name)
    {
        if (name.StartsWith("Parameters.", StringComparison.OrdinalIgnoreCase)) name = name[11..];
        if (Parameters.TryGetValue(name, out var parameter)) return parameter;
        switch (name.ToLowerInvariant())
        {
            case "pagen": case "page": case "pagenumber": return PageNumber;
            case "totalpages": return TotalPages;
            case "row#": case "row": case "rownumber": return RowNumber;
            case "column#": case "columnnumber": return ColumnNumber;
            case "date": case "today": return Value.From(ReportDate.Date);
            case "now": case "reportdate": return Value.From(ReportDate);
        }
        if (name.StartsWith("Parent.", StringComparison.OrdinalIgnoreCase))
            return Parent?.Resolve(name[7..]) ?? Value.Null;
        var dot = name.IndexOf('.');
        if (dot >= 0)
        {
            string source = name[..dot], field = name[(dot + 1)..];
            for (EvaluationContext? c = this; c is not null; c = c.Parent)
                if (source.Equals(c.TableName, StringComparison.OrdinalIgnoreCase))
                    return c.Row.TryGetValue(field, out var v) ? v : throw new ExpressionException($"Unknown field '{name}'.");
            throw new ExpressionException($"Data source '{source}' has no active row.");
        }
        if (Row.TryGetValue(name, out var value)) return value;
        throw new ExpressionException($"Unknown field or parameter '{name}'.");
    }
}

public sealed class ExpressionException(string message) : Exception(message);
