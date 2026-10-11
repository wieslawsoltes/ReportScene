using ReportScene.Designer;
using ReportScene.Model;

namespace ReportScene.Validation;

public static class AuthoringTextCacheTests
{
    private sealed class Metrics : ITextMeasurer
    {
        public int Calls; public double Advance = 1;
        public double MeasureWidth(string text, ItemStyle style) { Calls++; return text.Length * Advance * style.FontSize / 10; }
        public double LineHeight(ItemStyle style) => 12;
    }
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("authoring text cache: " + name, true); } catch (Exception e) { return new("authoring text cache: " + name, false, e.ToString()); } }
        yield return Test("repeated redraw text returns identical output without shaping again", () =>
        {
            var metrics = new Metrics(); var cache = new TextLayoutCache(metrics); var style = new ItemStyle();
            string first = cache.GetWrappedText("line one and line two", 8, style); int calls = metrics.Calls;
            for (int n = 0; n < 1000; n++) Conformance.Equal(first, cache.GetWrappedText("line one and line two", 8, style with { }));
            Conformance.Equal(calls, metrics.Calls); Conformance.Equal(1000L, cache.Hits); Conformance.Equal(1L, cache.Misses);
        });
        yield return Test("width text and style changes cannot reuse stale line breaks", () =>
        {
            var cache = new TextLayoutCache(new Metrics()); var style = new ItemStyle();
            string first = cache.GetWrappedText("abcdefghijklmnop", 8, style);
            Conformance.Equal(false, first == cache.GetWrappedText("abcdefghijklmnop", 4, style));
            Conformance.Equal(false, first == cache.GetWrappedText("abcdefghijklmnop", 8, style with { FontSize = 20 }));
            Conformance.Equal("different", cache.GetWrappedText("different", 100, style)); Conformance.Equal(4L, cache.Misses);
        });
        yield return Test("entry and character budgets remain bounded under unique text", () =>
        {
            var cache = new TextLayoutCache(new Metrics()); var style = new ItemStyle();
            for (int n = 0; n < 1000; n++) cache.GetWrappedText(new string('x', 1000) + n, 40, style);
            Conformance.Equal(true, cache.Count <= TextLayoutCache.MaximumEntries); Conformance.Equal(true, cache.RetainedCharacters <= TextLayoutCache.MaximumCharacters);
        });
        yield return Test("oversized input is computed but never retained", () =>
        {
            var cache = new TextLayoutCache(new Metrics()); var style = new ItemStyle(); string text = new('x', TextLayoutCache.MaximumInputLength + 1);
            Conformance.Equal(text, cache.GetWrappedText(text, text.Length, style)); Conformance.Equal(0, cache.Count); Conformance.Equal(0, cache.RetainedCharacters);
        });
        yield return Test("clearing releases keys and honors a changed measurement environment", () =>
        {
            var metrics = new Metrics(); var cache = new TextLayoutCache(metrics); var style = new ItemStyle();
            string first = cache.GetWrappedText("abcdefghijklmnop", 8, style); metrics.Advance = 2; cache.Clear();
            Conformance.Equal(0, cache.Count); Conformance.Equal(0, cache.RetainedCharacters); Conformance.Equal(false, first == cache.GetWrappedText("abcdefghijklmnop", 8, style));
        });
        yield return Test("exceptions and invalid widths never replace a valid entry", () =>
        {
            var cache = new TextLayoutCache(new Metrics()); var style = new ItemStyle(); string first = cache.GetWrappedText("text", 8, style);
            Conformance.Throws<ArgumentOutOfRangeException>(() => cache.GetWrappedText("text", double.NaN, style));
            Conformance.Equal(first, cache.GetWrappedText("text", 8, style)); Conformance.Equal(1, cache.Count);
        });
    }
}
