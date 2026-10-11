using ReportScene.Model;

namespace ReportScene.Validation;

public static class SubreportGraphTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action)
        { try { action(); return new("subreport graph: " + name, true); } catch (Exception e) { return new("subreport graph: " + name, false, e.ToString()); } }
        ReportPage Page(string name, params string[] targets) => new()
        { Name = name, Bands = [new() { Kind = BandKind.ReportTitle, Items = targets.Select(t => (ReportItem)new SubreportItem { PageName = t }).ToList() }] };
        yield return Test("null target returns diagnostics rather than throwing during graph construction", () =>
        {
            var d = new ReportDocument { Pages = [Page("Main", new string[] { null! })] };
            Conformance.Equal(true, DocumentValidator.Validate(d).Any(e => e.Code == "RS1500"));
        });
        yield return Test("shared targets use longest depth instead of first visited depth", () =>
        {
            var pages = new List<ReportPage> { Page("Tail") };
            for (int n = 0; n < 33; n++) pages.Add(Page("Chain" + n, n == 0 ? "Tail" : "Chain" + (n - 1)));
            var d = new ReportDocument { Pages = pages };
            Conformance.Equal(true, DocumentValidator.Validate(d).Any(e => e.Message.Contains("nesting")));
        });
        yield return Test("shared acyclic targets are valid", () =>
        {
            var d = new ReportDocument { Pages = [Page("Main", "A", "B"), Page("A", "Tail"), Page("B", "Tail"), Page("Tail")] };
            Conformance.Equal(0, DocumentValidator.Validate(d).Count);
        });
        yield return Test("unused page cycles are still diagnosed", () =>
        {
            var d = new ReportDocument { Pages = [Page("Main"), Page("A", "B"), Page("B", "A")] };
            Conformance.Equal(true, DocumentValidator.Validate(d).Any(e => e.Message.Contains("cycle")));
        });
        yield return Test("binding names have a bounded size", () =>
        {
            var d = new ReportDocument { Pages = [Page("Main", "Detail"), Page("Detail")] };
            var item = (SubreportItem)d.Pages[0].Bands[0].Items[0]; item.ParameterBindings.Add(new(new string('x', 257), "1"));
            Conformance.Equal(true, DocumentValidator.Validate(d).Any(e => e.Code == "RS1500"));
        });
    }
}
