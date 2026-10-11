using ReportScene.Engine;
using ReportScene.Model;
using ReportScene.Serialization;

namespace ReportScene.Validation;

public static class SubreportTests
{
    private static TextItem Text(string name, string value, double width = 170) => new()
    { Name = name, Text = value, Bounds = new(0, 0, width, 20), Style = new() { FontSize = 9, Padding = 1 } };
    public static ReportDocument Parallel(int leftRows = 10, int rightRows = 7)
    {
        DataTable Table(string name, int count) => new() { Name = name, Columns = ["Label", "Amount"], Rows = Enumerable.Range(1, count).Select(n => new DataRow { ["Label"] = name + n, ["Amount"] = n }).ToList() };
        ReportPage Target(string name, string data, double rowHeight) => new()
        {
            Name = name, Bands = [new() { Kind = BandKind.Data, Name = data + "Band", DataSource = data, Height = rowHeight, CanSplit = false,
                Items = [Text(data + "Value", "[" + data + ".Label] / [PageN] / [TotalPages] / [ColumnNumber]")] }]
        };
        return new()
        {
            Name = "Parallel subreports", Data = [Table("Alpha", leftRows), Table("Beta", rightRows)],
            Pages = [new()
            {
                Name = "Main", Width = 500, Height = 240, Margins = new(20, 20, 20, 20), Bands =
                [new() { Kind = BandKind.PageHeader, Name = "MainHeader", Height = 24, Items = [Text("MainHeader", "Main [PageN]/[TotalPages]")] },
                 new() { Kind = BandKind.ReportTitle, Name = "ParallelBand", Height = 44, Items =
                    [Text("Prefix", "Two independent flows"),
                     new SubreportItem { Name = "Left", PageName = "LeftPage", Bounds = new(0, 20, 210, 24) },
                     new SubreportItem { Name = "Right", PageName = "RightPage", Bounds = new(230, 20, 210, 24) }] },
                 new() { Kind = BandKind.ReportSummary, Name = "SummaryBand", Height = 20, Items = [Text("After", "AFTER SUBREPORTS")] },
                 new() { Kind = BandKind.PageFooter, Name = "MainFooter", Height = 20, Items = [Text("MainFooter", "Footer [PageN]/[TotalPages]")] }]
            }, Target("LeftPage", "Alpha", 26), Target("RightPage", "Beta", 31)]
        };
    }
    private static List<PreparedItem> Named(PreparedReport report, string name) => report.Pages.SelectMany(p => p.Items).Where(i => i.Source is TextItem && i.Source.Name == name).ToList();
    private static ReportDocument Inline(int rows = 3)
    {
        var report = Parallel(rows, 0); var main = report.Pages[0];
        main.Bands[1] = main.Bands[1] with { Height = 80, Items =
            [Text("Prefix", "Prefix"), new SubreportItem { Id = "inline", Name = "Inline", PageName = "LeftPage", PrintOnParent = true, Bounds = new(0, 20, 210, 24) },
             Text("Following", "FOLLOWING") with { Bounds = new(0, 50, 210, 20) }] };
        report.Pages.RemoveAt(2); return report;
    }
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action)
        { try { action(); return new("subreport: " + name, true); } catch (Exception e) { return new("subreport: " + name, false, e.ToString()); } }
        void Equal<T>(T expected, T actual) => Conformance.Equal(expected, actual);
        var preparer = new ReportPreparer();
        yield return Test("parallel fixtures validate", () => Equal(0, DocumentValidator.Validate(Parallel()).Count));
        yield return Test("referenced pages are not top-level pages", () => Equal("Main", SubreportGraph.PrintablePages(Parallel()).Single().Name));
        yield return Test("independent row heights use two physical pages", () => Equal(2, preparer.Prepare(Parallel()).Pages.Count));
        yield return Test("every left and right source row prints once", () => { var p = preparer.Prepare(Parallel()); Equal(10, Named(p, "AlphaValue").Count); Equal(7, Named(p, "BetaValue").Count); Equal(10, Named(p, "AlphaValue").Select(i => i.Text.Split(" / ")[0]).Distinct().Count()); });
        yield return Test("left and right lanes retain independent x positions", () => { var p = preparer.Prepare(Parallel()); Equal(true, Named(p, "AlphaValue").All(i => i.Bounds.X >= 20 && i.Bounds.Right <= 230)); Equal(true, Named(p, "BetaValue").All(i => i.Bounds.X >= 250 && i.Bounds.Right <= 460)); });
        yield return Test("continuation rows use final physical page numbers", () => { var p = preparer.Prepare(Parallel()); Equal(true, p.Pages.All(page => page.Items.Where(i => i.Source is TextItem && i.Source.Name == "AlphaValue").All(i => i.Text.Contains(" / " + page.Number + " / 2 / ")))); });
        yield return Test("parent headers and footers repeat without target duplication", () => { var p = preparer.Prepare(Parallel()); Equal(2, Named(p, "MainHeader").Count); Equal(2, Named(p, "MainFooter").Count); Equal(1, Named(p, "Prefix").Count); });
        yield return Test("following bands begin after the longest lane", () => { var p = preparer.Prepare(Parallel()); Equal(1, Named(p, "After").Count); var after = Named(p, "After").Single(); Equal(true, after.Bounds.Y >= 174); Equal(true, p.Pages[^1].Items.Contains(after)); });
        yield return Test("only one lane continuing does not repeat finished lane", () => { var p = preparer.Prepare(Parallel(20, 1)); Equal(20, Named(p, "AlphaValue").Count); Equal(1, Named(p, "BetaValue").Count); Equal(true, p.Pages.Count >= 4); });
        yield return Test("empty lane preserves the other stream", () => { var p = preparer.Prepare(Parallel(0, 7)); Equal(0, Named(p, "AlphaValue").Count); Equal(7, Named(p, "BetaValue").Count); });
        yield return Test("both empty lanes consume no placeholder height", () => { var p = preparer.Prepare(Parallel(0, 0)); Equal(1, p.Pages.Count); Equal(65d, Named(p, "After").Single().Bounds.Y); });
        yield return Test("target page order does not change printable root order", () => { var d = Parallel(); var root = d.Pages[0]; d.Pages.RemoveAt(0); d.Pages.Add(root); Equal(2, preparer.Prepare(d).Pages.Count); });
        yield return Test("unreferenced additional pages still print", () => { var d = Parallel(1, 1); d.Pages.Add(new() { Name = "Appendix", Bands = [new() { Kind = BandKind.ReportTitle, Items = [Text("Appendix", "APPENDIX")] }] }); Equal(2, preparer.Prepare(d).Pages.Count); });
        yield return Test("same target can be instantiated twice independently", () => { var d = Parallel(1, 1); d.Pages[0].Bands[1].Items[2] = ((SubreportItem)d.Pages[0].Bands[1].Items[2]) with { PageName = "LeftPage" }; d.Pages.RemoveAt(2); var values = Named(preparer.Prepare(d), "AlphaValue"); Equal(2, values.Count); Equal(2, values.Select(i => i.Source.Id).Distinct().Count()); });
        yield return Test("target page lookup is case-insensitive", () => { var d = Parallel(); d.Pages[0].Bands[1].Items[1] = ((SubreportItem)d.Pages[0].Bands[1].Items[1]) with { PageName = "leftpage" }; Equal(2, preparer.Prepare(d).Pages.Count); });
        yield return Test("short remaining space advances an unsplittable child row", () => { var d = Parallel(1, 0); d.Pages[0].Bands.Insert(1, new() { Kind = BandKind.Child, Height = 130 }); var p = preparer.Prepare(d); Equal(2, p.Pages.Count); Equal(true, p.Pages[1].Items.Any(i => i.Source.Name == "AlphaValue")); });
        yield return Test("main multi-column continuation retains physical page and column numbers", () => { var d = Parallel(); d.Pages[0] = d.Pages[0] with { Width = 1000, Columns = 2, ColumnGap = 20 }; var p = preparer.Prepare(d); Equal(1, p.Pages.Count); var values = Named(p, "AlphaValue"); Equal(true, values.Any(i => i.Text.EndsWith(" / 1 / 1 / 2"))); Equal(true, values.Any(i => i.Text.EndsWith(" / 1 / 1 / 1"))); });
        yield return Test("subreport data header repeats on its own continuation", () => { var d = Parallel(10, 1); var band = d.Pages[1].Bands[0]; d.Pages[1].Bands[0] = band with { Header = new() { Name = "AlphaHeader", Kind = BandKind.DataHeader, Height = 20, Items = [Text("AlphaHeader", "ALPHA")] } }; var p = preparer.Prepare(d); Equal(3, p.Pages.Count); Equal(3, Named(p, "AlphaHeader").Count); Equal(10, Named(p, "AlphaValue").Count); });
        yield return Test("target margins do not double-indent embedded content", () => { var d = Parallel(1, 1); d.Pages[1] = d.Pages[1] with { Margins = new(80, 90, 80, 90) }; Equal(21d, Named(preparer.Prepare(d), "AlphaValue").Single().Bounds.X); });
        yield return Test("nested independent subreport uses parent clipping and page schedule", () => { var d = Parallel(10, 1); var target = d.Pages[1] with { Name = "Nested" }; d.Pages[1] = new() { Name = "LeftPage", Bands = [new() { Kind = BandKind.ReportTitle, Height = 24, Items = [new SubreportItem { PageName = "Nested", Bounds = new(0, 0, 210, 24) }] }] }; d.Pages.Add(target); var p = preparer.Prepare(d); Equal(2, p.Pages.Count); Equal(10, Named(p, "AlphaValue").Count); });
        yield return Test("parameter bindings are isolated between parallel invocations", () =>
        {
            var d = Parallel(1, 1); d.Parameters.Add(new("Factor", 2));
            d.Pages[0].Bands[1].Items[1] = ((SubreportItem)d.Pages[0].Bands[1].Items[1]) with { ParameterBindings = [new("Factor", "3")] };
            d.Pages[1].Bands[0].Items[0] = Text("AlphaValue", "[Parameters.Factor * Alpha.Amount]"); d.Pages[2].Bands[0].Items[0] = Text("BetaValue", "[Parameters.Factor * Beta.Amount]");
            var p = preparer.Prepare(d); Equal("3", Named(p, "AlphaValue").Single().Text); Equal("2", Named(p, "BetaValue").Single().Text); Equal(2m, d.Parameters[0].DefaultValue.Number);
        });
        yield return Test("bindings evaluate against caller not preceding bindings", () =>
        {
            var d = Parallel(1, 0); d.Parameters.Add(new("Factor", 2));
            d.Pages[0].Bands[1].Items[1] = ((SubreportItem)d.Pages[0].Bands[1].Items[1]) with { ParameterBindings = [new("Factor", "3"), new("Copied", "Parameters.Factor")] };
            d.Pages[1].Bands[0].Items[0] = Text("AlphaValue", "[Parameters.Copied]"); Equal("2", Named(preparer.Prepare(d), "AlphaValue").Single().Text);
        });
        yield return Test("master-detail relation and Parent scope reach the target", () =>
        {
            var d = Parallel(0, 0); d.Data.Add(new() { Name = "Masters", Columns = ["Id", "Name"], Rows = [new() { ["Id"] = 1, ["Name"] = "One" }, new() { ["Id"] = 2, ["Name"] = "Two" }] });
            d.Data[0].Columns.Add("MasterId"); d.Data[0].Rows.Add(new() { ["MasterId"] = 1, ["Label"] = "A", ["Amount"] = 7 }); d.Data[0].Rows.Add(new() { ["MasterId"] = 2, ["Label"] = "B", ["Amount"] = 9 });
            d.Relations.Add(new("Masters", "Id", "Alpha", "MasterId")); d.Pages[0].Bands[1] = new() { Kind = BandKind.Data, DataSource = "Masters", Height = 24, Items = [new SubreportItem { PageName = "LeftPage", Bounds = new(0, 0, 210, 24) }] };
            d.Pages.RemoveAt(2); d.Pages[1].Bands[0].Items[0] = Text("AlphaValue", "[Parent.Name]:[Alpha.Label]");
            Equal("One:A,Two:B", string.Join(',', Named(preparer.Prepare(d), "AlphaValue").Select(i => i.Text)));
        });
        yield return Test("on-parent growth shifts following objects and expands band", () => { var p = preparer.Prepare(Inline()); Equal(3, Named(p, "AlphaValue").Count); Equal(149d, Named(p, "Following").Single().Bounds.Y); });
        yield return Test("on-parent empty content shrinks and shifts upward", () => { var p = preparer.Prepare(Inline(0)); Equal(71d, Named(p, "Following").Single().Bounds.Y); });
        yield return Test("on-parent ShiftFollowing false leaves following object anchored", () => { var d = Inline(); d.Pages[0].Bands[1].Items[1] = ((SubreportItem)d.Pages[0].Bands[1].Items[1]) with { ShiftFollowing = false }; Equal(95d, Named(preparer.Prepare(d), "Following").Single().Bounds.Y); });
        yield return Test("on-parent fixed height clips instead of mutating target layout", () => { var d = Inline(3); d.Pages[0].Bands[1].Items[1] = ((SubreportItem)d.Pages[0].Bands[1].Items[1]) with { CanGrow = false, CanShrink = false }; Equal(95d, Named(preparer.Prepare(d), "Following").Single().Bounds.Y); Equal(1, Named(preparer.Prepare(d), "AlphaValue").Count); });
        yield return Test("on-parent large report preserves all rows across parent slices", () => { var p = preparer.Prepare(Inline(15)); Equal(15, Named(p, "AlphaValue").Count); Equal(1, Named(p, "Following").Count); Equal(true, p.Pages.Count >= 3); });
        yield return Test("hidden subreport is not invoked and target is not appended", () => { var d = Parallel(1, 1); d.Pages[0].Bands[1].Items[1] = ((SubreportItem)d.Pages[0].Bands[1].Items[1]) with { VisibleExpression = "false" }; var p = preparer.Prepare(d); Equal(0, Named(p, "AlphaValue").Count); Equal(1, Named(p, "BetaValue").Count); });
        yield return Test("preparation does not mutate report definitions", () => { var d = Parallel(); string before = ReportJson.Serialize(d); preparer.Prepare(d); Equal(before, ReportJson.Serialize(d)); });
        yield return Test("native round trip preserves target and mode", () => { var d = ReportJson.Clone(Inline()); var item = (SubreportItem)d.Pages[0].Bands[1].Items[1]; Equal("LeftPage", item.PageName); Equal(true, item.PrintOnParent); Equal(3, Named(preparer.Prepare(d), "AlphaValue").Count); });
        yield return Test("clipboard defaults preserve grow shrink and bindings", () => { var item = (SubreportItem)ReportJson.DeserializeItems("[{\"$type\":\"subreport\",\"pageName\":\"Detail\"}]").Single(); Equal(true, item.CanGrow && item.CanShrink && item.ShiftFollowing); Equal(false, item.PrintOnParent); Equal(0, item.ParameterBindings.Count); });
        yield return Test("clipboard null bindings rejected", () => Conformance.Throws<FormatException>(() => ReportJson.DeserializeItems("[{\"$type\":\"subreport\",\"parameterBindings\":null}]")));
        yield return Test("clipboard incomplete binding rejected", () => Conformance.Throws<FormatException>(() => ReportJson.DeserializeItems("[{\"$type\":\"subreport\",\"parameterBindings\":[{\"name\":\"X\"}]}]")));
        yield return Test("unknown subreport property rejected", () => Conformance.Throws<FormatException>(() => ReportJson.DeserializeItems("[{\"$type\":\"subreport\",\"madeUp\":true}]")));
        yield return Test("missing target is diagnosed", () => { var d = Parallel(); d.Pages.RemoveAt(1); Conformance.Throws<ReportPreparationException>(() => preparer.Prepare(d)); });
        yield return Test("self reference rejected before pagination", () => { var d = Parallel(); d.Pages[1].Bands.Add(new() { Items = [new SubreportItem { PageName = "LeftPage" }] }); Conformance.Throws<ReportPreparationException>(() => preparer.Prepare(d)); });
        yield return Test("indirect reference cycle rejected", () => { var d = Parallel(); d.Pages[1].Bands.Add(new() { Items = [new SubreportItem { PageName = "Main" }] }); Conformance.Throws<ReportPreparationException>(() => preparer.Prepare(d)); });
        yield return Test("duplicate parameter names rejected", () => { var d = Parallel(); d.Pages[0].Bands[1].Items[1] = ((SubreportItem)d.Pages[0].Bands[1].Items[1]) with { ParameterBindings = [new("X", "1"), new("x", "2")] }; Conformance.Throws<ReportPreparationException>(() => preparer.Prepare(d)); });
        yield return Test("parallel overlapping lanes fail explicitly", () => { var d = Parallel(); d.Pages[0].Bands[1].Items[2] = ((SubreportItem)d.Pages[0].Bands[1].Items[2]) with { Bounds = new(100, 20, 210, 24) }; Conformance.Throws<ReportPreparationException>(() => preparer.Prepare(d)); });
        yield return Test("objects below band-flow anchor require child band", () => { var d = Parallel(); d.Pages[0].Bands[1].Items.Add(Text("Below", "Below") with { Bounds = new(0, 50, 100, 20) }); Conformance.Throws<ReportPreparationException>(() => preparer.Prepare(d)); });
        yield return Test("target columns are diagnosed rather than silently discarded", () => { var d = Parallel(); d.Pages[1] = d.Pages[1] with { Columns = 2 }; Conformance.Throws<ReportPreparationException>(() => preparer.Prepare(d)); });
        yield return Test("oversized unsplittable child band cannot loop", () => { var d = Parallel(1, 0); d.Pages[1].Bands[0] = d.Pages[1].Bands[0] with { Height = 1000 }; Conformance.Throws<ReportPreparationException>(() => preparer.Prepare(d)); });
        yield return Test("maximum pages applies to composed report", () => Conformance.Throws<ReportPreparationException>(() => preparer.Prepare(Parallel(), options: new(MaximumPages: 1))));
        yield return Test("maximum invocations shared by sibling reports", () => Conformance.Throws<ReportPreparationException>(() => preparer.Prepare(Parallel(), options: new(MaximumSubreportInvocations: 1))));
        yield return Test("maximum bands shared by all child runs", () => Conformance.Throws<ReportPreparationException>(() => preparer.Prepare(Parallel(), options: new(MaximumBandInstances: 5))));
        yield return Test("maximum items applies to combined lane output", () => Conformance.Throws<ReportPreparationException>(() => preparer.Prepare(Parallel(), options: new(MaximumItems: 10))));
        yield return Test("pre-cancellation", () => Conformance.Throws<OperationCanceledException>(() => preparer.Prepare(Parallel(), cancellationToken: new(true))));
        yield return Test("invalid depth budget rejected", () => Conformance.Throws<ArgumentOutOfRangeException>(() => preparer.Prepare(Parallel(), options: new(MaximumSubreportDepth: 0))));
        yield return Test("PrintOnParent page breaks fail explicitly", () => { var d = Inline(); d.Pages[1].Bands[0] = d.Pages[1].Bands[0] with { StartNewPage = true }; Conformance.Throws<ReportPreparationException>(() => preparer.Prepare(d)); });
    }
}
