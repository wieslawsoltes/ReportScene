using ReportScene.Designer;
using ReportScene.Model;

namespace ReportScene.Validation;

public static class DesignerOutlineTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("designer outline: " + name, true); } catch (Exception e) { return new("designer outline: " + name, false, e.ToString()); } }
        DesignerOutline Fixture() => new([new("p", null, "Page", OutlineKind.Page), new("b", "p", "Details", OutlineKind.Band),
            new("a", "b", "Alpha", OutlineKind.Object), new("z", "b", "Zulu", OutlineKind.Object), new("q", null, "Other", OutlineKind.Page)]);
        yield return Test("leaf search retains ancestors but not unrelated siblings", () => Conformance.Equal("p,b,a", string.Join(',', Fixture().Filter("ALPHA").Select(e => e.Key))));
        yield return Test("matching a parent includes its subtree", () => Conformance.Equal("p,b,a,z", string.Join(',', Fixture().Filter("Details").Select(e => e.Key))));
        yield return Test("empty and failed searches are exact", () => { var f = Fixture(); Conformance.Equal(5, f.Filter(" ").Count); Conformance.Equal(0, f.Filter("absent").Count); });
        yield return Test("repeated query reuses the immutable projection", () => { var f = Fixture(); var first = f.Filter("Alpha"); Conformance.Equal(true, ReferenceEquals(first, f.Filter("Alpha"))); Conformance.Equal(1, f.QueryScanCount); });
        yield return Test("cancellation does not replace previous cached results", () =>
        { var f = Fixture(); var old = f.Filter("Alpha"); using var c = new CancellationTokenSource(); c.Cancel(); Conformance.Throws<OperationCanceledException>(() => f.Filter("Zulu", c.Token)); Conformance.Equal(true, ReferenceEquals(old, f.Filter("Alpha"))); });
        yield return Test("invalid graph ordering and duplicate keys are rejected", () =>
        {
            Conformance.Throws<ArgumentException>(() => new DesignerOutline([new("x", "missing", "X", OutlineKind.Object)]));
            Conformance.Throws<ArgumentException>(() => new DesignerOutline([new("x", null, "X", OutlineKind.Page), new("x", null, "Y", OutlineKind.Page)]));
        });
        yield return Test("report hierarchy retains nested header child and footer owners", () =>
        {
            var doc = new ReportDocument { Pages = [new() { Bands = [new() { Id = "d", Header = new() { Id = "h" }, Footer = new() { Id = "f" }, Children = [new() { Id = "c", Items = [new TextItem { Id = "t" }] }] }] }] };
            var f = DesignerOutline.Report(doc); Conformance.Equal("band:d", f.Find("band:h")!.ParentKey!); Conformance.Equal("band:d", f.Find("band:f")!.ParentKey!); Conformance.Equal("band:c", f.Find("item:t")!.ParentKey!);
        });
        yield return Test("geometry and text changes do not rebuild outline entries", () =>
        {
            var item = new TextItem { Id = "t", Name = "Caption" }; var doc = new ReportDocument { Pages = [new() { Bands = [new() { Items = [item] }] }] };
            var old = DesignerOutline.Report(doc); doc.Pages[0].Bands[0].Items[0] = item with { Bounds = item.Bounds.Offset(5, 5), Text = "changed" };
            Conformance.Equal(true, old.HasSameEntries(DesignerOutline.Report(doc)));
            doc.Pages[0].Bands[0].Items[0] = item with { Name = "Renamed" }; Conformance.Equal(false, old.HasSameEntries(DesignerOutline.Report(doc))); Conformance.Equal("Caption", old.Find("item:t")!.Label);
        });
        yield return Test("data tree tracks parameter names and qualifies reserved fields", () =>
        {
            var doc = new ReportDocument { Data = [new() { Name = "Sales", Columns = ["RowNumber"] }] };
            var old = DesignerOutline.Data(doc); Conformance.Equal("[Sales.RowNumber]", old.Find("table:0/field:0")!.Expression!);
            doc.Parameters.Add(new("Rate", Value.From(1m))); var current = DesignerOutline.Data(doc); Conformance.Equal(false, old.HasSameEntries(current)); Conformance.Equal("[Parameters.Rate]", current.Find("parameter:Rate")!.Expression!);
        });
        yield return Test("structural equality is not an ambiguous concatenated fingerprint", () =>
        {
            var first = new DesignerOutline([new("ab", null, "c", OutlineKind.Page)]); var second = new DesignerOutline([new("a", null, "bc", OutlineKind.Page)]);
            Conformance.Equal(false, first.HasSameEntries(second));
        });
        yield return Test("large leaf search keeps only the matching path", () =>
        {
            var entries = new List<DesignerOutlineEntry> { new("root", null, "Report", OutlineKind.Page) };
            for (int n = 0; n < 20000; n++) entries.Add(new("item" + n, "root", "Label_" + n.ToString("D5"), OutlineKind.Object));
            var f = new DesignerOutline(entries); Conformance.Equal(2, f.Filter("Label_12345").Count); Conformance.Equal(1, f.QueryScanCount);
        });
    }
}
