using ReportScene.Designer;
using ReportScene.Expressions;
using ReportScene.Model;

namespace ReportScene.Validation;

public static class AuthoringIntegrationTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult result;
        try
        {
            var variables = DesignerOutline.Data(new()).Entries.Where(e => e.Kind == OutlineKind.SystemVariable).ToArray();
            var expressions = new ExpressionEngine(); var context = new EvaluationContext { ColumnNumber = 3 };
            Conformance.Equal(6, variables.Length);
            foreach (var entry in variables) Conformance.Equal(false, expressions.Evaluate(entry.Expression!, context).IsNull);
            Conformance.Equal(3m, expressions.Evaluate(variables.Single(e => e.Label == "ColumnNumber").Expression!, context).Number);
            result = new("authoring integration: every system-variable tree insertion has an executable expression", true);
        }
        catch (Exception e) { result = new("authoring integration: every system-variable tree insertion has an executable expression", false, e.ToString()); }
        yield return result;
    }
}
