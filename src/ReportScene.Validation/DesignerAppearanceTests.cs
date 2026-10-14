using ReportScene.Designer;
using ReportScene.Model;
using ReportScene.Serialization;

namespace ReportScene.Validation;

public static class DesignerAppearanceTests
{
    private static DesignerSession Create() => new(new() { Name = "Appearance commands", Pages = [new() { Bands = [new() { Id = "band", Items = [
        new TextItem { Id = "a", Name = "A", Text = "Keep [Value]", Bounds = new(12, 10, 80, 30), Bookmark = "Keep", Style = new() { BorderWidth = 2.375, BorderColor = "#108840", Bold = true } },
        new TextItem { Id = "b", Name = "B", Text = "Other", Bounds = new(110, 10, 80, 30), Style = new() { Italic = true } },
        new TextItem { Id = "locked", Name = "Locked", Locked = true, Style = new() { BorderWidth = 6, VerticalAlignment = VerticalAlign.Bottom } }
    ], Children = [new() { Id = "child-band", Kind = BandKind.Child, Items = [new TextItem { Id = "c", Name = "C", Text = "Nested" }] }] }] }] });
    private static ReportItem Item(DesignerSession session, string id) => DocumentTree.AllItems(session.Document).Single(item => item.Id == id);
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action run) { try { run(); return new("appearance: " + name, true); } catch (Exception error) { return new("appearance: " + name, false, error.ToString()); } }
        yield return Test("mixed border toggle adds missing widths and retains existing widths", () =>
        {
            var s = Create(); s.SelectMany(["a", "b", "locked", "c"]); var original = ReportJson.Serialize(s.Document); var locked = Item(s, "locked");
            var a = Item(s, "a"); var b = Item(s, "b"); s.ToggleSelectionBorder();
            Conformance.Equal(a, Item(s, "a")); Conformance.Equal(b with { Style = b.Style with { BorderWidth = 1 } }, Item(s, "b"));
            Conformance.Equal(1d, Item(s, "c").Style.BorderWidth); Conformance.Equal(locked, Item(s, "locked"));
            Conformance.Equal(1, s.HistoryCount); Conformance.Equal(4, s.LastReplacementScanCount);
            s.Undo(); Conformance.Equal(original, ReportJson.Serialize(s.Document)); s.Redo();
            Conformance.Equal(2.375, Item(s, "a").Style.BorderWidth); Conformance.Equal(1d, Item(s, "b").Style.BorderWidth);
        });
        yield return Test("all present borders toggle off together and undo retains individual widths", () =>
        {
            var s = Create(); s.SelectMany(["a", "locked"]); var original = ReportJson.Serialize(s.Document);
            s.ToggleSelectionBorder(); Conformance.Equal(0d, Item(s, "a").Style.BorderWidth); Conformance.Equal(6d, Item(s, "locked").Style.BorderWidth);
            s.Undo(); Conformance.Equal(original, ReportJson.Serialize(s.Document));
        });
        yield return Test("locked missing border cannot force an editable border to stay enabled", () =>
        {
            var s = Create(); s.Select("b"); s.SetSelectionLocked(true); s.SelectMany(["a", "b"]);
            s.ToggleSelectionBorder(); Conformance.Equal(0d, Item(s, "a").Style.BorderWidth); Conformance.Equal(0d, Item(s, "b").Style.BorderWidth);
        });
        yield return Test("precise width affects only appearance and uses one nested batch", () =>
        {
            var s = Create(); s.SelectMany(["a", "b", "c", "locked"]); var original = ReportJson.Serialize(s.Document);
            var before = DocumentTree.AllItems(s.Document).ToArray(); s.SetSelectionBorderWidth(1.125);
            foreach (var item in before) Conformance.Equal(item.Locked ? item : item with { Style = item.Style with { BorderWidth = 1.125 } }, Item(s, item.Id));
            Conformance.Equal(1, s.HistoryCount); Conformance.Equal(4, s.LastReplacementScanCount);
            s.Undo(); Conformance.Equal(original, ReportJson.Serialize(s.Document)); s.Redo(); Conformance.Equal(1.125, Item(s, "c").Style.BorderWidth);
        });
        yield return Test("equal width and alignment do not create history or dirtiness", () =>
        {
            var s = Create(); s.Select("a"); var original = ReportJson.Serialize(s.Document); var revision = s.CaptureRevision();
            s.SetSelectionBorderWidth(2.375); s.SetSelectionVerticalAlignment(VerticalAlign.Top);
            Conformance.Equal(true, s.IsCurrent(revision)); Conformance.Equal(0, s.HistoryCount); Conformance.Equal(false, s.IsDirty);
            Conformance.Equal(original, ReportJson.Serialize(s.Document));
        });
        foreach (double invalid in new[] { -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            yield return Test("invalid width is rejected before mutation " + invalid, () =>
            {
                var s = Create(); s.SelectAll(); var original = ReportJson.Serialize(s.Document);
                Conformance.Throws<ArgumentOutOfRangeException>(() => s.SetSelectionBorderWidth(invalid));
                Conformance.Equal(original, ReportJson.Serialize(s.Document)); Conformance.Equal(0, s.HistoryCount);
            });
        yield return Test("alignment validates before mutation and preserves all other fields", () =>
        {
            var s = Create(); s.SelectMany(["a", "b", "c", "locked"]); var before = DocumentTree.AllItems(s.Document).ToArray();
            Conformance.Throws<ArgumentOutOfRangeException>(() => s.SetSelectionVerticalAlignment((VerticalAlign)77));
            Conformance.Equal(0, s.HistoryCount); s.SetSelectionVerticalAlignment(VerticalAlign.Center);
            foreach (var item in before) Conformance.Equal(item.Locked ? item : item with { Style = item.Style with { VerticalAlignment = VerticalAlign.Center } }, Item(s, item.Id));
            Conformance.Equal(1, s.HistoryCount); Conformance.Equal(4, s.LastReplacementScanCount);
        });
        yield return Test("empty and locked selections are no-ops", () =>
        {
            var s = Create(); var original = ReportJson.Serialize(s.Document);
            foreach (string? target in new string?[] { null, "locked" })
            { s.Select(target); s.SetSelectionBorderWidth(3); s.ToggleSelectionBorder(); s.SetSelectionVerticalAlignment(VerticalAlign.Center); }
            Conformance.Equal(original, ReportJson.Serialize(s.Document)); Conformance.Equal(0, s.HistoryCount);
        });
        yield return Test("active drags reject all appearance operations without modifying geometry", () =>
        {
            var s = Create(); s.Select("a"); var original = ReportJson.Serialize(s.Document); s.BeginDrag();
            Conformance.Throws<InvalidOperationException>(() => s.SetSelectionBorderWidth(3));
            Conformance.Throws<InvalidOperationException>(s.ToggleSelectionBorder);
            Conformance.Throws<InvalidOperationException>(() => s.SetSelectionVerticalAlignment(VerticalAlign.Bottom));
            s.CancelDrag(); Conformance.Equal(original, ReportJson.Serialize(s.Document)); Conformance.Equal(0, s.HistoryCount);
        });
        yield return Test("large border selection is replaced in one document scan", () =>
        {
            var items = Enumerable.Range(0, 2000).Select(n => (ReportItem)new TextItem { Id = "i" + n, Name = "Item" + n }).ToList();
            var s = new DesignerSession(new() { Pages = [new() { Bands = [new() { Items = items }] }] });
            s.SelectMany(items.Where((_, n) => n % 2 == 0).Select(item => item.Id)); s.SetSelectionBorderWidth(.75);
            Conformance.Equal(2000, s.LastReplacementScanCount); Conformance.Equal(1, s.HistoryCount);
            Conformance.Equal(1000, DocumentTree.AllItems(s.Document).Count(item => item.Style.BorderWidth == .75));
        });
    }
}
