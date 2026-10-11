using ReportScene.Model;

namespace ReportScene.Designer;

/// <summary>
/// Bounded immutable wrapping results for an authoring surface. Owned by one UI/rendering
/// thread; it does not own the measurer. Clear after changing the measurer's font environment.
/// Position, selection and zoom do not affect point-based line breaks.
/// </summary>
public sealed class TextLayoutCache
{
    private readonly record struct Key(string Text, double Width, ItemStyle Style);
    private sealed record Entry(string Text, int Characters);
    private readonly ITextMeasurer measurer;
    private readonly Dictionary<Key, Entry> entries = new();
    private readonly Queue<Key> order = new();
    public const int MaximumEntries = 256, MaximumCharacters = 131072, MaximumInputLength = 4096;
    public int Count => entries.Count;
    public int RetainedCharacters { get; private set; }
    public long Hits { get; private set; }
    public long Misses { get; private set; }
    public TextLayoutCache(ITextMeasurer measurer) => this.measurer = measurer ?? throw new ArgumentNullException(nameof(measurer));
    public string GetWrappedText(string text, double width, ItemStyle style)
    {
        ArgumentNullException.ThrowIfNull(text); ArgumentNullException.ThrowIfNull(style);
        if (double.IsNaN(width)) throw new ArgumentOutOfRangeException(nameof(width));
        var key = new Key(text, Math.Max(1, width), style);
        if (entries.TryGetValue(key, out var cached)) { Hits++; return cached.Text; }
        string result = string.Join('\n', TextLayout.Wrap(text, key.Width, style, measurer)); Misses++;
        long characters = (long)text.Length + result.Length + (style.FontFamily?.Length ?? 0) + (style.Foreground?.Length ?? 0)
            + (style.Fill?.Length ?? 0) + (style.BorderColor?.Length ?? 0);
        if (text.Length > MaximumInputLength || characters > MaximumCharacters) return result;
        while (entries.Count >= MaximumEntries || RetainedCharacters + characters > MaximumCharacters)
        {
            var oldest = order.Dequeue(); var removed = entries[oldest]; entries.Remove(oldest); RetainedCharacters -= removed.Characters;
        }
        entries.Add(key, new(result, (int)characters)); order.Enqueue(key); RetainedCharacters += (int)characters; return result;
    }
    public void Clear() { entries.Clear(); order.Clear(); RetainedCharacters = 0; }
}
