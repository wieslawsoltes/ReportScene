using System.Globalization;
using ReportScene.Model;
using ReportScene.Rendering.Skia;

namespace ReportScene.Validation;

public static class TextLayoutPerformanceTests
{
    private sealed class CountingMeasurer : ITextMeasurer
    {
        public long Characters; public int Calls;
        public double MeasureWidth(string text, ItemStyle style) { Characters += text.Length; Calls++; return text.Length; }
        public double LineHeight(ItemStyle style) => 12;
    }
    // Frozen pre-optimization algorithm: differential correctness reference, not production code.
    public static IReadOnlyList<string> OriginalWrap(string text, double width, ItemStyle style, ITextMeasurer measurer)
    {
        var result = new List<string>(); width = Math.Max(1, width);
        foreach (var paragraph in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (paragraph.Length == 0) { result.Add(""); continue; }
            var remaining = paragraph;
            while (remaining.Length > 0)
            {
                if (measurer.MeasureWidth(remaining, style) <= width) { result.Add(remaining); break; }
                var boundaries = StringInfo.ParseCombiningCharacters(remaining); int lo = 1, hi = boundaries.Length, fit = 1;
                while (lo <= hi) { int mid = (lo + hi) / 2, end = mid == boundaries.Length ? remaining.Length : boundaries[mid]; if (measurer.MeasureWidth(remaining[..end], style) <= width) { fit = mid; lo = mid + 1; } else hi = mid - 1; }
                int count = fit == boundaries.Length ? remaining.Length : boundaries[fit]; int space = remaining.LastIndexOf(' ', Math.Max(0, count - 1), count);
                if (space > 0) count = space; result.Add(remaining[..count]); remaining = remaining[count..].TrimStart(' ');
            }
        }
        return result;
    }
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("text performance: " + name, true); } catch (Exception e) { return new("text performance: " + name, false, e.ToString()); } }
        yield return Test("differential wrapping with deterministic metrics", () =>
        {
            var random = new Random(177); var style = new ItemStyle(); var m = new ApproximateTextMeasurer();
            string[] tokens = ["alpha", "beta", " ", "  ", "\n", "\r\n", "a\u0301", "😀", "👩‍💻", "漢字", "\t"];
            for (int n = 0; n < 80; n++)
            {
                string text = string.Concat(Enumerable.Range(0, 30).Select(_ => tokens[random.Next(tokens.Length)])); double width = random.Next(1, 160);
                Conformance.Equal(string.Join('|', OriginalWrap(text, width, style, m)), string.Join('|', TextLayout.Wrap(text, width, style, m)));
            }
        });
        yield return Test("differential wrapping with actual Skia shaping", () =>
        {
            using var m = new SkiaTextMeasurer(); var style = new ItemStyle { FontSize = 11 };
            foreach (var text in new[] { "Invoice details: office services and AV typography.  Final line.", "a\u0301bc résumé naïve — café", "First\r\n\r\nSecond\rThird", new string('W', 250) })
                foreach (var width in new[] { 20d, 71d, 150d, 900d })
                    Conformance.Equal(string.Join('|', OriginalWrap(text, width, style, m)), string.Join('|', TextLayout.Wrap(text, width, style, m)));
        });
        yield return Test("long narrow paragraph avoids quadratic measured-character work", () =>
        {
            string text = new('x', 32768); var m = new CountingMeasurer();
            var lines = TextLayout.Wrap(text, 40, new(), m);
            Conformance.Equal(text, string.Concat(lines)); Conformance.Equal(true, lines.All(l => l.Length <= 40));
            Conformance.Equal(true, m.Characters < text.Length * 20L);
        });
        yield return Test("doubling paragraph length has near-linear measured work", () =>
        {
            var a = new CountingMeasurer(); var b = new CountingMeasurer();
            TextLayout.Wrap(new string('x', 8192), 40, new(), a); TextLayout.Wrap(new string('x', 16384), 40, new(), b);
            Conformance.Equal(true, b.Characters < a.Characters * 2.1);
        });
        yield return Test("oversized emoji and combining clusters remain whole", () =>
        {
            string text = "👩‍💻a\u0301🇵🇱"; var lines = TextLayout.Wrap(text, 1, new(), new CountingMeasurer());
            Conformance.Equal(text, string.Concat(lines)); Conformance.Equal(3, lines.Count);
        });
        yield return Test("space-plus-combining-mark is never split by a word break", () =>
        {
            var lines = TextLayout.Wrap("a \u0301bc", 3, new(), new CountingMeasurer());
            Conformance.Equal("a \u0301bc", string.Concat(lines)); Conformance.Equal(true, lines.All(l => !l.StartsWith('\u0301')));
        });
        yield return Test("pre-cancelled wrap stops before measurement", () =>
        { var m = new CountingMeasurer(); Conformance.Throws<OperationCanceledException>(() => TextLayout.Wrap("text", 10, new(), m, new(true))); Conformance.Equal(0, m.Calls); });
        yield return Test("invalid wrap width is rejected", () => Conformance.Throws<ArgumentOutOfRangeException>(() => TextLayout.Wrap("text", double.NaN, new(), new CountingMeasurer())));
        yield return Test("font cache does not retain long paragraphs", () =>
        { using var m = new SkiaTextMeasurer(); m.MeasureWidth(new string('x', 2048), new()); Conformance.Equal(0, m.CachedWidthCount); Conformance.Equal(0, m.CachedCharacterCount); });
        yield return Test("font cache is bounded by total retained characters", () =>
        {
            using var m = new SkiaTextMeasurer();
            for (int n = 0; n < 600; n++) m.MeasureWidth(n + new string('x', 500), new());
            Conformance.Equal(true, m.CachedCharacterCount <= SkiaTextMeasurer.MaximumCachedCharacters);
            Conformance.Equal(true, m.CachedWidthCount > 100 && m.CachedWidthCount < 600);
        });
        yield return Test("font cache is bounded by entry count without flushing everything", () =>
        {
            using var m = new SkiaTextMeasurer(); for (int n = 0; n < 4200; n++) m.MeasureWidth(n.ToString(CultureInfo.InvariantCulture), new());
            Conformance.Equal(SkiaTextMeasurer.MaximumCachedWidths, m.CachedWidthCount);
        });
        yield return Test("disposed metrics reject cached and empty measurement paths", () =>
        {
            var m = new SkiaTextMeasurer(); m.MeasureWidth("cached", new()); m.Dispose();
            Conformance.Throws<ObjectDisposedException>(() => m.MeasureWidth("cached", new())); Conformance.Throws<ObjectDisposedException>(() => m.MeasureWidth("", new()));
            Conformance.Equal(0, m.CachedWidthCount);
        });
        yield return Test("search excludes objects fully clipped from prepared output", () =>
        {
            var source = new TextItem { Bookmark = "Hidden" };
            var p = new PreparedPage(1, 100, 100, [new(source, new(0, 0, 30, 10), "Hidden", Clip: new(0, 20, 20, 10)), new(source, new(0, 120, 30, 10), "Outside")]);
            var report = new PreparedReport("test", [p], [], TimeSpan.Zero);
            Conformance.Equal(0, report.Search("Hidden").Count()); Conformance.Equal(0, report.Search("Outside").Count()); Conformance.Equal(0, report.Outline.Count());
        });
        yield return Test("search highlights use the visible intersection", () =>
        {
            var p = new PreparedPage(1, 100, 100, [new(new TextItem(), new(0, 0, 40, 20), "Visible", Clip: new(10, 10, 20, 20))]);
            var report = new PreparedReport("test", [p], [], TimeSpan.Zero); Conformance.Equal(new RectD(10, 10, 20, 10), report.Search("Visible").Single().Bounds);
        });
    }
}
