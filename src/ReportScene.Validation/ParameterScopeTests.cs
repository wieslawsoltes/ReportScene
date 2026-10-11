using ReportScene.Expressions;
using ReportScene.Model;

namespace ReportScene.Validation;

public static class ParameterScopeTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("parameter scope: " + name, true); } catch (Exception e) { return new("parameter scope: " + name, false, e.ToString()); } }
        yield return Test("missing explicit parameter cannot resolve a row field", () =>
        {
            var context = new EvaluationContext { Row = new() { ["Amount"] = 25 } };
            Conformance.Throws<ExpressionException>(() => new ExpressionEngine().Evaluate("Parameters.Amount", context));
            Conformance.Equal(25m, context.Resolve("Amount").Number);
        });
        yield return Test("missing explicit parameter cannot resolve a system variable", () =>
        {
            var context = new EvaluationContext { PageNumber = 7 };
            Conformance.Throws<ExpressionException>(() => context.Resolve("Parameters.PageNumber"));
            Conformance.Equal(7m, context.Resolve("PageNumber").Number);
        });
        yield return Test("explicit null parameter remains null rather than falling through", () =>
        {
            var context = new EvaluationContext { Row = new() { ["Amount"] = 25 }, Parameters = new Dictionary<string, Value>(StringComparer.OrdinalIgnoreCase) { ["Amount"] = Value.Null } };
            Conformance.Equal(true, context.Resolve("parameters.AMOUNT").IsNull);
        });
        yield return Test("child parameter overrides stay local and parent lookup remains explicit", () =>
        {
            var parent = new EvaluationContext { Parameters = new Dictionary<string, Value> { ["Factor"] = 2 } };
            var child = parent with { Parent = parent, Parameters = new Dictionary<string, Value> { ["Factor"] = 3 } };
            Conformance.Equal(3m, child.Resolve("Parameters.Factor").Number); Conformance.Equal(2m, child.Resolve("Parent.Parameters.Factor").Number);
        });
    }
}
