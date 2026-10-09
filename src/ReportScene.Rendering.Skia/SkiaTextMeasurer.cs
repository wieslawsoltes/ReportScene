using ReportScene.Model;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace ReportScene.Rendering.Skia;

/// <summary>Own one instance per rendering/preparation thread; dispose it with the containing view or export operation.</summary>
public sealed class SkiaTextMeasurer : ITextMeasurer, IDisposable
{
    private sealed record Face(SKTypeface Typeface, SKShaper Shaper);
    private readonly Dictionary<(string Family, bool Bold, bool Italic), Face> faces = new();
    private readonly Dictionary<(string Text, string Family, bool Bold, bool Italic, double Size), double> widths = new();
    private bool disposed;
    private Face GetFace(ItemStyle style)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var key = (style.FontFamily, style.Bold, style.Italic);
        if (faces.TryGetValue(key, out var cached)) return cached;
        if (faces.Count >= 128) { foreach (var face in faces.Values) { face.Shaper.Dispose(); face.Typeface.Dispose(); } faces.Clear(); widths.Clear(); }
        var fontStyle = new SKFontStyle(style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        var typeface = SKTypeface.FromFamilyName(style.FontFamily, fontStyle) ?? SKTypeface.FromFamilyName(null, fontStyle) ?? throw new InvalidOperationException("No system font is available. Register a font before preparing reports.");
        var result = new Face(typeface, new SKShaper(typeface));
        faces.Add(key, result); return result;
    }
    public SKFont CreateFont(ItemStyle style) => new(GetFace(style).Typeface, (float)style.FontSize) { Edging = SKFontEdging.Antialias, Subpixel = true };
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
