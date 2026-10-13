using ReportScene.Designer;
using ReportScene.Model;
using ReportScene.Serialization;

namespace ReportScene.Validation;

public static class DesignerFormattingReentrancyTests
{
    private static DesignerSession Create()
    {
        var session = new DesignerSession(new() { Name = "Formatting observers", Pages = [new() { Bands = [new() { Id = "band", Items = [
            new TextItem { Id = "source", Name = "Source", Text = "Source text", Style = new() { Bold = true, Fill = "#108840" } },
            new TextItem { Id = "target", Name = "Target", Text = "Target text", Bounds = new(12, 30, 90, 40) },
            new TextItem { Id = "third", Name = "Third", Text = "Third text", Style = new() { Italic = true } }
        ] }] }] });
        session.Select("source"); return session;
    }
    private static ReportItem Item(DesignerSession session, string id) => DocumentTree.AllItems(session.Document).Single(i => i.Id == id);
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("formatting observers: " + name, true); } catch (Exception e) { return new("formatting observers: " + name, false, e.ToString()); } }
        foreach (bool subscribedFirst in new[] { true, false })
            yield return Test("external edit cancels repeat regardless of subscription order " + subscribedFirst, () =>
            {
                var s = Create(); var original = ReportJson.Serialize(s.Document); var target = Item(s, "target"); bool edited = false;
                EventHandler observer = (_, _) => { if (edited) return; edited = true; s.Move(4, 0); };
                if (subscribedFirst) s.Changed += observer;
                using var painter = new DesignerFormatPainter(s);
                if (!subscribedFirst) s.Changed += observer;
                painter.Begin(repeat: true);
                Conformance.Equal(new DesignerFormattingResult(1, 1), painter.Apply(["target"]));
                Conformance.Equal(false, painter.IsActive); Conformance.Equal("", painter.SourceName);
                Conformance.Equal(2, s.HistoryCount); Conformance.Equal(target.Bounds.X + 4, Item(s, "target").Bounds.X);
                Conformance.Equal(Item(s, "source").Style, Item(s, "target").Style);
                Conformance.Equal(target is TextItem t ? t.Text : "", ((TextItem)Item(s, "target")).Text);
                s.Changed -= observer; s.Undo(); s.Undo();
                Conformance.Equal(original, ReportJson.Serialize(s.Document));
                s.Redo(); s.Redo(); Conformance.Equal(false, painter.IsActive);
            });
        yield return Test("same-object reload inside an observer invalidates the operation epoch", () =>
        {
            var s = Create(); using var painter = new DesignerFormatPainter(s); painter.Begin(repeat: true); bool loaded = false;
            s.Changed += (_, _) => { if (loaded) return; loaded = true; s.Load(s.Document); };
            painter.Apply(["target"]);
            Conformance.Equal(false, painter.IsActive); Conformance.Equal("", painter.SourceName);
            Conformance.Equal(0, s.HistoryCount); Conformance.Equal(false, s.IsDirty);
            Conformance.Equal(default(DesignerFormattingResult), painter.Apply(["third"]));
        });
        yield return Test("equal-format selection observers cannot be adopted as owned edits", () =>
        {
            var s = Create(); using var painter = new DesignerFormatPainter(s); painter.Begin(repeat: true); bool edited = false;
            s.SelectionChanged += (_, _) => { if (edited) return; edited = true; s.Move(4, 0); };
            Conformance.Equal(new DesignerFormattingResult(1, 0), painter.Apply(["source"]));
            Conformance.Equal(1, s.HistoryCount); Conformance.Equal(false, painter.IsActive);
            Conformance.Equal("", painter.SourceName);
        });
        yield return Test("a selection observer starting a drag cannot resurrect the capture on cancellation", () =>
        {
            var s = Create(); using var painter = new DesignerFormatPainter(s); painter.Begin(repeat: true); bool started = false;
            s.SelectionChanged += (_, _) => { if (started) return; started = true; s.BeginDrag(); };
            Conformance.Equal(new DesignerFormattingResult(1, 0), painter.Apply(["source"]));
            Conformance.Equal(true, s.HasActiveDrag); Conformance.Equal(false, painter.IsActive);
            s.CancelDrag(); Conformance.Equal(false, painter.IsActive); Conformance.Equal("", painter.SourceName);
            Conformance.Equal(0, s.HistoryCount);
        });
        yield return Test("save acknowledgment inside a change callback preserves repeat ownership", () =>
        {
            var s = Create(); using var painter = new DesignerFormatPainter(s); painter.Begin(repeat: true); bool saved = false;
            s.Changed += (_, _) => { if (saved) return; saved = true; s.MarkSaved(); };
            painter.Apply(["target"]); Conformance.Equal(true, painter.IsRepeating);
            Conformance.Equal(false, s.IsDirty); Conformance.Equal(1, s.HistoryCount);
            painter.Apply(["third"]); Conformance.Equal(true, painter.IsRepeating); Conformance.Equal(2, s.HistoryCount);
            s.Undo(); Conformance.Equal(false, painter.IsActive);
        });
        yield return Test("an observer's new one-shot capture is not consumed by the older operation", () =>
        {
            var s = Create(); using var painter = new DesignerFormatPainter(s); painter.Begin(); bool captured = false;
            EventHandler observer = (_, _) => { if (captured) return; captured = true; s.Select("third"); Conformance.Equal(true, painter.Begin()); };
            s.Changed += observer; painter.Apply(["target"]); s.Changed -= observer;
            Conformance.Equal(true, painter.IsActive); Conformance.Equal(false, painter.IsRepeating);
            Conformance.Equal("Third", painter.SourceName); Conformance.Equal(1, s.HistoryCount);
            painter.Apply(["source"]); Conformance.Equal(false, painter.IsActive);
            Conformance.Equal(Item(s, "third").Style, Item(s, "source").Style); Conformance.Equal(2, s.HistoryCount);
        });
        yield return Test("selection-only recapture survives an equal-style one-shot operation", () =>
        {
            var s = Create(); using var painter = new DesignerFormatPainter(s); painter.Begin(); bool captured = false;
            EventHandler observer = (_, _) => { if (captured) return; captured = true; s.Select("third"); painter.Begin(); };
            s.SelectionChanged += observer;
            Conformance.Equal(new DesignerFormattingResult(1, 0), painter.Apply(["source"]));
            s.SelectionChanged -= observer;
            Conformance.Equal(true, painter.IsActive); Conformance.Equal("Third", painter.SourceName);
            Conformance.Equal(0, s.HistoryCount); painter.Apply(["target"]);
            Conformance.Equal(false, painter.IsActive); Conformance.Equal(1, s.HistoryCount);
            Conformance.Equal(Item(s, "third").Style, Item(s, "target").Style);
        });
        yield return Test("explicit cancellation during notification is not rearmed", () =>
        {
            var s = Create(); using var painter = new DesignerFormatPainter(s); painter.Begin(repeat: true);
            s.Changed += (_, _) => painter.Cancel(); painter.Apply(["target"]);
            Conformance.Equal(false, painter.IsActive); Conformance.Equal("", painter.SourceName);
            Conformance.Equal(1, s.HistoryCount); Conformance.Equal(Item(s, "source").Style, Item(s, "target").Style);
        });
        yield return Test("nested Apply is rejected without an additional edit", () =>
        {
            var s = Create(); using var painter = new DesignerFormatPainter(s); painter.Begin(repeat: true);
            var third = Item(s, "third"); int rejected = 0;
            s.Changed += (_, _) => { Conformance.Throws<InvalidOperationException>(() => painter.Apply(["third"])); rejected++; };
            painter.Apply(["target"]);
            Conformance.Equal(1, rejected); Conformance.Equal(1, s.HistoryCount);
            Conformance.Equal(third, Item(s, "third")); Conformance.Equal(true, painter.IsRepeating);
        });
        yield return Test("a throwing observer after commit leaves undo available and cancels the tool", () =>
        {
            var s = Create(); using var painter = new DesignerFormatPainter(s); painter.Begin(repeat: true);
            var original = ReportJson.Serialize(s.Document);
            EventHandler observer = (_, _) => throw new InvalidOperationException("Host observer failure"); s.Changed += observer;
            Conformance.Throws<InvalidOperationException>(() => painter.Apply(["target"])); s.Changed -= observer;
            Conformance.Equal(1, s.HistoryCount); Conformance.Equal(false, painter.IsActive);
            Conformance.Equal(Item(s, "source").Style, Item(s, "target").Style);
            s.Undo(); Conformance.Equal(original, ReportJson.Serialize(s.Document));
        });
        yield return Test("disposal during notification cannot retain the completed operation", () =>
        {
            var s = Create(); using var painter = new DesignerFormatPainter(s); painter.Begin(repeat: true);
            s.Changed += (_, _) => painter.Dispose(); painter.Apply(["target"]);
            Conformance.Equal(false, painter.IsActive); Conformance.Equal("", painter.SourceName); Conformance.Equal(1, s.HistoryCount);
            Conformance.Throws<ObjectDisposedException>(() => painter.Apply(["third"]));
        });
    }
}
