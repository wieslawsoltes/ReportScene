namespace ReportScene.Model;

public enum MatrixAggregate { Sum, Count, CountRows, Average, Min, Max, DistinctCount, First, Last }
public enum MatrixDisplay { Value, PercentOfRow, PercentOfColumn, PercentOfGrandTotal }

/// <summary>A typed, expression-backed level in a cross-tab axis. Subtotals follow its descendants.</summary>
public sealed record MatrixDimension
{
    public string Expression { get; init; } = "Category";
    public string Title { get; init; } = "";
    public bool Descending { get; init; }
    public bool Subtotal { get; init; }
}

public sealed record MatrixMeasure
{
    public string Expression { get; init; } = "Value";
    public string Title { get; init; } = "Value";
    public MatrixAggregate Aggregate { get; init; }
    public string? Format { get; init; }
    public MatrixDisplay Display { get; init; }
}

/// <summary>Dynamic cross-tab. Geometry is in points; cells never depend on screen DPI.</summary>
public sealed record MatrixItem : ReportItem
{
    public MatrixItem() { Bounds = new(0, 0, 480, 120); CanGrow = true; }
    public string DataSource { get; init; } = "Data";
    public string? Filter { get; init; }
    public List<MatrixDimension> RowDimensions { get; init; } = [];
    public List<MatrixDimension> ColumnDimensions { get; init; } = [];
    public List<MatrixMeasure> Measures { get; init; } = [new()];
    public double RowHeaderWidth { get; init; } = 120;
    public double CellWidth { get; init; } = 90;
    public double RowHeight { get; init; } = 24;
    public bool ShowRowGrandTotal { get; init; } = true;
    public bool ShowColumnGrandTotal { get; init; } = true;
    public bool RepeatHeaders { get; init; } = true;
    public string NullLabel { get; init; } = "(null)";
    public string TotalLabel { get; init; } = "Total";
    public string GrandTotalLabel { get; init; } = "Grand total";
}

public static class MatrixDefinitionValidator
{
    public static IReadOnlyList<Diagnostic> Validate(MatrixItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var errors = new List<Diagnostic>();
        void Error(string message) => errors.Add(new("RS1400", message, ObjectId: item.Id));
        bool Expression(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length <= 8192;
        bool Size(double value) => double.IsFinite(value) && value > 0 && value <= 1_000_000;
        if (!Expression(item.DataSource)) Error("A matrix needs a nonempty data-source name.");
        if (item.Filter is { Length: > 8192 }) Error("A matrix filter exceeds 8192 characters.");
        if (!Size(item.RowHeaderWidth) || !Size(item.CellWidth) || !Size(item.RowHeight)) Error("Matrix cell, row and row-header dimensions must be positive and finite.");
        if (item.NullLabel is null || item.TotalLabel is null || item.GrandTotalLabel is null) Error("Matrix labels cannot be null.");
        foreach (var axis in new[] { item.RowDimensions, item.ColumnDimensions })
        {
            if (axis is null || axis.Count > 8) { Error("A matrix axis supports zero to eight dimensions."); continue; }
            if (axis.Any(d => d is null || !Expression(d.Expression) || d.Title is null)) Error("Matrix dimensions need valid expressions and non-null titles.");
        }
        if (item.Measures is null || item.Measures.Count is < 1 or > 32) Error("A matrix needs one to 32 measures.");
        else foreach (var measure in item.Measures)
        {
            if (measure is null) { Error("A matrix measure cannot be null."); continue; }
            if (!Enum.IsDefined(measure.Aggregate) || !Enum.IsDefined(measure.Display)) Error("Unknown matrix aggregation or display mode.");
            if (measure.Aggregate != MatrixAggregate.CountRows && !Expression(measure.Expression)) Error("A matrix measure needs a valid expression.");
            if (measure.Title is null) Error("A matrix measure title cannot be null.");
            if (measure.Display != MatrixDisplay.Value && measure.Aggregate is not (MatrixAggregate.Sum or MatrixAggregate.Count or MatrixAggregate.CountRows or MatrixAggregate.DistinctCount))
                Error("Percentage displays require Sum, Count, CountRows or DistinctCount.");
        }
        return errors;
    }
}
