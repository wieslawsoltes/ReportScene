using ReportScene.Model;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace ReportScene.Rendering.Skia;

/// <summary>Own one instance per rendering/preparation thread; dispose it with the containing view or export operation.</summary>
public sealed class SkiaTextMeasurer : ITextMeasurer, IDisposable
{
    private sealed record Face(SKTypeface Typeface, SKShaper Shaper, bool Fallback);
    private readonly Dictionary<(string Family, bool Bold, bool Italic), Face> faces = new();
    private readonly Dictionary<(string Text, string Family, bool Bold, bool Italic, double Size), double> widths = new();
    private static byte[]? fallbackFont;
    private bool disposed;
    /// <summary>Hosts without a system font manager can supply a licensed font from their own application assets before creating report views.</summary>
    public static void SetFallbackFont(byte[] fontData)
    {
        if (fontData.Length is 0 or > 16_000_000) throw new ArgumentException("The fallback font must be between 1 byte and 16 MB.");
        using var data = SKData.CreateCopy(fontData); using var face = SKTypeface.FromData(data) ?? throw new FormatException("The supplied fallback font is invalid.");
        fallbackFont = fontData.ToArray();
    }
    private Face GetFace(ItemStyle style)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var key = (style.FontFamily, style.Bold, style.Italic);
        if (faces.TryGetValue(key, out var cached)) return cached;
        if (faces.Count >= 128) { foreach (var face in faces.Values) { face.Shaper.Dispose(); face.Typeface.Dispose(); } faces.Clear(); widths.Clear(); }
        var fontStyle = new SKFontStyle(style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        SKTypeface? typeface = null; bool fallback = false;
        if (!OperatingSystem.IsBrowser()) typeface = SKTypeface.FromFamilyName(style.FontFamily, fontStyle);
        if (typeface is null && fallbackFont is { } bytes)
        { using var data = SKData.CreateCopy(bytes); typeface = SKTypeface.FromData(data); fallback = true; }
        typeface ??= SKTypeface.FromFamilyName(null, fontStyle);
        if (typeface is null) throw new InvalidOperationException("No font is available. Register a fallback font before preparing reports.");
        var result = new Face(typeface, new SKShaper(typeface), fallback);
        faces.Add(key, result); return result;
    }
    public SKFont CreateFont(ItemStyle style)
    {
        var face = GetFace(style);
        return new(face.Typeface, (float)style.FontSize) { Edging = SKFontEdging.Antialias, Subpixel = true, Embolden = face.Fallback && style.Bold, SkewX = face.Fallback && style.Italic ? -.2f : 0 };
    }
    public double MeasureWidth(string text, ItemStyle style)
    {
        if (text.Length == 0) return 0;
        var key = (text, style.FontFamily, style.Bold, style.Italic, style.FontSize);
        if (widths.TryGetValue(key, out var width)) return width;
        using var font = CreateFont(style);
        width = GetFace(style).Shaper.Shape(text, font).Width;
        if (widths.Count >= 4096) widths.Clear();
        widths[key] = width; return width;
    }
    public double LineHeight(ItemStyle style) => style.FontSize * style.LineHeight;
    public void Draw(SKCanvas canvas, string text, float x, float baseline, ItemStyle style, SKPaint paint)
    {
        if (string.IsNullOrEmpty(text)) return;
        using var font = CreateFont(style);
        canvas.DrawShapedText(GetFace(style).Shaper, text, x, baseline, SKTextAlign.Left, font, paint);
    }
    public void Dispose()
    {
        if (disposed) return;
        foreach (var face in faces.Values) { face.Shaper.Dispose(); face.Typeface.Dispose(); }
        faces.Clear(); widths.Clear(); disposed = true;
    }
}
