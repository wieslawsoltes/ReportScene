using System.Globalization;
using ReportScene.Data.AdoNet;
using ReportScene.Model;
using DataTable = System.Data.DataTable;

namespace ReportScene.Validation;

public static class AdoDataTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action)
        { try { action(); return new("ADO.NET: " + name, true); } catch (Exception e) { return new("ADO.NET: " + name, false, e.ToString()); } }
        void Equal<T>(T expected, T actual) => Conformance.Equal(expected, actual);
        DataTable Fixture()
        {
            var table = new DataTable("Sales"); table.Columns.Add("Id", typeof(int)); table.Columns.Add("Amount", typeof(decimal)); table.Columns.Add("Label", typeof(string));
            table.Rows.Add(1, 12.50m, "First"); table.Rows.Add(2, 7.25m, DBNull.Value); return table;
        }
        yield return Test("typed table snapshot preserves decimal and DBNull", () => { var t = AdoDataReader.FromDataTable(Fixture()); Equal(2, t.Rows.Count); Equal(12.50m, t.Rows[0]["Amount"].Number); Equal(true, t.Rows[1]["Label"].IsNull); Equal("Sales", t.Name); });
        yield return Test("reader is left open and next result remains available", () => { using var reader = new System.Data.DataTableReader([Fixture(), Fixture()]); Equal(2, AdoDataReader.Read(reader).Rows.Count); Equal(false, reader.IsClosed); Equal(true, reader.NextResult()); });
        yield return Test("async reader returns the same values", () => { using var reader = Fixture().CreateDataReader(); Equal(7.25m, AdoDataReader.ReadAsync(reader).GetAwaiter().GetResult().Rows[1]["Amount"].Number); Equal(false, reader.IsClosed); });
        yield return Test("snapshot does not share mutable source rows", () => { var source = Fixture(); var t = AdoDataReader.FromDataTable(source); source.Rows[0]["Amount"] = 999m; Equal(12.5m, t.Rows[0]["Amount"].Number); });
        yield return Test("deleted source rows are excluded", () => { var source = Fixture(); source.AcceptChanges(); source.Rows[0].Delete(); Equal(1, AdoDataReader.FromDataTable(source).Rows.Count); });
        yield return Test("empty source retains schema", () => { var source = Fixture(); source.Rows.Clear(); var result = AdoDataReader.FromDataTable(source); Equal(3, result.Columns.Count); Equal(0, result.Rows.Count); });
        yield return Test("case-colliding columns rejected", () => { var table = new DataTable("Bad"); table.Columns.Add("name"); table.Columns.Add("Name"); Conformance.Throws<FormatException>(() => AdoDataReader.FromDataTable(table)); });
        yield return Test("row limit", () => Conformance.Throws<FormatException>(() => AdoDataReader.FromDataTable(Fixture(), options: new(MaximumRows: 1))));
        yield return Test("cell limit", () => Conformance.Throws<FormatException>(() => AdoDataReader.FromDataTable(Fixture(), options: new(MaximumCells: 3))));
        yield return Test("column limit", () => Conformance.Throws<FormatException>(() => AdoDataReader.FromDataTable(Fixture(), options: new(MaximumColumns: 2))));
        yield return Test("character limit", () => Conformance.Throws<FormatException>(() => AdoDataReader.FromDataTable(Fixture(), options: new(MaximumCharacters: 2))));
        yield return Test("pre-cancellation leaves source unread", () => { using var r = Fixture().CreateDataReader(); Conformance.Throws<OperationCanceledException>(() => AdoDataReader.Read(r, cancellationToken: new(true))); Equal(true, r.Read()); Equal(1, r.GetInt32(0)); });
        yield return Test("async cancellation", () => { using var r = Fixture().CreateDataReader(); Conformance.Throws<OperationCanceledException>(() => AdoDataReader.ReadAsync(r, cancellationToken: new(true)).GetAwaiter().GetResult()); });
        yield return Test("invalid limits", () => Conformance.Throws<ArgumentOutOfRangeException>(() => AdoDataReader.FromDataTable(Fixture(), options: new(MaximumCells: 0))));
        yield return Test("unsigned integers do not overflow signed Int64", () => Equal(18446744073709551615m, AdoDataReader.ConvertValue(ulong.MaxValue).Number));
        yield return Test("signed Int64 remains exact", () => Equal(-9223372036854775808m, AdoDataReader.ConvertValue(long.MinValue).Number));
        yield return Test("decimal min and max remain exact", () => { Equal(decimal.MaxValue, AdoDataReader.ConvertValue(decimal.MaxValue).Number); Equal(decimal.MinValue, AdoDataReader.ConvertValue(decimal.MinValue).Number); });
        yield return Test("finite floats round-trip", () => { Equal(.1m, AdoDataReader.ConvertValue(.1f).Number); Equal(.1m, AdoDataReader.ConvertValue(.1d).Number); });
        yield return Test("nonfinite doubles are rejected", () => Conformance.Throws<FormatException>(() => AdoDataReader.ConvertValue(double.NaN)));
        yield return Test("nonfinite floats are rejected", () => Conformance.Throws<FormatException>(() => AdoDataReader.ConvertValue(float.PositiveInfinity)));
        yield return Test("numeric underflow is not silently rounded to zero", () => Conformance.Throws<FormatException>(() => AdoDataReader.ConvertValue(double.Epsilon)));
        yield return Test("DateTime keeps UTC and ticks", () => { var date = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc).AddTicks(123); Equal(date.ToString("O"), AdoDataReader.ConvertValue(date).AsString()); });
        yield return Test("DateTimeOffset preserves its offset", () => { var date = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)); Equal(date.ToString("O"), AdoDataReader.ConvertValue(date).AsString()); });
        yield return Test("binary data round-trips as base64", () => Equal("AAEC/w==", AdoDataReader.ConvertValue(new byte[] { 0, 1, 2, 255 }).AsString()));
        yield return Test("binary size is bounded", () => Conformance.Throws<FormatException>(() => AdoDataReader.ConvertValue(new byte[3], 2)));
        yield return Test("unsupported CLR objects are not stringified", () => Conformance.Throws<NotSupportedException>(() => AdoDataReader.ConvertValue(new object())));
        yield return Test("culture does not change numeric or date wire values", () => { var previous = CultureInfo.CurrentCulture; try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL"); Equal(.25m, AdoDataReader.ConvertValue(.25d).Number); Equal("2026-01-02", AdoDataReader.ConvertValue(new DateOnly(2026, 1, 2)).AsString()); } finally { CultureInfo.CurrentCulture = previous; } });
        yield return Test("DataSet relations are retained", () =>
        {
            using var data = new System.Data.DataSet(); var parents = new DataTable("Parents"); parents.Columns.Add("Id", typeof(int)); parents.Rows.Add(1);
            var children = new DataTable("Children"); children.Columns.Add("ParentId", typeof(int)); children.Rows.Add(1);
            data.Tables.Add(parents); data.Tables.Add(children); data.Relations.Add("ChildrenOfParent", parents.Columns[0], children.Columns[0]);
            var snapshot = AdoDataReader.FromDataSet(data); Equal(2, snapshot.Tables.Count); Equal(new DataRelation("Parents", "Id", "Children", "ParentId"), snapshot.Relations.Single());
        });
        yield return Test("composite relations fail before lossy conversion", () =>
        {
            using var data = new System.Data.DataSet(); var a = new DataTable("A"); a.Columns.Add("X", typeof(int)); a.Columns.Add("Y", typeof(int)); var b = a.Clone(); b.TableName = "B";
            data.Tables.Add(a); data.Tables.Add(b); data.Relations.Add("Composite", [a.Columns[0], a.Columns[1]], [b.Columns[0], b.Columns[1]]);
            Conformance.Throws<NotSupportedException>(() => AdoDataReader.FromDataSet(data));
        });
        yield return Test("combined DataSet limits apply across tables", () => { using var d = new System.Data.DataSet(); d.Tables.Add(Fixture()); var second = Fixture(); second.TableName = "Other"; d.Tables.Add(second); Conformance.Throws<FormatException>(() => AdoDataReader.FromDataSet(d, new(MaximumRows: 3))); });
        yield return Test("query parameter names are unique", () => Conformance.Throws<ArgumentException>(() => new DbQueryDataProvider("Sales", () => throw new Exception(), "select 1", [new("@Id", 1), new("@id", 2)])));
        yield return Test("query timeout cannot be unbounded", () => Conformance.Throws<ArgumentOutOfRangeException>(() => new DbQueryDataProvider("Sales", () => throw new Exception(), "select 1", timeoutSeconds: 0)));
        yield return Test("query cancellation precedes connection factory", () => { bool called = false; var p = new DbQueryDataProvider("Sales", () => { called = true; throw new Exception(); }, "select 1"); Conformance.Throws<OperationCanceledException>(() => p.ReadAsync(new(true)).GetAwaiter().GetResult()); Equal(false, called); });
        if (OperatingSystem.IsBrowser()) yield return Test("direct database access rejected before browser factory", () => { bool called = false; var p = new DbQueryDataProvider("Sales", () => { called = true; throw new Exception(); }, "select 1"); Conformance.Throws<PlatformNotSupportedException>(() => p.ReadAsync().GetAwaiter().GetResult()); Equal(false, called); });
        else foreach (var result in DbProviderTests.Run()) yield return result;
    }
}
