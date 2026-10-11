using ReportScene.Designer;
using ReportScene.Model;

namespace ReportScene.Validation;

public static class SubreportNavigationTests
{
    private static DesignerSession Session() => new(new ReportDocument { Pages = [new() { Name = "Main", Bands = [new() { Name = "MainBand", Kind = BandKind.ReportTitle }] }] });
    private static ReportPage Target() => new() { Name = "Target", Bands = [new() { Name = "TargetBand", Kind = BandKind.ReportTitle }] };
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action)
        { try { action(); return new("subreport navigation: " + name, true); } catch (Exception e) { return new("subreport navigation: " + name, false, e.ToString()); } }
        yield return Test("undo after navigating to a created target restores the owner band", () =>
        {
            var s = Session(); string owner = s.SelectedBand!.Id; var item = s.AddSubreport(Target(), new(0, 30, 200, 24));
            s.OpenSubreportPage(item.Id); s.Undo();
            Conformance.Equal("Main", s.Page.Name); Conformance.Equal(owner, s.SelectedBand!.Id);
            s.Add(new TextItem { Text = "Still editable" }); Conformance.Equal(1, s.Page.Bands[0].Items.Count);
        });
        yield return Test("redo returns to the owning reference location", () =>
        {
            var s = Session(); var item = s.AddSubreport(Target(), new(0, 30, 200, 24)); s.OpenSubreportPage(item.Id);
            s.Undo(); s.Redo(); Conformance.Equal("Main", s.Page.Name); Conformance.Equal(item.Id, s.Selection.Single().Id);
            Conformance.Equal(true, s.SelectedBand!.Items.Any(i => i.Id == item.Id));
        });
        yield return Test("page rename undo returns to that page after navigation elsewhere", () =>
        {
            var s = Session(); var item = s.AddSubreport(Target(), new(0, 30, 200, 24)); s.OpenSubreportPage(item.Id);
            s.UpdatePage(s.Page with { Name = "Renamed" }); s.SetPage(0); s.Undo();
            Conformance.Equal("Target", s.Page.Name); Conformance.Equal("TargetBand", s.SelectedBand!.Name);
            s.Redo(); Conformance.Equal("Renamed", s.Page.Name);
        });
    }
}
