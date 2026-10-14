using ReportScene.Designer;
using ReportScene.Model;
using ReportScene.Serialization;

namespace ReportScene.Validation;

public static class DesignerFormattingTests
{
    private static DesignerSession Create()
    {
        var style = new ItemStyle { FontFamily = "Courier New", FontSize = 17, Bold = true, Italic = true, Underline = true,
            Foreground = "#80112233", Fill = "#00abcdef", BorderColor = "#123456", BorderWidth = 2.5, Padding = 3,
            LineHeight = 1.4, Alignment = TextAlign.Right, VerticalAlignment = VerticalAlign.Bottom };
        return new(new() { Name = "Formatting contract", Pages = [new() { Bands = [new() { Id = "band", Height = 240, Items = [
            new TextItem { Id = "source", Name = "Source", Text = "Source [Value]", Style = style },
            new TextItem { Id = "target", Name = "Target", Text = "Keep [Parameters.Value]", Format = "N2", Bounds = new(12, 30, 90, 40),
                Bookmark = "Keep", Hyperlink = "https://example.invalid", VisibleExpression = "true", CanGrow = true },
            new TextItem { Id = "locked", Name = "Locked", Locked = true }
        ], Children = [new() { Id = "nested", Kind = BandKind.Child, Items = [new ShapeItem { Id = "child", Name = "Child", Shape = ShapeKind.Ellipse }] }] }] }] });
    }
    private static ReportItem Item(DesignerSession session, string id) => DocumentTree.AllItems(session.Document).Single(i => i.Id == id);
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("format painter: " + name, true); } catch (Exception e) { return new("format painter: " + name, false, e.ToString()); } }
        yield return Test("capture and cancel preserve document, dirty flag, history and selection", () =>
        {
            var s = Create(); s.Select("source"); var json = ReportJson.Serialize(s.Document); using var p = new DesignerFormatPainter(s);
            Conformance.Equal(true, p.Begin()); Conformance.Equal("Source", p.SourceName); Conformance.Equal(false, p.IsRepeating);
            p.Cancel(); Conformance.Equal(false, p.IsActive); Conformance.Equal("", p.SourceName);
            Conformance.Equal(json, ReportJson.Serialize(s.Document)); Conformance.Equal(0, s.HistoryCount); Conformance.Equal(false, s.IsDirty);
            Conformance.Equal("source", s.SelectedIds.Single());
        });
        yield return Test("one shot copies every style field and preserves all other target fields", () =>
        {
            var s = Create(); s.Select("source"); using var p = new DesignerFormatPainter(s); var before = Item(s, "target"); var source = Item(s, "source");
            var page = s.Page; var band = s.Page.Bands[0]; p.Begin(); var result = p.Apply(["target"]);
            Conformance.Equal(new DesignerFormattingResult(1, 1), result); Conformance.Equal(before with { Style = source.Style }, Item(s, "target"));
            Conformance.Equal(source, Item(s, "source")); Conformance.Equal(true, ReferenceEquals(page, s.Page) && ReferenceEquals(band, s.Page.Bands[0]));
            Conformance.Equal(false, p.IsActive); Conformance.Equal(1, s.HistoryCount); Conformance.Equal("target", s.SelectedIds.Single());
            s.Undo(); Conformance.Equal(before, Item(s, "target")); Conformance.Equal("source", s.SelectedIds.Single()); Conformance.Equal(false, s.IsDirty);
            s.Redo(); Conformance.Equal(before with { Style = source.Style }, Item(s, "target")); Conformance.Equal(false, p.IsActive);
        });
        yield return Test("batch formatting covers nested targets and skips locked objects in one undo", () =>
        {
            var s = Create(); var json = ReportJson.Serialize(s.Document); var locked = Item(s, "locked");
            var ids = new List<string> { "target", "child", "locked", "target" };
            var result = s.ApplyFormatting(Item(s, "source").Style, ids); ids.Clear();
            Conformance.Equal(new DesignerFormattingResult(2, 2), result); Conformance.Equal(1, s.HistoryCount); Conformance.Equal(4, s.LastReplacementScanCount);
            Conformance.Equal(3, s.SelectedIds.Count); Conformance.Equal(locked, Item(s, "locked"));
            Conformance.Equal(Item(s, "source").Style, Item(s, "child").Style);
            s.Undo(); Conformance.Equal(json, ReportJson.Serialize(s.Document)); s.Redo(); Conformance.Equal(3, s.SelectedIds.Count);
        });
        yield return Test("equal formatting consumes a one-shot tool without adding history", () =>
        {
            var s = Create(); s.Select("source"); using var p = new DesignerFormatPainter(s); p.Begin();
            Conformance.Equal(new DesignerFormattingResult(1, 0), p.Apply(["source"])); Conformance.Equal(false, p.IsActive);
            Conformance.Equal(0, s.HistoryCount); Conformance.Equal(false, s.IsDirty);
        });
        yield return Test("locked target retains the tool and cannot change selection or source", () =>
        {
            var s = Create(); s.Select("source"); using var p = new DesignerFormatPainter(s); p.Begin(); var before = ReportJson.Serialize(s.Document);
            Conformance.Equal(default(DesignerFormattingResult), p.Apply(["locked"])); Conformance.Equal(true, p.IsActive);
            Conformance.Equal("source", s.SelectedIds.Single()); Conformance.Equal(before, ReportJson.Serialize(s.Document));
            Conformance.Equal(1, p.Apply(["target"]).ChangedObjects); Conformance.Equal(false, p.IsActive);
        });
        yield return Test("copying from a locked source is permitted and never unlocks a target", () =>
        {
            var s = Create(); s.Select("source"); s.SetSelectionLocked(true); var before = s.HistoryCount;
            using var p = new DesignerFormatPainter(s); Conformance.Equal(true, p.Begin()); p.Apply(["target", "locked"]);
            Conformance.Equal(true, Item(s, "source").Locked); Conformance.Equal(true, Item(s, "locked").Locked);
            Conformance.Equal(before + 1, s.HistoryCount);
        });
        yield return Test("repeat mode refreshes its revision after only its own edits", () =>
        {
            var s = Create(); s.Select("source"); using var p = new DesignerFormatPainter(s); p.Begin(repeat: true);
            p.Apply(["target"]); Conformance.Equal(true, p.IsRepeating); p.Apply(["child"]); Conformance.Equal(true, p.IsRepeating);
            Conformance.Equal(2, s.HistoryCount); s.Move(4, 0); Conformance.Equal(false, p.IsActive);
            s.Undo(); Conformance.Equal(false, p.IsActive); Conformance.Equal(default(DesignerFormattingResult), p.Apply(["target"]));
        });
        yield return Test("undo and redo cannot resurrect a pending captured style", () =>
        {
            var s = Create(); s.Select("source"); s.Move(4, 0); using var p = new DesignerFormatPainter(s); p.Begin(repeat: true);
            s.Undo(); Conformance.Equal(false, p.IsActive); s.Redo(); Conformance.Equal(false, p.IsActive);
        });
        yield return Test("reloading the same document object invalidates a capture", () =>
        {
            var s = Create(); s.Select("source"); using var p = new DesignerFormatPainter(s); p.Begin();
            s.Load(s.Document); Conformance.Equal(false, p.IsActive); Conformance.Equal(default(DesignerFormattingResult), p.Apply(["target"]));
            Conformance.Equal(0, s.HistoryCount);
        });
        yield return Test("selection and save acknowledgments do not invalidate a capture", () =>
        {
            var s = Create(); s.Select("source"); using var p = new DesignerFormatPainter(s); p.Begin(); var token = s.CaptureRevision();
            s.SelectMany(["target", "child"]); s.MarkSaved(token); Conformance.Equal(true, p.IsActive);
            Conformance.Equal(new DesignerFormattingResult(2, 2), p.ApplyToSelection()); Conformance.Equal(1, s.HistoryCount);
        });
        yield return Test("missing targets abort the complete batch before mutation", () =>
        {
            var s = Create(); s.Select("source"); using var p = new DesignerFormatPainter(s); p.Begin(); var before = ReportJson.Serialize(s.Document);
            Conformance.Throws<InvalidOperationException>(() => p.Apply(["target", "absent"]));
            Conformance.Equal(before, ReportJson.Serialize(s.Document)); Conformance.Equal(0, s.HistoryCount); Conformance.Equal(true, p.IsActive);
        });
        yield return Test("duplicate document identifiers abort before replacement", () =>
        {
            var s = Create(); s.Page.Bands[0].Items.Add(new TextItem { Id = "target" }); var before = ReportJson.Serialize(s.Document);
            Conformance.Throws<InvalidOperationException>(() => s.ApplyFormatting(Item(s, "source").Style, ["target", "child"]));
            Conformance.Equal(before, ReportJson.Serialize(s.Document)); Conformance.Equal(0, s.HistoryCount);
        });
        yield return Test("empty and invalid requests cannot partially format", () =>
        {
            var s = Create(); var style = Item(s, "source").Style;
            Conformance.Equal(default(DesignerFormattingResult), s.ApplyFormatting(style, []));
            Conformance.Throws<ArgumentException>(() => s.ApplyFormatting(style, ["target", " "]));
            Conformance.Throws<ArgumentNullException>(() => s.ApplyFormatting(style, null!));
            Conformance.Throws<ArgumentNullException>(() => s.ApplyFormatting(null!, ["target"])); Conformance.Equal(0, s.HistoryCount);
        });
        yield return Test("invalid metrics alignment and colors are rejected before any edit", () =>
        {
            var s = Create(); var style = Item(s, "source").Style;
            foreach (var invalid in new[] { style with { FontSize = double.NaN }, style with { FontSize = 0 }, style with { LineHeight = double.PositiveInfinity },
                style with { Padding = -1 }, style with { BorderWidth = -1 }, style with { FontFamily = "" }, style with { Fill = "bad" },
                style with { Alignment = (TextAlign)999 }, style with { VerticalAlignment = (VerticalAlign)999 } })
                Conformance.Throws<ArgumentException>(() => s.ApplyFormatting(invalid, ["target"]));
            Conformance.Equal(0, s.HistoryCount); Conformance.Equal(false, s.IsDirty);
        });
        yield return Test("drag state cannot be captured or edited by formatting", () =>
        {
            var s = Create(); s.Select("source"); using var p = new DesignerFormatPainter(s); p.Begin(); s.BeginDrag();
            Conformance.Equal(false, p.Begin()); Conformance.Equal(false, p.IsActive);
            Conformance.Throws<InvalidOperationException>(() => s.ApplyFormatting(Item(s, "source").Style, ["target"]));
            Conformance.Equal(default(DesignerFormattingResult), p.Apply(["target"])); s.CancelDrag(); Conformance.Equal(false, p.IsActive);
            Conformance.Equal(0, s.HistoryCount);
        });
        yield return Test("capture requires one real object and disposal detaches lifecycle observation", () =>
        {
            var s = Create(); var p = new DesignerFormatPainter(s); Conformance.Equal(false, p.Begin());
            s.SelectAll(); Conformance.Equal(false, p.Begin()); s.Select("absent"); Conformance.Equal(false, p.Begin()); s.Select("source");
            Conformance.Equal(true, p.Begin()); p.Dispose(); p.Dispose(); Conformance.Equal(false, p.IsActive);
            Conformance.Throws<ObjectDisposedException>(() => p.Begin()); Conformance.Throws<ObjectDisposedException>(() => p.Apply(["target"]));
            s.Move(4, 0); Conformance.Equal(1, s.HistoryCount);
        });
        yield return Test("large batches perform one replacement scan and retain unrelated data", () =>
        {
            const int count = 2000; var s = Create(); var band = s.Page.Bands[0]; band.Items.Clear(); band.Children.Clear();
            for (int n = 0; n < count; n++) band.Items.Add(new TextItem { Id = "item" + n, Name = "Item" + n });
            var data = s.Document.Data; var ids = band.Items.Where((_, i) => i % 2 == 0).Select(i => i.Id).ToArray();
            var result = s.ApplyFormatting(new() { Bold = true }, ids);
            Conformance.Equal(new DesignerFormattingResult(count / 2, count / 2), result); Conformance.Equal(count, s.LastReplacementScanCount);
            Conformance.Equal(true, ReferenceEquals(data, s.Document.Data)); Conformance.Equal(1, s.HistoryCount);
            s.Undo(); Conformance.Equal(count, s.LastReplacementScanCount); Conformance.Equal(true, band.Items.All(i => !i.Style.Bold));
        });
    }
}
