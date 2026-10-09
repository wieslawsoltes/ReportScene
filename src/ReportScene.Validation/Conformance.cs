using ReportScene.Model;
using ReportScene.Expressions;
using ReportScene.Data;

namespace ReportScene.Validation;

public sealed record TestResult(string Name, bool Passed, string? Error = null);

/// <summary>The same suite is callable from a console host and the actual .NET WebAssembly application.</summary>
public static partial class Conformance
{
    public static List<TestResult> Run()
    {
        var results = new List<TestResult>();
        void Test(string name, Action action)
        { try { action(); results.Add(new(name, true)); } catch (Exception e) { results.Add(new(name, false, e.ToString())); } }
        var e = new ExpressionEngine();
        var table = new DataTable { Name = "Sales", Columns = ["Category", "Price", "Qty"], Rows = [new() { ["Category"] = "A", ["Price"] = 12.50m, ["Qty"] = 2 }, new() { ["Category"] = "B", ["Price"] = 7m, ["Qty"] = 3 }] };
        var c = new EvaluationContext { Row = table.Rows[0], Rows = table.Rows, TableName = "Sales", Tables = new Dictionary<string, DataTable>(StringComparer.OrdinalIgnoreCase) { [table.Name] = table }, Parameters = new Dictionary<string, Value>(StringComparer.OrdinalIgnoreCase) { ["Tax"] = .2m }, ReportDate = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc) };
        Test("decimal arithmetic is exact", () => Equal(.3m, e.Evaluate("0.1 + 0.2", c).AsNumber()));
        Test("multiplication precedence", () => Equal(14m, e.Evaluate("2 + 3 * 4", c).AsNumber()));
        Test("parentheses", () => Equal(20m, e.Evaluate("(2 + 3) * 4", c).AsNumber()));
        Test("qualified fields", () => Equal(25m, e.Evaluate("[Sales.Price] * [Sales.Qty]", c).AsNumber()));
        Test("case-insensitive fields", () => Equal(12.5m, e.Evaluate("sales.price", c).AsNumber()));
        Test("parameters", () => Equal(15m, e.Evaluate("Price * (1 + Parameters.Tax)", c).AsNumber()));
        Test("lazy conditional", () => Equal(42m, e.Evaluate("IIf(true, 42, 1/0)", c).AsNumber()));
        Test("ternary conditional", () => Equal(3m, e.Evaluate("false ? 1 : 3", c).AsNumber()));
        Test("short-circuit and", () => Equal(false, e.Evaluate("false && 1/0 > 0", c).AsBoolean()));
        Test("short-circuit or", () => Equal(true, e.Evaluate("true || Unknown", c).AsBoolean()));
        Test("null coalescing", () => Equal("fallback", e.Evaluate("null ?? 'fallback'", c).AsString()));
        Test("sum", () => Equal(19.5m, e.Evaluate("Sum(Price)", c).AsNumber()));
        Test("aggregate expression", () => Equal(46m, e.Evaluate("Sum(Price * Qty)", c).AsNumber()));
        Test("average", () => Equal(9.75m, e.Evaluate("Avg(Price)", c).AsNumber()));
        Test("count", () => Equal(2m, e.Evaluate("Count()", c).AsNumber()));
        Test("min", () => Equal(7m, e.Evaluate("Min(Price)", c).AsNumber()));
        Test("max", () => Equal(12.5m, e.Evaluate("Max(Price)", c).AsNumber()));
        Test("global aggregate without active row", () => Equal(19.5m, e.Evaluate("Sum(Sales.Price)", c with { TableName = "", Row = new(), Rows = [] }).AsNumber()));
        Test("format number", () => Equal("12.50", e.Evaluate("FormatNumber(Price, 'F2')", c).AsString()));
        Test("date functions", () => Equal("2026-01-05", e.Evaluate("FormatDate(AddDays(Date, 3), 'yyyy-MM-dd')", c).AsString()));
        Test("string escapes", () => Equal("it's", e.Evaluate("'it''s'", c).AsString()));
        Test("string functions", () => Equal("PORT", e.Evaluate("Upper(Substring('report', 2))", c).AsString()));
        Test("round to even", () => Equal(2m, e.Evaluate("Round(2.5)", c).AsNumber()));
        Test("VB-style operators", () => Equal(true, e.Evaluate("Not false And 3 <> 4", c).AsBoolean()));
        Test("text expressions", () => Equal("Total: 25.00", e.Interpolate("Total: [Price * Qty]", c, "F2")));
        Test("nested field interpolation preserves decimal scale", () => Equal("25.00", e.Interpolate("[[Sales.Price] * [Sales.Qty]]", c)));
        Test("escaped expression delimiter", () => Equal("[literal]", e.Interpolate("\\[literal\\]", c)));
        Test("unknown fields rejected", () => Throws<ExpressionException>(() => e.Evaluate("Missing", c)));
        Test("arbitrary runtime calls rejected", () => Throws<ExpressionException>(() => e.Evaluate("System.IO.File.ReadAllText('secret')", c)));
        Test("division by zero diagnosed", () => Throws<ExpressionException>(() => e.Evaluate("1/0", c)));
        Test("trailing input rejected", () => Throws<ExpressionException>(() => e.Evaluate("1 2", c)));
        Test("nesting bound", () => Throws<ExpressionException>(() => e.Evaluate(new string('(', 110) + "1" + new string(')', 110), c)));
        Test("JSON scalar data", () => Equal(2, DataReaders.Json("[{\"x\":1},{\"y\":true}]").Columns.Count));
        Test("JSON sparse rows normalized", () => Equal(true, DataReaders.Json("[{\"x\":1},{\"y\":true}]").Rows[1]["x"].IsNull));
        Test("JSON nested rows rejected", () => Throws<FormatException>(() => DataReaders.Json("[{\"x\":{}}]")));
        Test("JSON duplicate columns rejected", () => Throws<FormatException>(() => DataReaders.Json("[{\"x\":1,\"X\":2}]")));
        Test("JSON row limit", () => Throws<FormatException>(() => DataReaders.Json("[{},{}]", options: new(MaximumRows: 1))));
        Test("CSV quoted delimiter", () => Equal("a,b", DataReaders.Csv("Name,Amount\r\n\"a,b\",12\r\n").Rows[0]["Name"].AsString()));
        Test("CSV multiline fields", () => Equal("a\nb", DataReaders.Csv("Name\n\"a\nb\"\n").Rows[0]["Name"].AsString()));
        Test("CSV escaped quote", () => Equal("a\"b", DataReaders.Csv("Name\n\"a\"\"b\"").Rows[0]["Name"].AsString()));
        Test("CSV numeric inference", () => Equal(12.5m, DataReaders.Csv("Amount\n12.5", options: new(InferNumbers: true)).Rows[0]["Amount"].Number));
        Test("CSV duplicate headers rejected", () => Throws<FormatException>(() => DataReaders.Csv("a,A\n1,2")));
        Test("CSV inconsistent width rejected", () => Throws<FormatException>(() => DataReaders.Csv("a,b\n1")));
        Test("CSV malformed quotes rejected", () => Throws<FormatException>(() => DataReaders.Csv("a\n\"unclosed")));
        Test("CSV quote suffix rejected", () => Throws<FormatException>(() => DataReaders.Csv("a\n\"x\"z")));
        Test("CSV formula protection", () => Equal(true, DataReaders.WriteCsv(new() { Columns = ["x"], Rows = [new() { ["x"] = "=1+1" }] }).Contains("'=1+1")));
        Test("empty CSV", () => Equal(0, DataReaders.Csv("").Rows.Count));
        Test("text wrapping makes progress", () => Equal(3, TextLayout.Wrap("abc", 1, new(), new ApproximateTextMeasurer()).Count));
        Test("surrogate pair remains intact", () => Equal("😀", TextLayout.Wrap("😀x", 1, new(), new ApproximateTextMeasurer())[0]));
        Test("combining sequence remains intact", () => Equal("a\u0301", TextLayout.Wrap("a\u0301b", 1, new(), new ApproximateTextMeasurer())[0]));
        MoreTests(Test);
        return results;
    }
    static partial void MoreTests(Action<string, Action> test);
    internal static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected <{expected}>, got <{actual}>."); }
    internal static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}."); }
}
