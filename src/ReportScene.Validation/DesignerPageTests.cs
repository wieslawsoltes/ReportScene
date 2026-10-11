using ReportScene.Designer;
using ReportScene.Model;
using ReportScene.Serialization;

namespace ReportScene.Validation;

public static class DesignerPageTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("designer page: " + name, true); } catch (Exception e) { return new("designer page: " + name, false, e.ToString()); } }
        DesignerSession Fixture() => new(new() { Pages = [new() { Name = "Main", Bands = [new() { Id = "b", Kind = BandKind.ReportTitle, Items = [new TextItem { Id = "t" }] }] }, new() { Name = "Detail" }] });
        yield return Test("page selection is a navigation operation", () =>
        { var s = Fixture(); s.Select("t"); long revision = s.Revision; s.SelectPage(); Conformance.Equal(0, s.SelectedIds.Count); Conformance.Equal(true, s.SelectedBand is null); Conformance.Equal(revision, s.Revision); Conformance.Equal(false, s.IsDirty); });
        yield return Test("page edits and undo preserve explicit page selection", () =>
        { var s = Fixture(); s.SelectPage(); double width = s.Page.Width; s.UpdatePageProperties(s.Page with { Width = 800 }); Conformance.Equal(true, s.SelectedBand is null); s.Undo(); Conformance.Equal(width, s.Page.Width); Conformance.Equal(true, s.SelectedBand is null); s.Redo(); Conformance.Equal(800d, s.Page.Width); });
        yield return Test("invalid paper and column geometry cannot dirty or partly edit the page", () =>
        {
            var s = Fixture(); string old = ReportJson.Serialize(s.Document);
            foreach (var page in new[] { s.Page with { Width = 20 }, s.Page with { Height = double.NaN }, s.Page with { Columns = 0 }, s.Page with { ColumnGap = double.PositiveInfinity } })
                Conformance.Throws<ArgumentException>(() => s.UpdatePageProperties(page));
            Conformance.Equal(old, ReportJson.Serialize(s.Document)); Conformance.Equal(0, s.HistoryCount);
        });
        yield return Test("blank and case-colliding page names are rejected", () =>
        { var s = Fixture(); Conformance.Throws<ArgumentException>(() => s.UpdatePageProperties(s.Page with { Name = " " })); Conformance.Throws<ArgumentException>(() => s.UpdatePageProperties(s.Page with { Name = "detail" })); Conformance.Equal(0, s.HistoryCount); });
        yield return Test("page properties preserve subreport rename propagation", () =>
        {
            var s = Fixture(); s.Page.Bands[0].Items.Add(new SubreportItem { PageName = "Detail" }); s.SetPage(1); s.SelectPage(); s.UpdatePageProperties(s.Page with { Name = "Renamed" });
            Conformance.Equal("Renamed", DocumentTree.AllItems(s.Document).OfType<SubreportItem>().Single().PageName); s.Undo(); Conformance.Equal("Detail", DocumentTree.AllItems(s.Document).OfType<SubreportItem>().Single().PageName);
        });
        yield return Test("compact band layout preserves every object and hit coordinate", () =>
        {
            var page = Fixture().Page; var full = DesignLayout.Create(page); var compact = DesignLayout.Create(page, false);
            Conformance.Equal(true, compact.Paper.Height < full.Paper.Height); Conformance.Equal(true, compact.Objects.SequenceEqual(full.Objects)); Conformance.Equal(true, compact.Bands.SequenceEqual(full.Bands));
            var item = compact.Objects.Single(); Conformance.Equal("t", compact.HitTest(item.Bounds.X + 1, item.Bounds.Y + 1)!.Item.Id);
        });
        yield return Test("large band content is never clipped to physical paper height in the designer", () =>
        { var page = Fixture().Page; page.Bands[0].Items[0] = new TextItem { Bounds = new(0, 1500, 100, 200) }; var compact = DesignLayout.Create(page, false); Conformance.Equal(true, compact.Paper.Bottom > compact.Objects.Single().Bounds.Bottom); });
        yield return Test("page properties cannot silently replace the band graph", () =>
        { var s = Fixture(); Conformance.Throws<ArgumentException>(() => s.UpdatePageProperties(s.Page with { Bands = [] })); Conformance.Equal(1, s.Page.Bands.Count); Conformance.Equal(0, s.HistoryCount); });
        yield return Test("no-op page property updates do not create undo entries", () =>
        { var s = Fixture(); s.UpdatePageProperties(s.Page with { }); Conformance.Equal(0, s.HistoryCount); });
    }
}
