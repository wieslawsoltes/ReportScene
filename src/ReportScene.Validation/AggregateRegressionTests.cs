using System.Globalization;
using ReportScene.Model;
using ReportScene.Engine;
using ReportScene.Expressions;
using ReportScene.Samples;

namespace ReportScene.Validation;

public static class AggregateRegressionTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action)
        { try { action(); return new(name, true); } catch (Exception e) { return new(name, false, e.ToString()); } }
        var table = new DataTable { Name = "Items", Columns = ["Quantity", "Price"], Rows = [new() { ["Quantity"] = 2, ["Price"] = 12.50m }, new() { ["Quantity"] = 3, ["Price"] = 7m }] };
        var context = new EvaluationContext { Tables = new Dictionary<string, DataTable>(StringComparer.OrdinalIgnoreCase) { [table.Name] = table }, Parameters = new Dictionary<string, Value> { ["Tax"] = .2m } };
        var engine = new ExpressionEngine();
        yield return Test("report-level compound sum resolves its data source", () => Conformance.Equal(46m, engine.Evaluate("Sum(Items.Quantity * Items.Price)", context).AsNumber()));
        yield return Test("aggregate resolves fields nested in function calls", () => Conformance.Equal(46m, engine.Evaluate("Sum(Round(Items.Quantity * Items.Price, 2))", context).AsNumber()));
        yield return Test("aggregate resolves conditional field references", () => Conformance.Equal(25m, engine.Evaluate("Sum(IIf(Items.Price > 10, Items.Price * Items.Quantity, 0))", context).AsNumber()));
        yield return Test("aggregate excludes parameter prefixes from table inference", () => Conformance.Equal(55.2m, engine.Evaluate("Sum(Items.Price * Items.Quantity * (1 + Parameters.Tax))", context).AsNumber()));
        yield return Test("compound aggregate preserves an active group scope", () => Conformance.Equal(25m, engine.Evaluate("Sum(Items.Price * Items.Quantity)", context with { TableName = "Items", Row = table.Rows[0], Rows = [table.Rows[0]] }).AsNumber()));
        yield return Test("missing aggregate source diagnosed before empty iteration", () => Conformance.Throws<ExpressionException>(() => engine.Evaluate("Sum(Missing.Qty * Missing.Price)", context)));
        yield return Test("nested aggregate is explicitly rejected", () => Conformance.Throws<ExpressionException>(() => engine.Evaluate("Sum(Sum(Items.Price))", context)));
        yield return Test("prepared invoice total agrees with independent decimal sum", () =>
        {
            var document = SampleReports.Invoice();
            decimal expected = document.Data.Single().Rows.Sum(r => r["Quantity"].AsNumber() * r["Price"].AsNumber());
            var prepared = new ReportPreparer().Prepare(document);
            var actual = prepared.Pages.SelectMany(p => p.Items).Single(i => i.Source.Name == "GrandTotal" && i.Source is TextItem).Text;
            Conformance.Equal(expected.ToString("C2", CultureInfo.GetCultureInfo("en-US")), actual);
            // Independent line totals: 75 + 175 + 300 + 112.5 + 250 + 412.5 + 150 + 325.
            Conformance.Equal(1800m, expected);
        });
        yield return Test("qualified data fields do not collide with system variables", () =>
        {
            var row = new DataRow { ["Row"] = "North", ["Page"] = "Cover", ["ColumnNumber"] = "Quarter" };
            var c = context with { Row = row, TableName = "Sales", Rows = [row], RowNumber = 7, PageNumber = 2, ColumnNumber = 3 };
            Conformance.Equal("North", engine.Evaluate("[Sales.Row]", c).AsString());
            Conformance.Equal("Cover", engine.Evaluate("Sales.Page", c).AsString());
            Conformance.Equal("Quarter", engine.Evaluate("Sales.ColumnNumber", c).AsString());
            Conformance.Equal(7m, engine.Evaluate("RowNumber", c).AsNumber());
            Conformance.Equal(2m, engine.Evaluate("PageN", c).AsNumber());
        });
    }
}
