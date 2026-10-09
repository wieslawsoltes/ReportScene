using System.Xml;
using ReportScene.Model;
using ReportScene.Engine;
using ReportScene.Serialization;
using ReportScene.Designer;
using ReportScene.Samples;

namespace ReportScene.Validation;

public static partial class Conformance
{
    static partial void MoreTests(Action<string, Action> test)
    {
        var engine = new ReportPreparer();
        ReportDocument Small(int count = 8, double rowHeight = 25) => new()
        {
            Name = "Small", Data = [new() { Name = "Rows", Columns = ["N"], Rows = Enumerable.Range(1, count).Select(n => new DataRow { ["N"] = n }).ToList() }],
            Pages = [new() { Width = 220, Height = 160, Margins = new(10, 10, 10, 10), Bands = [new() { Kind = BandKind.PageHeader, Name = "Header", Height = 20, Items = [new TextItem { Name = "Page", Text = "[PageN]/[TotalPages]", Bounds = new(0, 0, 100, 20) }] }, new() { Name = "Data", Kind = BandKind.Data, DataSource = "Rows", Height = rowHeight, CanSplit = false, Items = [new TextItem { Name = "Row", Text = "Row [Rows.N]", Bounds = new(0, 0, 150, 20) }] }, new() { Name = "Footer", Kind = BandKind.PageFooter, Height = 20, Items = [new TextItem { Text = "Footer", Bounds = new(0, 0, 100, 20) }] }] }]
        };
        foreach (var name in SampleReports.Names)
        {
            test("sample validates: " + name, () => Equal(0, DocumentValidator.Validate(SampleReports.Create(name)).Count));
            test("sample prepares: " + name, () => Equal(true, engine.Prepare(SampleReports.Create(name)).Pages.Count > 0));
            test("native round-trip: " + name, () => { var original = SampleReports.Create(name); var copy = ReportJson.Clone(original); Equal(ReportJson.Serialize(original), ReportJson.Serialize(copy)); });
        }
        test("pagination preserves every row", () => { var p = engine.Prepare(Small(17)); Equal(17, p.Search("Row ").Count()); });
        test("pagination exact page count", () => Equal(2, engine.Prepare(Small()).Pages.Count));
        test("page number and total pages resolve", () => { var p = engine.Prepare(Small()); Equal(true, p.Pages[0].Items.Any(i => i.Text == "1/2")); Equal(true, p.Pages[1].Items.Any(i => i.Text == "2/2")); });
        test("page footer repeats", () => { var p = engine.Prepare(Small(17)); Equal(p.Pages.Count, p.Search("Footer").Count()); });
        test("empty dataset is valid", () => Equal(1, engine.Prepare(Small(0)).Pages.Count));
        test("maximum pages enforced", () => Throws<ReportPreparationException>(() => engine.Prepare(Small(100), options: new(MaximumPages: 1))));
        test("maximum instances enforced", () => Throws<ReportPreparationException>(() => engine.Prepare(Small(100), options: new(MaximumBandInstances: 5))));
        test("maximum prepared items enforced", () => Throws<ReportPreparationException>(() => engine.Prepare(Small(100), options: new(MaximumItems: 5))));
        test("cancellation", () => { using var c = new CancellationTokenSource(); c.Cancel(); Throws<OperationCanceledException>(() => engine.Prepare(Small(), cancellationToken: c.Token)); });
        test("oversized unsplittable band rejected", () => Throws<ReportPreparationException>(() => engine.Prepare(Small(1, 200))));
        test("split band terminates", () => { var d = Small(1, 240); d.Pages[0].Bands[1] = d.Pages[0].Bands[1] with { CanSplit = true }; Equal(true, engine.Prepare(d).Pages.Count >= 3); });
        test("filtering", () => { var d = Small(10); d.Pages[0].Bands[1] = d.Pages[0].Bands[1] with { Filter = "N > 7" }; Equal(3, engine.Prepare(d).Search("Row ").Count()); });
        test("stable descending sort", () => { var d = Small(); d.Pages[0].Bands[1].Sort.Add(new("N", true)); Equal("Row 8", engine.Prepare(d).Search("Row ").First().Text); });
        test("required parameters", () => { var d = SampleReports.Invoice(); Throws<ReportPreparationException>(() => engine.Prepare(d, new Dictionary<string, Value> { ["Customer"] = Value.Null })); });
        test("missing data source rejected", () => { var d = Small(); d.Data.Clear(); Throws<ReportPreparationException>(() => engine.Prepare(d)); });
        test("invalid margins rejected", () => { var d = Small(); d.Pages[0] = d.Pages[0] with { Margins = new(300, 0, 0, 0) }; Equal(true, DocumentValidator.Validate(d).Count > 0); });
        test("duplicate object IDs rejected", () => { var d = Small(); d.Pages[0].Bands[1].Items.Add(d.Pages[0].Bands[1].Items[0]); Equal(true, DocumentValidator.Validate(d).Count > 0); });
        test("cyclic bands rejected before traversal", () => { var d = Small(); d.Pages[0].Bands[1].Children.Add(d.Pages[0].Bands[1]); Equal(true, DocumentValidator.Validate(d).Count > 0); });
        test("grouped rows are not lost", () => Equal(90, engine.Prepare(SampleReports.GroupedSales()).Search("SO-").Count()));
        test("group outlines", () => Equal(3, engine.Prepare(SampleReports.GroupedSales()).Outline.Count()));
        test("master/detail relation filters children", () => Equal(18, engine.Prepare(SampleReports.MasterDetail()).Search("ORD-").Count()));
        test("multi-column layout", () => { var p = engine.Prepare(SampleReports.Labels()); var x = p.Pages[0].Items.Where(i => i.Source.Name == "Name").Select(i => Math.Round(i.Bounds.X)).Distinct().Count(); Equal(3, x); });
        test("chart points prepared", () => Equal(6, engine.Prepare(SampleReports.TablesAndCharts()).Pages.SelectMany(p => p.Items).Single(i => i.Source is ChartItem).Points!.Count));
        test("table cells expanded", () => Equal(true, engine.Prepare(SampleReports.TablesAndCharts()).Search("January").Any()));
        test("native scalar case-insensitivity retained", () => { var d = ReportJson.Clone(SampleReports.Invoice()); Equal(true, d.Data[0].Rows[0].ContainsKey("description")); });
        test("native unknown version rejected", () => Throws<FormatException>(() => ReportJson.Deserialize("{\"formatVersion\":99}")));
        test("FRX basic import units", () => { var d = FrxSerializer.Import("<Report><ReportPage Name='Page1' PaperWidth='210' PaperHeight='297'><ReportTitleBand Name='Title' Height='40'><TextObject Name='Text1' Left='16' Width='200' Height='20' Text='Hello'/></ReportTitleBand></ReportPage></Report>"); Equal(true, d.Success); Equal(12d, d.Document.Pages[0].Bands[0].Items[0].Bounds.X); });
        test("FRX forbids DTD", () => Throws<XmlException>(() => FrxSerializer.Import("<!DOCTYPE Report [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><Report>&x;</Report>")));
        test("FRX scripts diagnosed", () => Equal(false, FrxSerializer.Import("<Report><ScriptText>System.IO.File.ReadAllText(\"x\");</ScriptText><ReportPage Name='Page1'/></Report>").Success));
        test("FRX unsupported object diagnosed", () => Equal(false, FrxSerializer.Import("<Report><ReportPage Name='Page1'><ReportTitleBand Name='Title'><RichObject Name='Rich1'/></ReportTitleBand></ReportPage></Report>").Success));
        test("FRX invoice round-trip object count", () => { var d = SampleReports.Invoice(); var imported = FrxSerializer.Import(FrxSerializer.Export(d)); Equal(true, imported.Success); Equal(DocumentTree.AllItems(d).Count(), DocumentTree.AllItems(imported.Document).Count()); });
        test("FRX refuses unsupported chart export", () => Throws<NotSupportedException>(() => FrxSerializer.Export(SampleReports.TablesAndCharts())));
        test("designer insertion is undoable", () => { var s = new DesignerSession(Small()); var before = DocumentTree.AllItems(s.Document).Count(); s.Add(new TextItem()); Equal(before + 1, DocumentTree.AllItems(s.Document).Count()); s.Undo(); Equal(before, DocumentTree.AllItems(s.Document).Count()); s.Redo(); Equal(before + 1, DocumentTree.AllItems(s.Document).Count()); });
        test("designer deletion restores order", () => { var s = new DesignerSession(Small()); s.SelectAll(); string before = ReportJson.Serialize(s.Document); s.DeleteSelection(); s.Undo(); Equal(before, ReportJson.Serialize(s.Document)); });
        test("designer drag is one undo entry", () => { var s = new DesignerSession(Small()); var item = s.Page.Bands[1].Items[0]; s.Select(item.Id); s.BeginDrag(); for (int i = 0; i < 100; i++) s.PreviewDrag(i, i); s.CommitDrag(); Equal(1, s.HistoryCount); s.Undo(); Equal(item.Bounds, s.Selection.Single().Bounds); });
        test("designer canceled drag creates no history", () => { var s = new DesignerSession(Small()); var item = s.Page.Bands[1].Items[0]; s.Select(item.Id); s.BeginDrag(); s.PreviewDrag(20, 20); s.CancelDrag(); Equal(0, s.HistoryCount); Equal(item.Bounds, s.Selection.Single().Bounds); });
        test("designer paste regenerates IDs", () => { var s = new DesignerSession(Small()); var item = s.Page.Bands[1].Items[0]; s.Select(item.Id); s.Paste(s.Copy()); Equal(false, s.Selection.Single().Id == item.Id); });
        test("designer saved revision follows undo", () => { var s = new DesignerSession(Small()); s.MarkSaved(); s.Add(new TextItem()); Equal(true, s.IsDirty); s.Undo(); Equal(false, s.IsDirty); });
        test("designer redo branch discarded", () => { var s = new DesignerSession(Small()); s.Add(new TextItem()); s.Undo(); s.Add(new ShapeItem()); Equal(false, s.CanRedo); });
        test("designer move clamps negative bounds", () => { var s = new DesignerSession(Small()); s.Select(s.Page.Bands[1].Items[0].Id); s.Move(-100, -100); Equal(0d, s.Selection.Single().Bounds.X); });
        test("designer hit testing respects z-order", () => { var d = Small(); var b = d.Pages[0].Bands[1]; b.Items.Add(new TextItem { Name = "Top", Bounds = b.Items[0].Bounds }); var l = DesignLayout.Create(d.Pages[0]); var o = l.Objects.Last(x => x.Band.Id == b.Id); Equal("Top", l.HitTest(o.Bounds.X + 3, o.Bounds.Y + 3)!.Item.Name); });
        test("designer page changes undo", () => { var s = new DesignerSession(Small()); s.AddPage(); Equal(2, s.Document.Pages.Count); s.Undo(); Equal(1, s.Document.Pages.Count); });
        AdditionalTests(test);
    }
    static partial void AdditionalTests(Action<string, Action> test);
}
