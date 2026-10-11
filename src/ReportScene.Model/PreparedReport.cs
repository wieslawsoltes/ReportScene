using System.Globalization;

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
                if (item.Text.Contains(text, comparison) && PreparedVisibility.VisibleBounds(page, item) is { } bounds)
                    yield return new(page.Number, bounds, item.Text, item.Source.Id);
    }
    public IEnumerable<OutlineEntry> Outline => Pages.SelectMany(p => p.Items
        .Where(i => !string.IsNullOrWhiteSpace(i.Source.Bookmark) && PreparedVisibility.VisibleBounds(p, i) is not null)
        .Select(i => new OutlineEntry(i.Source.Bookmark!, p.Number, PreparedVisibility.VisibleBounds(p, i)!.Value)));
}

/// <summary>Common geometric visibility for preview, search, outlines and hit-testing.</summary>
public static class PreparedVisibility
{
    public static RectD? VisibleBounds(PreparedPage page, PreparedItem item)
    {
        ArgumentNullException.ThrowIfNull(page); ArgumentNullException.ThrowIfNull(item);
        var bounds = Intersect(item.Bounds, new(0, 0, page.Width, page.Height));
        if (item.Clip is { } clip) bounds = Intersect(bounds, clip);
        return bounds.Width > 0 && bounds.Height > 0 ? bounds : null;
    }
    public static RectD Intersect(RectD a, RectD b)
    {
        double x = Math.Max(a.X, b.X), y = Math.Max(a.Y, b.Y);
        return new(x, y, Math.Max(0, Math.Min(a.Right, b.Right) - x), Math.Max(0, Math.Min(a.Bottom, b.Bottom) - y));
    }
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
        => Wrap(text, width, style, measurer, CancellationToken.None);

    /// <summary>
    /// Segment each paragraph once; exponentially probe a line-sized prefix before binary
    /// searching its end. Never shape or allocate the whole remaining suffix for every line.
    /// As with the original binary search, prefix advances are assumed to be monotonic.
    /// </summary>
    public static IReadOnlyList<string> Wrap(string text, double width, ItemStyle style, ITextMeasurer measurer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text); ArgumentNullException.ThrowIfNull(style); ArgumentNullException.ThrowIfNull(measurer);
        if (double.IsNaN(width)) throw new ArgumentOutOfRangeException(nameof(width));
        cancellationToken.ThrowIfCancellationRequested();
        var result = new List<string>(); width = Math.Max(1, width);
        foreach (var paragraph in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (paragraph.Length == 0) { result.Add(""); continue; }
            var boundaries = StringInfo.ParseCombiningCharacters(paragraph);
            int first = 0;
            int End(int index) => index == boundaries.Length ? paragraph.Length : boundaries[index];
            while (first < boundaries.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int start = boundaries[first], remaining = boundaries.Length - first;
                bool Fits(int count)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    double measured = measurer.MeasureWidth(paragraph.Substring(start, End(first + count) - start), style);
                    if (!double.IsFinite(measured) || measured < 0) throw new InvalidOperationException("Text measurement must return a finite nonnegative width.");
                    return measured <= width;
                }
                int fit = 0, probe = Math.Min(16, remaining);
                while (Fits(probe))
                {
                    fit = probe; if (probe == remaining) break;
                    probe = (int)Math.Min(remaining, (long)probe * 2);
                }
                if (fit == remaining) { result.Add(paragraph[start..]); break; }
                int lo = fit + 1, hi = probe - 1;
                while (lo <= hi)
                {
                    int mid = lo + (hi - lo) / 2;
                    if (Fits(mid)) { fit = mid; lo = mid + 1; } else hi = mid - 1;
                }
                // One oversized grapheme is clipped by its object, never split or retried forever.
                int next = first + Math.Max(1, fit), end = End(next);
                int space = paragraph.LastIndexOf(' ', end - 1, end - start);
                if (space > start)
                {
                    int boundary = Array.BinarySearch(boundaries, space);
                    // A space followed by combining marks is a single grapheme: don't tear it apart.
                    if (boundary >= 0 && End(boundary + 1) == space + 1) { next = boundary; end = space; }
                }
                result.Add(paragraph.Substring(start, end - start)); first = next;
                while (first < boundaries.Length && paragraph[boundaries[first]] == ' ' && End(first + 1) == boundaries[first] + 1) first++;
            }
        }
        return result;
    }
}
