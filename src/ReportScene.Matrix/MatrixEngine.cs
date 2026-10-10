using ReportScene.Model;

namespace ReportScene.Matrix;

public sealed record MatrixLimits(int MaximumRows = 1_000_000, int MaximumAxisNodes = 20_000,
    int MaximumOutputValues = 1_000_000, int MaximumAccumulatorValues = 1_000_000,
    long MaximumUpdates = 50_000_000, int MaximumDistinctValues = 1_000_000);

public sealed class MatrixException(string message) : Exception(message);
public enum MatrixAxisKind { Leaf, Subtotal, GrandTotal }

public sealed record MatrixAxisEntry
{
    internal int Id { get; }
    public IReadOnlyList<Value> Values { get; }
    public MatrixAxisKind Kind { get; }
    public int Depth => Values.Count;
    internal MatrixAxisEntry(int id, Value[] values, MatrixAxisKind kind)
    { Id = id; Values = Array.AsReadOnly((Value[])values.Clone()); Kind = kind; }
}

/// <summary>An immutable, sparse result. Unpopulated intersections are null, not fabricated zeros.</summary>
public sealed class MatrixResult
{
    private readonly Dictionary<(int Row, int Column), Value[]> cells;
    public IReadOnlyList<MatrixAxisEntry> Rows { get; }
    public IReadOnlyList<MatrixAxisEntry> Columns { get; }
    public IReadOnlyList<MatrixMeasure> Measures { get; }
    public int AcceptedRows { get; }
    public int SparseCellCount => cells.Count;
    internal MatrixResult(MatrixAxisEntry[] rows, MatrixAxisEntry[] columns, MatrixMeasure[] measures,
        Dictionary<(int Row, int Column), Value[]> cells, int acceptedRows)
    {
        Rows = Array.AsReadOnly(rows); Columns = Array.AsReadOnly(columns);
        Measures = Array.AsReadOnly(measures); this.cells = cells; AcceptedRows = acceptedRows;
    }
    public Value GrandTotal(int measure) { CheckMeasure(measure); return Raw(0, 0, measure); }
    public Value GetRawValue(int row, int column, int measure)
    { Check(row, column, measure); return Raw(Rows[row].Id, Columns[column].Id, measure); }
    public Value GetValue(int row, int column, int measure)
    {
        Check(row, column, measure);
        int r = Rows[row].Id, c = Columns[column].Id;
        var value = Raw(r, c, measure);
        var display = Measures[measure].Display;
        if (display == MatrixDisplay.Value || value.IsNull) return value;
        var denominator = display switch
        {
            MatrixDisplay.PercentOfRow => Raw(r, 0, measure),
            MatrixDisplay.PercentOfColumn => Raw(0, c, measure),
            MatrixDisplay.PercentOfGrandTotal => Raw(0, 0, measure),
            _ => throw new MatrixException("Unknown matrix percentage mode.")
        };
        if (denominator.IsNull || denominator.AsNumber() == 0) return Value.Null;
        return value.AsNumber() / denominator.AsNumber();
    }
    private Value Raw(int row, int column, int measure) => cells.TryGetValue((row, column), out var values) ? values[measure] : Value.Null;
    private void CheckMeasure(int measure)
    { if ((uint)measure >= (uint)Measures.Count) throw new ArgumentOutOfRangeException(nameof(measure)); }
    private void Check(int row, int column, int measure)
    {
        if ((uint)row >= (uint)Rows.Count) throw new ArgumentOutOfRangeException(nameof(row));
        if ((uint)column >= (uint)Columns.Count) throw new ArgumentOutOfRangeException(nameof(column));
        CheckMeasure(measure);
    }
}

/// <summary>
/// Independent of expressions, report pagination, UI and rendering. Values and keys are evaluated
/// once per accepted source row. Prefix rollups aggregate source values, never already-aggregated cells.
/// </summary>
public sealed class MatrixEngine
{
    public MatrixResult Build(MatrixItem definition, IReadOnlyList<DataRow> rows,
        Func<DataRow, string, Value>? evaluate = null, MatrixLimits? limits = null, CancellationToken cancellationToken = default)
        => BuildIndexed(definition, rows, evaluate is null ? null : (row, _, expression) => evaluate(row, expression), limits, cancellationToken);

    /// <summary>The evaluator receives the one-based source row number, before applying the filter.</summary>
    public MatrixResult BuildIndexed(MatrixItem definition, IReadOnlyList<DataRow> rows,
        Func<DataRow, int, string, Value>? evaluate = null, MatrixLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition); ArgumentNullException.ThrowIfNull(rows);
        var errors = MatrixDefinitionValidator.Validate(definition);
        if (errors.Count > 0) throw new MatrixException(string.Join("\n", errors.Select(e => e.Message)));
        limits ??= new();
        if (limits.MaximumRows < 1 || limits.MaximumAxisNodes < 1 || limits.MaximumOutputValues < 1 ||
            limits.MaximumAccumulatorValues < 1 || limits.MaximumUpdates < 1 || limits.MaximumDistinctValues < 1)
            throw new ArgumentOutOfRangeException(nameof(limits));
        cancellationToken.ThrowIfCancellationRequested();
        if (rows.Count > limits.MaximumRows) throw new MatrixException("Matrix source exceeds the row limit.");
        evaluate ??= static (row, _, field) => row.TryGetValue(field, out var value) ? value : throw new MatrixException($"Unknown matrix field '{field}'.");
        var dimensionsR = definition.RowDimensions.ToArray();
        var dimensionsC = definition.ColumnDimensions.ToArray();
        var measures = definition.Measures.ToArray();
        var axisR = new Axis(dimensionsR, limits.MaximumAxisNodes);
        var axisC = new Axis(dimensionsC, limits.MaximumAxisNodes);
        var cells = new Dictionary<(int Row, int Column), Accumulator[]>();
        int accepted = 0, distinct = 0;
        long updates = 0, accumulatorValues = 0;
        void AddDistinct()
        { if (++distinct > limits.MaximumDistinctValues) throw new MatrixException("Matrix distinct-value storage limit exceeded."); }
        try
        {
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = rows[rowIndex]; int number = rowIndex + 1;
                if (row is null) throw new MatrixException("A matrix source row cannot be null.");
                if (!string.IsNullOrWhiteSpace(definition.Filter) && !evaluate(row, number, definition.Filter).AsBoolean()) continue;
                var keysR = dimensionsR.Select(d => evaluate(row, number, d.Expression)).ToArray();
                var keysC = dimensionsC.Select(d => evaluate(row, number, d.Expression)).ToArray();
                var values = measures.Select(m => m.Aggregate == MatrixAggregate.CountRows ? Value.Null : evaluate(row, number, m.Expression)).ToArray();
                var pathR = axisR.Add(keysR); var pathC = axisC.Add(keysC);
                foreach (var r in pathR) foreach (var c in pathC)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    updates = checked(updates + measures.Length);
                    if (updates > limits.MaximumUpdates) throw new MatrixException("Matrix aggregation work limit exceeded.");
                    if (!cells.TryGetValue((r, c), out var state))
                    {
                        accumulatorValues += measures.Length;
                        if (accumulatorValues > limits.MaximumAccumulatorValues) throw new MatrixException("Matrix sparse-accumulator limit exceeded.");
                        state = measures.Select(m => new Accumulator(m.Aggregate)).ToArray(); cells.Add((r, c), state);
                    }
                    for (int i = 0; i < measures.Length; i++) state[i].Add(values[i], AddDistinct);
                }
                accepted++;
            }
            var outputR = axisR.Entries(definition.ShowRowGrandTotal, cancellationToken);
            var outputC = axisC.Entries(definition.ShowColumnGrandTotal, cancellationToken);
            long outputCount = checked((long)outputR.Length * outputC.Length * measures.Length);
            if (outputCount > limits.MaximumOutputValues) throw new MatrixException("Matrix output exceeds the cell-value limit; reduce dimensions or filter the source.");
            var result = new Dictionary<(int Row, int Column), Value[]>(cells.Count);
            foreach (var cell in cells)
            { cancellationToken.ThrowIfCancellationRequested(); result.Add(cell.Key, cell.Value.Select(s => s.Value).ToArray()); }
            return new(outputR, outputC, measures, result, accepted);
        }
        catch (OverflowException e) { throw new MatrixException("Matrix decimal or size overflow: " + e.Message); }
    }

    private sealed class Axis(MatrixDimension[] dimensions, int maximumNodes)
    {
        private readonly Node root = new(0, []);
        private int nextId;
        public int[] Add(Value[] values)
        {
            var path = new List<int> { 0 }; var node = root;
            for (int i = 0; i < values.Length; i++)
            {
                if (!node.Children.TryGetValue(values[i], out var child))
                {
                    if (++nextId > maximumNodes) throw new MatrixException("Matrix axis-node limit exceeded.");
                    child = new(nextId, values[..(i + 1)]); node.Children.Add(values[i], child);
                }
                node = child;
                if (i == values.Length - 1 || dimensions[i].Subtotal) path.Add(node.Id);
            }
            return path.ToArray();
        }
        public MatrixAxisEntry[] Entries(bool grandTotal, CancellationToken cancellation)
        {
            var result = new List<MatrixAxisEntry>();
            void Visit(Node node)
            {
                cancellation.ThrowIfCancellationRequested();
                if (node.Values.Length == dimensions.Length) { result.Add(new(node.Id, node.Values, MatrixAxisKind.Leaf)); return; }
                bool descending = dimensions[node.Values.Length].Descending;
                foreach (var child in node.Children.OrderBy(p => p.Key, Comparer<Value>.Create((a, b) => descending ? Compare(b, a) : Compare(a, b)))) Visit(child.Value);
                if (node.Values.Length > 0 && dimensions[node.Values.Length - 1].Subtotal)
                    result.Add(new(node.Id, node.Values, MatrixAxisKind.Subtotal));
            }
            Visit(root);
            if (grandTotal && dimensions.Length > 0) result.Add(new(0, [], MatrixAxisKind.GrandTotal));
            return result.ToArray();
        }
        private sealed class Node(int id, Value[] values)
        {
            public int Id { get; } = id;
            public Value[] Values { get; } = values;
            public Dictionary<Value, Node> Children { get; } = new();
        }
    }
    private sealed class Accumulator(MatrixAggregate kind)
    {
        private decimal sum;
        private int count;
        private bool seen;
        private Value current;
        private HashSet<Value>? distinct;
        public void Add(Value value, Action addDistinct)
        {
            if (kind == MatrixAggregate.CountRows) { count++; return; }
            if (kind == MatrixAggregate.First) { if (!seen) current = value; seen = true; return; }
            if (kind == MatrixAggregate.Last) { current = value; seen = true; return; }
            if (value.IsNull) return;
            switch (kind)
            {
                case MatrixAggregate.Sum: case MatrixAggregate.Average: sum = checked(sum + value.AsNumber()); count++; break;
                case MatrixAggregate.Count: count++; break;
                case MatrixAggregate.Min: if (!seen || Compare(value, current) < 0) current = value; seen = true; break;
                case MatrixAggregate.Max: if (!seen || Compare(value, current) > 0) current = value; seen = true; break;
                case MatrixAggregate.DistinctCount:
                    distinct ??= new(); if (distinct.Add(value)) addDistinct(); break;
                default: throw new MatrixException("Unknown matrix aggregate.");
            }
        }
        public Value Value => kind switch
        {
            MatrixAggregate.Sum => sum,
            MatrixAggregate.Average => count == 0 ? Model.Value.Null : sum / count,
            MatrixAggregate.Count or MatrixAggregate.CountRows => count,
            MatrixAggregate.DistinctCount => distinct?.Count ?? 0,
            _ => current
        };
    }
    // Axis equality is typed. In particular 1, "1", false and null never collapse into one group.
    private static int Compare(Value a, Value b)
    {
        if (a.Kind != b.Kind) return a.Kind.CompareTo(b.Kind);
        return a.Kind switch
        {
            ValueKind.Number => a.Number.CompareTo(b.Number),
            ValueKind.String => StringComparer.Ordinal.Compare(a.Text, b.Text),
            ValueKind.Boolean => a.Boolean.CompareTo(b.Boolean),
            _ => 0
        };
    }
}
