using ReportScene.Matrix;
using ReportScene.Model;

namespace ReportScene.Validation;

public static class MatrixTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action)
        { try { action(); return new("matrix: " + name, true); } catch (Exception e) { return new("matrix: " + name, false, e.ToString()); } }
        void Equal<T>(T expected, T actual) => Conformance.Equal(expected, actual);
        DataRow Row(Value r, Value c, Value v) => new() { ["R"] = r, ["C"] = c, ["V"] = v };
        MatrixItem Definition(MatrixMeasure? first = null, MatrixMeasure? second = null) => new()
        {
            RowDimensions = [new() { Expression = "R" }], ColumnDimensions = [new() { Expression = "C" }],
            Measures = first is null ? [new() { Expression = "V" }] : second is null ? [first] : [first, second]
        };
        var engine = new MatrixEngine();
        var rows = new[] { Row("A", "X", 10), Row("A", "X", 30), Row("B", "X", 100), Row("B", "Y", Value.Null) };
        yield return Test("duplicate intersections sum exact decimals", () => Equal(40m, engine.Build(Definition(), rows).GetValue(0, 0, 0).AsNumber()));
        yield return Test("grand totals are independent source sums", () => Equal(140m, engine.Build(Definition(), rows).GrandTotal(0).AsNumber()));
        yield return Test("row and column grand totals", () => { var r = engine.Build(Definition(), rows); Equal(40m, r.GetValue(0, 2, 0).AsNumber()); Equal(140m, r.GetValue(2, 0, 0).AsNumber()); });
        yield return Test("sparse intersections stay null", () => Equal(true, engine.Build(Definition(), rows).GetValue(0, 1, 0).IsNull));
        yield return Test("average totals are weighted by source count", () => Equal(140m / 3, engine.Build(Definition(new() { Expression = "V", Aggregate = MatrixAggregate.Average }), rows).GrandTotal(0).AsNumber()));
        yield return Test("null-only average is null", () => Equal(true, engine.Build(Definition(new() { Expression = "V", Aggregate = MatrixAggregate.Average }), rows).GetValue(1, 1, 0).IsNull));
        yield return Test("Count excludes null while CountRows includes it", () => { var r = engine.Build(Definition(new() { Expression = "V", Aggregate = MatrixAggregate.Count }, new() { Expression = "", Aggregate = MatrixAggregate.CountRows }), rows); Equal(3m, r.GrandTotal(0).AsNumber()); Equal(4m, r.GrandTotal(1).AsNumber()); });
        yield return Test("distinct grand totals do not sum group cardinalities", () => { var r = engine.Build(Definition(new() { Expression = "V", Aggregate = MatrixAggregate.DistinctCount }), [Row("A", "X", 1), Row("B", "X", 1), Row("B", "Y", 2)]); Equal(2m, r.GrandTotal(0).AsNumber()); });
        yield return Test("Min and Max support strings and skip null", () => { var r = engine.Build(Definition(new() { Expression = "V", Aggregate = MatrixAggregate.Min }, new() { Expression = "V", Aggregate = MatrixAggregate.Max }), [Row("A", "X", "Zulu"), Row("A", "X", Value.Null), Row("A", "X", "Alpha")]); Equal("Alpha", r.GrandTotal(0).AsString()); Equal("Zulu", r.GrandTotal(1).AsString()); });
        yield return Test("First and Last retain source order including null", () => { var r = engine.Build(Definition(new() { Expression = "V", Aggregate = MatrixAggregate.First }, new() { Expression = "V", Aggregate = MatrixAggregate.Last }), [Row("B", "X", Value.Null), Row("A", "X", 7)]); Equal(true, r.GrandTotal(0).IsNull); Equal(7m, r.GrandTotal(1).AsNumber()); });
        yield return Test("typed keys distinguish number string bool and null", () => { var r = engine.Build(Definition(), [Row(1, "X", 1), Row("1", "X", 2), Row(false, "X", 4), Row(Value.Null, "X", 8)]); Equal(5, r.Rows.Count); Equal(15m, r.GrandTotal(0).AsNumber()); });
        yield return Test("decimal scale is not a separate category", () => Equal(2, engine.Build(Definition(), [Row(1m, "X", 1), Row(1.00m, "X", 2)]).Rows.Count));
        yield return Test("descending leaves precede grand total", () => { var d = Definition() with { RowDimensions = [new() { Expression = "R", Descending = true }] }; var r = engine.Build(d, rows); Equal("B", r.Rows[0].Values[0].AsString()); Equal(MatrixAxisKind.GrandTotal, r.Rows[^1].Kind); });
        yield return Test("hierarchical subtotals follow their children", () =>
        {
            var d = Definition() with { RowDimensions = [new() { Expression = "R", Subtotal = true }, new() { Expression = "C" }], ColumnDimensions = [] };
            var r = engine.Build(d, rows); Equal(6, r.Rows.Count); Equal(MatrixAxisKind.Subtotal, r.Rows[1].Kind); Equal("A", r.Rows[1].Values[0].AsString()); Equal(40m, r.GetValue(1, 0, 0).AsNumber()); Equal(100m, r.GetValue(4, 0, 0).AsNumber());
        });
        yield return Test("structural keys cannot collide through delimiters", () =>
        {
            var d = Definition() with { RowDimensions = [new() { Expression = "R" }, new() { Expression = "C" }], ColumnDimensions = [] };
            Equal(3, engine.Build(d, [Row("A|B", "C", 1), Row("A", "B|C", 2)]).Rows.Count);
        });
        yield return Test("zero-dimensional axes produce a scalar", () => { var r = engine.Build(Definition() with { RowDimensions = [], ColumnDimensions = [] }, rows); Equal(1, r.Rows.Count); Equal(1, r.Columns.Count); Equal(140m, r.GetValue(0, 0, 0).AsNumber()); });
        yield return Test("empty source grand total stays null", () => { var r = engine.Build(Definition(), []); Equal(1, r.Rows.Count); Equal(true, r.GrandTotal(0).IsNull); Equal(0, r.AcceptedRows); });
        yield return Test("empty source without totals has no invented groups", () => Equal(0, engine.Build(Definition() with { ShowRowGrandTotal = false }, []).Rows.Count));
        yield return Test("filter and calculated values use caller evaluator", () => { var r = engine.Build(Definition() with { Filter = "accept" }, rows, (row, expression) => expression == "accept" ? row["R"].AsString() == "A" : expression == "V" ? row["V"].AsNumber() * 2 : row[expression]); Equal(2, r.AcceptedRows); Equal(80m, r.GrandTotal(0).AsNumber()); });
        yield return Test("dimensions and measures evaluate once per accepted row", () => { int calls = 0; engine.Build(Definition(), rows, (row, field) => { calls++; return row[field]; }); Equal(12, calls); });
        yield return Test("source row numbers survive duplicate row references", () => { var row = Row("A", "X", 0); var r = engine.BuildIndexed(Definition(), [row, row], (r, number, field) => field == "V" ? number : r[field]); Equal(3m, r.GrandTotal(0).AsNumber()); });
        yield return Test("percentage row denominator ignores hidden totals", () => { var d = Definition(new() { Expression = "V", Display = MatrixDisplay.PercentOfRow }) with { ShowColumnGrandTotal = false }; var r = engine.Build(d, [Row("A", "X", 1), Row("A", "Y", 3)]); Equal(.25m, r.GetValue(0, 0, 0).AsNumber()); Equal(.75m, r.GetValue(0, 1, 0).AsNumber()); Equal(2, r.Columns.Count); });
        yield return Test("percentage column and grand denominators", () => { var r = engine.Build(Definition(new() { Expression = "V", Display = MatrixDisplay.PercentOfColumn }, new() { Expression = "V", Display = MatrixDisplay.PercentOfGrandTotal }), [Row("A", "X", 1), Row("B", "X", 3), Row("B", "Y", 6)]); Equal(.25m, r.GetValue(0, 0, 0).AsNumber()); Equal(.1m, r.GetValue(0, 0, 1).AsNumber()); });
        yield return Test("zero percentage denominator is null", () => Equal(true, engine.Build(Definition(new() { Expression = "V", Display = MatrixDisplay.PercentOfGrandTotal }), [Row("A", "X", 0)]).GetValue(0, 0, 0).IsNull));
        yield return Test("result survives later definition edits", () => { var d = Definition(); var r = engine.Build(d, rows); d.Measures.Clear(); d.RowDimensions.Clear(); Equal(140m, r.GrandTotal(0).AsNumber()); Equal(3, r.Rows.Count); });
        yield return Test("row limit enforced", () => Conformance.Throws<MatrixException>(() => engine.Build(Definition(), rows, limits: new(MaximumRows: 1))));
        yield return Test("axis limit enforced", () => Conformance.Throws<MatrixException>(() => engine.Build(Definition(), rows, limits: new(MaximumAxisNodes: 1))));
        yield return Test("dense output limit enforced despite sparse storage", () => Conformance.Throws<MatrixException>(() => engine.Build(Definition(), rows, limits: new(MaximumOutputValues: 4))));
        yield return Test("sparse state limit enforced", () => Conformance.Throws<MatrixException>(() => engine.Build(Definition(), rows, limits: new(MaximumAccumulatorValues: 1))));
        yield return Test("aggregation work limit enforced", () => Conformance.Throws<MatrixException>(() => engine.Build(Definition(), rows, limits: new(MaximumUpdates: 1))));
        yield return Test("distinct storage limit enforced", () => Conformance.Throws<MatrixException>(() => engine.Build(Definition(new() { Expression = "V", Aggregate = MatrixAggregate.DistinctCount }), rows, limits: new(MaximumDistinctValues: 1))));
        yield return Test("pre-cancellation enforced", () => Conformance.Throws<OperationCanceledException>(() => engine.Build(Definition(), rows, cancellationToken: new(true))));
        yield return Test("mid-evaluation cancellation enforced", () => { using var c = new CancellationTokenSource(); Conformance.Throws<OperationCanceledException>(() => engine.Build(Definition(), rows, (row, field) => { c.Cancel(); return row[field]; }, cancellationToken: c.Token)); });
        yield return Test("decimal overflow diagnosed", () => Conformance.Throws<MatrixException>(() => engine.Build(Definition(), [Row("A", "X", decimal.MaxValue), Row("A", "X", 1)])));
        yield return Test("invalid percentages rejected", () => Conformance.Throws<MatrixException>(() => engine.Build(Definition(new() { Aggregate = MatrixAggregate.Average, Display = MatrixDisplay.PercentOfRow }), rows)));
        yield return Test("empty measure list rejected", () => Conformance.Throws<MatrixException>(() => engine.Build(Definition() with { Measures = [] }, rows)));
        yield return Test("invalid cell geometry rejected", () => Conformance.Throws<MatrixException>(() => engine.Build(Definition() with { CellWidth = double.NaN }, rows)));
        yield return Test("missing source field is not silently null", () => Conformance.Throws<MatrixException>(() => engine.Build(Definition(new() { Expression = "Missing" }), rows)));
        yield return Test("result indices validated", () => Conformance.Throws<ArgumentOutOfRangeException>(() => engine.Build(Definition(), rows).GetValue(-1, 0, 0)));
    }
}
