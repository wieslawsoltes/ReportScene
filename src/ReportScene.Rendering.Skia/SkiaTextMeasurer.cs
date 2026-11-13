using ReportScene.Model;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace ReportScene.Rendering.Skia;

/// <summary>Own one instance per rendering/preparation thread; dispose it with the containing view or export operation.</summary>
public sealed partial class SkiaTextMeasurer : ITextMeasurer, IDisposable
{
    private sealed record Face(SKTypeface Typeface, SKShaper Shaper, bool Fallback, bool IntrinsicColor);
    private readonly record struct WidthKey(string Text, string Family, bool Bold, bool Italic, double Size);
    public const int MaximumCachedWidths = 4096, MaximumCachedCharacters = 262144, MaximumCachedTextLength = 1024;
    private readonly Dictionary<(string Family, bool Bold, bool Italic), Face> faces = new();
    private readonly Func<string, SKFontStyle, SKTypeface?>? typefaceResolver;
    private readonly Dictionary<WidthKey, double> widths = new();
    private readonly Queue<WidthKey> widthOrder = new();
    private int cachedCharacters;
    private static byte[]? fallbackFont;
    private bool disposed;
    public int CachedWidthCount => widths.Count;
    public int CachedCharacterCount => cachedCharacters;

    public SkiaTextMeasurer() { }

    /// <summary>
    /// Resolves application-owned fonts before the platform font manager. A
    /// non-null result transfers ownership to this metrics instance and must not
    /// be reused or disposed by the resolver. Return null for normal resolution.
    /// The supplied font style is borrowed and valid only during the callback.
    /// Resolution occurs on cache misses; the metrics owner disposes resolved
    /// faces when their cache is evicted or when the owner itself is disposed.
    /// </summary>
    public SkiaTextMeasurer(Func<string, SKFontStyle, SKTypeface?> typefaceResolver)
    {
        ArgumentNullException.ThrowIfNull(typefaceResolver);
        this.typefaceResolver = typefaceResolver;
    }
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
        if (faces.Count >= 128) { ClearTextCaches(); foreach (var face in faces.Values) { face.Shaper.Dispose(); face.Typeface.Dispose(); } faces.Clear(); ClearWidths(); }
        using var fontStyle = new SKFontStyle(style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        SKTypeface? typeface = typefaceResolver?.Invoke(style.FontFamily, fontStyle); bool fallback = false;
        if (typeface is not null && typeface.Handle == IntPtr.Zero)
            throw new ObjectDisposedException(nameof(typefaceResolver), "The font resolver returned a disposed typeface.");
        if (typeface is null && !OperatingSystem.IsBrowser()) typeface = SKTypeface.FromFamilyName(style.FontFamily, fontStyle);
        if (typeface is null && fallbackFont is { } bytes)
        { using var data = SKData.CreateCopy(bytes); typeface = SKTypeface.FromData(data); fallback = true; }
        typeface ??= SKTypeface.FromFamilyName(null, fontStyle);
        if (typeface is null) throw new InvalidOperationException("No font is available. Register a fallback font before preparing reports.");
        SKShaper? shaper = null;
        try
        {
            shaper = new SKShaper(typeface);
            var result = new Face(typeface, shaper, fallback, ReportGlyphRaster.HasIntrinsicColor(typeface));
            faces.Add(key, result); return result;
        }
        catch { shaper?.Dispose(); typeface.Dispose(); throw; }
    }
    public SKFont CreateFont(ItemStyle style)
    {
        var face = GetFace(style);
        var font = new SKFont(face.Typeface, (float)style.FontSize) { Edging = SKFontEdging.Antialias, Subpixel = true, Embolden = face.Fallback && style.Bold, SkewX = face.Fallback && style.Italic ? -.2f : 0 };
        NativeFontCreationCount++;
        return font;
    }
    /// <summary>
    /// Returns glyph identifiers and positioned advances from the same HarfBuzz
    /// shaper used for pagination and exports. Result arrays belong to the caller;
    /// the typeface remains owned by this metrics instance. Do not retain a font
    /// across disposal or eviction of its metrics owner.
    /// </summary>
    public SKShaper.Result ShapeText(string text, ItemStyle style)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(text); ArgumentNullException.ThrowIfNull(style);
        using var font = CreateFont(style);
        ShapeOperationCount++;
        return GetFace(style).Shaper.Shape(text, font);
    }

    public double MeasureWidth(string text, ItemStyle style)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ArgumentNullException.ThrowIfNull(text); ArgumentNullException.ThrowIfNull(style);
        if (text.Length == 0) return 0;
        var key = new WidthKey(text, style.FontFamily, style.Bold, style.Italic, style.FontSize);
        if (widths.TryGetValue(key, out var width)) return width;
        if (runs.TryGetValue(key, out var run)) width = run.Width;
        else
        {
            // Pagination probes need only widths. Do not eagerly create or
            // retain rendering arrays for prefixes that may never be drawn.
            var font = GetCachedFont(style).Font; ShapeOperationCount++;
            width = GetFace(style).Shaper.Shape(text, font).Width;
        }
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
    /// <summary>
    /// Draws positioned text. Solid SrcOver fills use per-glyph opacity groups
    /// for translucent intrinsic color fonts, matching the GPU atlas compositor.
    /// Ordinary fonts, opaque color fonts and arbitrary native paint effects keep
    /// their original vector drawing path. The supplied paint is never mutated.
    /// </summary>
    public void Draw(SKCanvas canvas, string text, float x, float baseline, ItemStyle style, SKPaint paint) =>
        Draw(canvas, text, x, baseline, style, paint, colorGlyphRasterOptions: null);

    /// <summary>
    /// Selects self-contained raster glyphs for solid intrinsic-color text when
    /// requested by a backend lacking native color glyph / opacity-layer support.
    /// Other native paint effects retain their original drawing behavior.
    /// </summary>
    public void Draw(SKCanvas canvas, string text, float x, float baseline, ItemStyle style, SKPaint paint,
        ColorGlyphRasterOptions? colorGlyphRasterOptions)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(canvas); ArgumentNullException.ThrowIfNull(style); ArgumentNullException.ThrowIfNull(paint);
        if (string.IsNullOrEmpty(text)) return;
        var face = GetFace(style);
        if (!face.IntrinsicColor || !ReportGlyphRaster.IsSolidSourceOver(paint)
            || (colorGlyphRasterOptions is null && paint.Color.Alpha == 255))
        {
            DrawNativeCached(canvas, text, x, baseline, style, paint);
            return;
        }
        if (canvas.IsClipEmpty || paint.Color.Alpha == 0) return;
        var cachedFont = GetCachedFont(style);
        var font = cachedFont.Font;
        var shaped = GetNativeShapedText(text, style, cachedFont);
        if (colorGlyphRasterOptions is null)
        {
            ReportGlyphRaster.DrawWithOpacity(canvas, font, shaped, x, baseline, paint);
            return;
        }
        float scale = ReportGlyphRaster.GetRasterScale(canvas, colorGlyphRasterOptions);
        if (scale <= 0) return;
        double rasterSize = style.FontSize * scale;
        if (!double.IsFinite(rasterSize) || rasterSize <= 0 || rasterSize > float.MaxValue)
            throw new InvalidOperationException("The color glyph raster font size is not representable.");
        using var rasterFont = CreateFont(style with { FontSize = rasterSize });
        ReportGlyphRaster.DrawRasterized(canvas, rasterFont, shaped, x, baseline, paint, scale, colorGlyphRasterOptions);
    }
    public void Dispose()
    {
        if (disposed) return;
        ClearTextCaches();
        foreach (var face in faces.Values) { face.Shaper.Dispose(); face.Typeface.Dispose(); }
        faces.Clear(); ClearWidths(); disposed = true;
    }
}
