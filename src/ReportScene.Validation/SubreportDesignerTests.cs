using ReportScene.Designer;
using ReportScene.Engine;
using ReportScene.Model;
using ReportScene.Serialization;

namespace ReportScene.Validation;

public static class SubreportDesignerTests
{
    private static ReportPage Target(string name) => new() { Name = name, Bands = [new() { Name = name + "Band", Kind = BandKind.ReportTitle, Height = 30, Items = [new TextItem { Name = name + "Text", Text = "Child text" }] }] };
    private static DesignerSession Session() => new(new ReportDocument { Pages = [new() { Name = "Main", Bands = [new() { Name = "MainBand", Kind = BandKind.ReportTitle, Height = 20 }] }] });
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action)
        { try { action(); return new("subreport designer: " + name, true); } catch (Exception e) { return new("subreport designer: " + name, false, e.ToString()); } }
        void Equal<T>(T expected, T actual) => Conformance.Equal(expected, actual);
        yield return Test("reference and page insertion are one transaction", () =>
        {
            var s = Session(); var item = s.AddSubreport(Target("Detail"), new(0, 20, 200, 24));
            Equal(1, s.HistoryCount); Equal(2, s.Document.Pages.Count); Equal(item.Id, s.Selection.Single().Id);
            s.Undo(); Equal(1, s.Document.Pages.Count); Equal(0, s.Page.Bands[0].Items.Count);
            s.Redo(); Equal(2, s.Document.Pages.Count); Equal(item.Id, s.Selection.Single().Id);
            Equal(1, new ReportPreparer().Prepare(s.Document).Pages.Count);
        });
        yield return Test("new target cannot shadow an existing page", () =>
        {
            var s = Session(); Conformance.Throws<ArgumentException>(() => s.AddSubreport(Target("MAIN"), new(0, 20, 200, 24)));
            Equal(0, s.HistoryCount); Equal(1, s.Document.Pages.Count);
        });
        yield return Test("invalid insertion leaves the original document intact", () =>
        {
            var s = Session(); var before = s.Document;
            Conformance.Throws<ArgumentException>(() => s.AddSubreport(Target("Detail"), new(0, 20, -1, 24)));
            Equal(true, ReferenceEquals(before, s.Document)); Equal(0, s.HistoryCount);
        });
        yield return Test("renaming a referenced page updates every reference atomically", () =>
        {
            var s = Session(); var sub = s.AddSubreport(Target("Detail"), new(0, 20, 200, 24));
            s.Add(new SubreportItem { PageName = "Detail", Bounds = new(230, 20, 200, 24) });
            s.SetPage(1); s.UpdatePage(s.Page with { Name = "Renamed" });
            Equal(true, DocumentTree.AllItems(s.Document).OfType<SubreportItem>().All(i => i.PageName == "Renamed"));
            s.Undo(); Equal("Detail", s.Page.Name); Equal(true, DocumentTree.AllItems(s.Document).OfType<SubreportItem>().All(i => i.PageName == "Detail"));
            s.Redo(); Equal("Renamed", s.Page.Name); Equal(0, DocumentValidator.Validate(s.Document).Count);
        });
        yield return Test("duplicate page rename is rejected without recording history", () =>
        {
            var s = Session(); s.AddSubreport(Target("Detail"), new(0, 20, 200, 24)); s.SetPage(1); int history = s.HistoryCount;
            Conformance.Throws<ArgumentException>(() => s.UpdatePage(s.Page with { Name = "Main" }));
            Equal(history, s.HistoryCount); Equal("Detail", s.Page.Name);
        });
        yield return Test("retargeting a reference to its owner is rejected before mutation", () =>
        {
            var s = Session(); var sub = s.AddSubreport(Target("Detail"), new(0, 20, 200, 24));
            Conformance.Throws<ArgumentException>(() => s.UpdateSelection("Bad target", i => ((SubreportItem)i) with { PageName = "Main" }));
            Equal("Detail", ((SubreportItem)s.Selection.Single()).PageName); Equal(1, s.HistoryCount);
        });
        yield return Test("parameter changes apply and undo as one edit", () =>
        {
            var s = Session(); s.AddSubreport(Target("Detail"), new(0, 20, 200, 24));
            s.UpdateSelection("Bindings", i => ((SubreportItem)i) with { PrintOnParent = true, ParameterBindings = [new("Factor", "3")] });
            Equal(1, ((SubreportItem)s.Selection.Single()).ParameterBindings.Count);
            s.Undo(); Equal(0, ((SubreportItem)s.Selection.Single()).ParameterBindings.Count);
            s.Redo(); Equal(true, ((SubreportItem)s.Selection.Single()).PrintOnParent);
        });
        yield return Test("target navigation does not mutate history or save state", () =>
        {
            var s = Session(); var sub = s.AddSubreport(Target("Detail"), new(0, 20, 200, 24)); s.MarkSaved();
            s.OpenSubreportPage(sub.Id); Equal("Detail", s.Page.Name); Equal(1, s.HistoryCount); Equal(false, s.IsDirty);
            s.SetPage(0); Equal(false, s.IsDirty);
        });
        yield return Test("clipboard retains target and bindings but creates a new object ID", () =>
        {
            var s = Session(); var sub = s.AddSubreport(Target("Detail"), new(0, 20, 200, 24));
            s.UpdateSelection("Bindings", i => ((SubreportItem)i) with { ParameterBindings = [new("Factor", "2")] });
            s.Paste(s.Copy()); var copy = (SubreportItem)s.Selection.Single();
            Equal(false, copy.Id == sub.Id); Equal("Detail", copy.PageName); Equal("2", copy.ParameterBindings[0].Expression);
            Equal(2, s.Document.Pages.Count);
        });
        yield return Test("foreign clipboard references are refused without adding objects", () =>
        {
            var s = Session(); string clipboard = ReportJson.SerializeItems([new SubreportItem { PageName = "Absent" }]);
            Conformance.Throws<ArgumentException>(() => s.Paste(clipboard)); Equal(0, s.HistoryCount); Equal(0, s.Page.Bands[0].Items.Count);
        });
        yield return Test("copying a reference into its own target cannot create a cycle", () =>
        {
            var s = Session(); s.AddSubreport(Target("Detail"), new(0, 20, 200, 24)); string clipboard = s.Copy(); s.SetPage(1);
            Conformance.Throws<ArgumentException>(() => s.Paste(clipboard)); Equal(1, s.HistoryCount); Equal(1, s.Page.Bands[0].Items.Count);
        });
        yield return Test("structural edits compose with earlier item deltas", () =>
        {
            var s = Session(); var text = s.Add(new TextItem { Name = "Before", Text = "Original" });
            s.UpdateSelection("Text", i => ((TextItem)i) with { Text = "Changed" });
            s.AddSubreport(Target("Detail"), new(0, 30, 200, 24)); s.SetPage(1); s.UpdatePage(s.Page with { Name = "NewName" });
            s.Undo(); s.Undo(); s.Undo();
            Equal("Original", DocumentTree.AllItems(s.Document).OfType<TextItem>().Single(i => i.Id == text.Id).Text);
            s.Redo(); s.Redo(); s.Redo();
            Equal("Changed", DocumentTree.AllItems(s.Document).OfType<TextItem>().Single(i => i.Id == text.Id).Text);
            Equal("NewName", DocumentTree.AllItems(s.Document).OfType<SubreportItem>().Single().PageName);
        });
        yield return Test("nested target band settings are editable and reversible", () =>
        {
            var s = Session(); var child = new Band { Name = "Child", Kind = BandKind.Child, Height = 20 };
            var page = Target("Detail"); page.Bands[0].Children.Add(child);
            s.AddSubreport(page, new(0, 20, 200, 24)); s.SetPage(1); s.SelectBand(child.Id);
            s.UpdateBand(child with { Height = 45 }); Equal(45d, s.SelectedBand!.Height);
            s.Undo(); Equal(20d, s.SelectedBand!.Height); s.Redo(); Equal(45d, s.SelectedBand!.Height);
        });
        yield return Test("structural transactions retain the original immutable data binding", () =>
        {
            var s = Session(); var data = new DataTable { Name = "Rows", Rows = [new() { ["N"] = 1 }] }; s.ReplaceData(data);
            s.AddSubreport(Target("Detail"), new(0, 20, 200, 24)); s.SetPage(1); s.UpdatePage(s.Page with { Name = "Other" });
            Equal(true, ReferenceEquals(data, s.Document.Data.Single()));
        });
        yield return Test("removing the final reference makes its retained page printable", () =>
        {
            var s = Session(); s.AddSubreport(Target("Detail"), new(0, 20, 200, 24)); s.DeleteSelection();
            Equal(2, SubreportGraph.PrintablePages(s.Document).Count); s.Undo(); Equal(1, SubreportGraph.PrintablePages(s.Document).Count);
        });
        yield return Test("page name allocation avoids existing custom numbering", () =>
        {
            var s = Session(); s.AddSubreport(Target("Subreport1"), new(0, 20, 200, 24));
            Equal("Subreport2", s.UniquePageName("Subreport"));
        });
    }
}
