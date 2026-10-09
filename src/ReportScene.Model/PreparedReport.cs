namespace ReportScene.Model;

public sealed record PreparedItem(ReportItem Source, RectD Bounds, string Text = "", IReadOnlyList<ChartPoint>? Points = null, RectD? Clip = null);
public sealed record ChartPoint(string Label, decimal Value);
public sealed record PreparedPage(int Number, double Width, double Height, IReadOnlyList<PreparedItem> Items);
public sealed record OutlineEntry(string Text, int Page, RectD Bounds);
public sealed record SearchHit(int Page, RectD Bounds, string Text, string ObjectId);
public sealed record PreparedReport(string Name, IReadOnlyList<PreparedPage> Pages, IReadOnlyList<Diagnostic> Diagnostics, TimeSpan Elapsed)
{
    public IEnumerable<SearchHit> Search(string text, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        foreach (var page in Pages)
            foreach (var item in page.Items)
                if (item.Text.Contains(text, comparison)) yield return new(page.Number, item.Bounds, item.Text, item.Source.Id);
    }
    public IEnumerable<OutlineEntry> Outline => Pages.SelectMany(p => p.Items
        .Where(i => !string.IsNullOrWhiteSpace(i.Source.Bookmark))
        .Select(i => new OutlineEntry(i.Source.Bookmark!, p.Number, i.Bounds)));
}

public interface ITextMeasurer
{
    double MeasureWidth(string text, ItemStyle style);
    double LineHeight(ItemStyle style);
}

/// <summary>Deterministic fallback for headless engine tests. UI/export should use a font-backed measurer.</summary>
public sealed class ApproximateTextMeasurer : ITextMeasurer
{
    public double MeasureWidth(string text, ItemStyle style) => text.EnumerateRunes().Count() * style.FontSize * .55;
    public double LineHeight(ItemStyle style) => style.FontSize * style.LineHeight;
}

public static class TextLayout
{
    public static IReadOnlyList<string> Wrap(string text, double width, ItemStyle style, ITextMeasurer measurer)
    {
        var result = new List<string>();
        width = Math.Max(1, width);
        foreach (var paragraph in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (paragraph.Length == 0) { result.Add(""); continue; }
            var remaining = paragraph;
            while (remaining.Length > 0)
            {
                if (measurer.MeasureWidth(remaining, style) <= width) { result.Add(remaining); break; }
                // Search UTF-16 boundaries without splitting surrogate pairs.
                var boundaries = System.Globalization.StringInfo.ParseCombiningCharacters(remaining);
                int lo = 1, hi = boundaries.Length, fit = 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    int end = mid == boundaries.Length ? remaining.Length : boundaries[mid];
                    if (measurer.MeasureWidth(remaining[..end], style) <= width) { fit = mid; lo = mid + 1; }
                    else hi = mid - 1;
                }
                int count = fit == boundaries.Length ? remaining.Length : boundaries[fit];
                int space = remaining.LastIndexOf(' ', Math.Max(0, count - 1), count);
                if (space > 0) count = space;
                result.Add(remaining[..count]);
                remaining = remaining[count..].TrimStart(' ');
            }
        }
        return result;
    }
}
