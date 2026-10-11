using ReportScene.Model;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace ReportScene.Rendering.Skia;

/// <summary>Own one instance per rendering/preparation thread; dispose it with the containing view or export operation.</summary>
public sealed class SkiaTextMeasurer : ITextMeasurer, IDisposable
{
    private sealed record Face(SKTypeface Typeface, SKShaper Shaper, bool Fallback);
    private readonly record struct WidthKey(string Text, string Family, bool Bold, bool Italic, double Size);
    public const int MaximumCachedWidths = 4096, MaximumCachedCharacters = 262144, MaximumCachedTextLength = 1024;
    private readonly Dictionary<(string Family, bool Bold, bool Italic), Face> faces = new();
    private readonly Dictionary<WidthKey, double> widths = new();
    private readonly Queue<WidthKey> widthOrder = new();
    private int cachedCharacters;
    private static byte[]? fallbackFont;
    private bool disposed;
    public int CachedWidthCount => widths.Count;
    public int CachedCharacterCount => cachedCharacters;
    /// <summary>Hosts without a system font manager can supply a licensed font from their own application assets before creating report views.</summary>
    public static void SetFallbackFont(byte[] fontData)
    {
        ArgumentNullException.ThrowIfNull(fontData);
        if (fontData.Length is 0 or > 16_000_000) throw new ArgumentException("The fallback font must be between 1 byte and 16 MB.");
        using var data = SKData.CreateCopy(fontData); using var face = SKTypeface.FromData(data) ?? throw new FormatException("The supplied fallback font is invalid.");
        fallbackFont = fontData.ToArray();
    }
    private void ClearWidths() { widths.Clear(); widthOrder.Clear(); cachedCharacters = 0; }
    private Face GetFace(ItemStyle style)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var key = (style.FontFamily, style.Bold, style.Italic);
        if (faces.TryGetValue(key, out var cached)) return cached;
        if (faces.Count >= 128) { foreach (var face in faces.Values) { face.Shaper.Dispose(); face.Typeface.Dispose(); } faces.Clear(); ClearWidths(); }
        var fontStyle = new SKFontStyle(style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        SKTypeface? typeface = null; bool fallback = false;
        if (!OperatingSystem.IsBrowser()) typeface = SKTypeface.FromFamilyName(style.FontFamily, fontStyle);
        if (typeface is null && fallbackFont is { } bytes)
        { using var data = SKData.CreateCopy(bytes); typeface = SKTypeface.FromData(data); fallback = true; }
        typeface ??= SKTypeface.FromFamilyName(null, fontStyle);
        if (typeface is null) throw new InvalidOperationException("No font is available. Register a fallback font before preparing reports.");
        var result = new Face(typeface, new SKShaper(typeface), fallback); faces.Add(key, result); return result;
    }
    public SKFont CreateFont(ItemStyle style)
    {
        var face = GetFace(style);
        return new(face.Typeface, (float)style.FontSize) { Edging = SKFontEdging.Antialias, Subpixel = true, Embolden = face.Fallback && style.Bold, SkewX = face.Fallback && style.Italic ? -.2f : 0 };
    }
    public double MeasureWidth(string text, ItemStyle style)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ArgumentNullException.ThrowIfNull(text); ArgumentNullException.ThrowIfNull(style);
        if (text.Length == 0) return 0;
        var key = new WidthKey(text, style.FontFamily, style.Bold, style.Italic, style.FontSize);
        if (widths.TryGetValue(key, out var width)) return width;
        using var font = CreateFont(style); width = GetFace(style).Shaper.Shape(text, font).Width;
        // Bound retained characters as well as entry count. Full paragraph probes must not
        // pin multi-megabyte strings in a long-lived designer. FIFO eviction avoids flush storms.
        int cost = text.Length + style.FontFamily.Length;
        if (text.Length <= MaximumCachedTextLength && cost <= MaximumCachedCharacters)
        {
            while (widths.Count >= MaximumCachedWidths || cachedCharacters + cost > MaximumCachedCharacters)
            { var oldest = widthOrder.Dequeue(); widths.Remove(oldest); cachedCharacters -= oldest.Text.Length + oldest.Family.Length; }
            widths.Add(key, width); widthOrder.Enqueue(key); cachedCharacters += cost;
        }
        return width;
    }
    public double LineHeight(ItemStyle style)
    { ObjectDisposedException.ThrowIf(disposed, this); return style.FontSize * style.LineHeight; }
    public void Draw(SKCanvas canvas, string text, float x, float baseline, ItemStyle style, SKPaint paint)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (string.IsNullOrEmpty(text)) return;
        using var font = CreateFont(style);
        canvas.DrawShapedText(GetFace(style).Shaper, text, x, baseline, SKTextAlign.Left, font, paint);
    }
    public void Dispose()
    {
        if (disposed) return;
        foreach (var face in faces.Values) { face.Shaper.Dispose(); face.Typeface.Dispose(); }
        faces.Clear(); ClearWidths(); disposed = true;
    }
}
