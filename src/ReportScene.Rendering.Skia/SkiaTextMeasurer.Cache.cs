using System.Diagnostics.CodeAnalysis;
using ReportScene.Model;
using SkiaSharp;

namespace ReportScene.Rendering.Skia;

public sealed partial class SkiaTextMeasurer
{
    public const int MaximumCachedFonts = 128;
    public const int MaximumCachedTextRuns = 2048, MaximumCachedRunCharacters = 131072,
        MaximumCachedRunGlyphs = 131072, MaximumCachedRunTextLength = 4096;
    private readonly record struct FontKey(string Family, bool Bold, bool Italic, double Size);
    private readonly record struct RasterKey(WidthKey Text, float Scale);
    private readonly record struct BlobKey(WidthKey Text, float X, float Baseline);
    private sealed record CachedFont(SKFont Font, SKFontMetrics Metrics);
    private sealed record CachedBlob(SKTextBlob Blob, int GlyphCount);
    private readonly Dictionary<FontKey, CachedFont> fonts = new();
    private readonly Queue<FontKey> fontOrder = new();
    private readonly Dictionary<WidthKey, ReportTextRun> runs = new();
    private readonly Queue<WidthKey> runOrder = new();
    private readonly Dictionary<RasterKey, ReportRasterTextRun> rasterRuns = new();
    private readonly Queue<RasterKey> rasterOrder = new();
    private readonly Dictionary<BlobKey, CachedBlob> blobs = new();
    private readonly Queue<BlobKey> blobOrder = new();
    private int runCharacters, runGlyphs, rasterCharacters, rasterGlyphs, blobCharacters, blobGlyphs;
    public int CachedFontCount => fonts.Count;
    public int CachedTextRunCount => runs.Count;
    public int CachedRunCharacterCount => runCharacters;
    public int CachedRunGlyphCount => runGlyphs;
    public int CachedRasterRunCount => rasterRuns.Count;
    public int CachedRasterCharacterCount => rasterCharacters;
    public int CachedRasterGlyphCount => rasterGlyphs;
    public int CachedNativeBlobCount => blobs.Count;
    public int CachedBlobCharacterCount => blobCharacters;
    public int CachedBlobGlyphCount => blobGlyphs;
    public long ShapeOperationCount { get; private set; }
    public long RasterBoundsQueryCount { get; private set; }
    public long NativeBlobBuildCount { get; private set; }
    public long NativeFontCreationCount { get; private set; }

    private static WidthKey TextKey(string text, ItemStyle style) => new(text, style.FontFamily, style.Bold, style.Italic, style.FontSize);
    private static int TextCost(WidthKey key) => checked(key.Text.Length + key.Family.Length);
    private static bool CacheableText(WidthKey key) => (long)key.Text.Length + key.Family.Length <= MaximumCachedRunTextLength;

    private CachedFont GetCachedFont(ItemStyle style)
    {
        var face = GetFace(style);
        var key = new FontKey(style.FontFamily, style.Bold, style.Italic, style.FontSize);
        if (fonts.TryGetValue(key, out var cached)) return cached;
        var font = new SKFont(face.Typeface, (float)style.FontSize)
        { Edging = SKFontEdging.Antialias, Subpixel = true, Embolden = face.Fallback && style.Bold, SkewX = face.Fallback && style.Italic ? -.2f : 0 };
        NativeFontCreationCount++;
        try
        {
            cached = new(font, font.Metrics);
            if (fonts.Count >= MaximumCachedFonts)
            { var oldest = fontOrder.Dequeue(); fonts.Remove(oldest, out var removed); removed!.Font.Dispose(); }
            fonts.Add(key, cached); fontOrder.Enqueue(key); return cached;
        }
        catch { font.Dispose(); throw; }
    }

    /// <summary>Returns native font metrics without allocating a caller-owned font on a warm lookup.</summary>
    public SKFontMetrics GetFontMetrics(ItemStyle style)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ArgumentNullException.ThrowIfNull(style);
        return GetCachedFont(style).Metrics;
    }

    /// <summary>
    /// Returns immutable HarfBuzz positions for a zero-origin run. Retention is
    /// bounded independently by run count, UTF-16 characters, and glyph count.
    /// The caller-owned array contract of ShapeText remains unchanged.
    /// </summary>
    public ReportTextRun GetTextRun(string text, ItemStyle style)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(text); ArgumentNullException.ThrowIfNull(style);
        return GetTextRunCore(TextKey(text, style), style, retain: true);
    }

    private ReportTextRun GetTextRunCore(WidthKey key, ItemStyle style, bool retain)
    {
        if (runs.TryGetValue(key, out var cached)) return cached;
        var font = GetCachedFont(style);
        ShapeOperationCount++;
        var result = new ReportTextRun(GetFace(style).Shaper.Shape(key.Text, font.Font), font.Metrics);
        if (retain && CacheableText(key) && result.Glyphs.Length <= MaximumCachedRunTextLength)
        {
            int cost = TextCost(key), count = result.Glyphs.Length;
            while (runs.Count >= MaximumCachedTextRuns || runCharacters + cost > MaximumCachedRunCharacters || runGlyphs + count > MaximumCachedRunGlyphs)
            {
                var oldest = runOrder.Dequeue(); runs.Remove(oldest, out var removed);
                runCharacters -= TextCost(oldest); runGlyphs -= removed!.Glyphs.Length;
            }
            runs.Add(key, result); runOrder.Enqueue(key); runCharacters += cost; runGlyphs += count;
        }
        return result;
    }

    /// <summary>
    /// Returns cached native ink bounds for a bounded run. Oversized text returns
    /// false before allocating ink arrays; callers must retain bounded-batch
    /// traversal and clip before allocating glyph textures. Raster size uses the
    /// same float multiplication as CreateFont(style).Size *= rasterScale.
    /// </summary>
    public bool TryGetRasterTextRun(string text, ItemStyle style, float rasterScale,
        [NotNullWhen(true)] out ReportRasterTextRun? run)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(text); ArgumentNullException.ThrowIfNull(style);
        if (!float.IsFinite(rasterScale) || rasterScale <= 0) throw new ArgumentOutOfRangeException(nameof(rasterScale));
        var textKey = TextKey(text, style); run = null;
        if (!CacheableText(textKey)) return false;
        var key = new RasterKey(textKey, rasterScale);
        if (rasterRuns.TryGetValue(key, out run)) return true;
        var shaped = GetTextRunCore(textKey, style, retain: true);
        if (shaped.Glyphs.Length > MaximumCachedRunTextLength) return false;
        using var font = CreateFont(style); font.Size *= rasterScale;
        if (!float.IsFinite(font.Size) || font.Size <= 0) throw new ArgumentException("Glyph raster size must be finite and positive.", nameof(style));
        var bounds = new SKRect[shaped.Glyphs.Length];
        font.GetGlyphWidths(shaped.Glyphs, Span<float>.Empty, bounds);
        RasterBoundsQueryCount++;
        run = new(shaped, bounds, ReportRasterFont.FromFont(font));
        int cost = TextCost(textKey), count = shaped.Glyphs.Length;
        while (rasterRuns.Count >= MaximumCachedTextRuns || rasterCharacters + cost > MaximumCachedRunCharacters || rasterGlyphs + count > MaximumCachedRunGlyphs)
        {
            var oldest = rasterOrder.Dequeue(); rasterRuns.Remove(oldest, out var removed);
            rasterCharacters -= TextCost(oldest.Text); rasterGlyphs -= removed!.TextRun.Glyphs.Length;
        }
        rasterRuns.Add(key, run); rasterOrder.Enqueue(key); rasterCharacters += cost; rasterGlyphs += count;
        return true;
    }

    private void DrawNativeCached(SKCanvas canvas, string text, float x, float baseline, ItemStyle style, SKPaint paint)
    {
        var textKey = TextKey(text, style); var key = new BlobKey(textKey, x, baseline);
        if (blobs.TryGetValue(key, out var cached)) { canvas.DrawText(cached.Blob, 0, 0, paint); return; }
        var font = GetCachedFont(style).Font;
        // Match DrawShapedText's offset accumulation exactly. Translating a
        // zero-origin run later can round float advances differently.
        ShapeOperationCount++;
        var shaped = GetFace(style).Shaper.Shape(text, x, baseline, font);
        using var builder = new SKTextBlobBuilder();
        var target = builder.AllocateRawPositionedRun(font, shaped.Codepoints.Length, null);
        var glyphs = target.Glyphs; var positions = target.Positions;
        for (int i = 0; i < shaped.Codepoints.Length; i++)
        { glyphs[i] = (ushort)shaped.Codepoints[i]; positions[i] = shaped.Points[i]; }
        var blob = builder.Build() ?? throw new InvalidOperationException("The shaped text blob could not be created.");
        NativeBlobBuildCount++;
        bool retained = false;
        try
        {
            if (CacheableText(textKey) && shaped.Codepoints.Length <= MaximumCachedRunTextLength && float.IsFinite(x) && float.IsFinite(baseline))
            {
                int cost = TextCost(textKey), count = shaped.Codepoints.Length;
                while (blobs.Count >= MaximumCachedTextRuns || blobCharacters + cost > MaximumCachedRunCharacters || blobGlyphs + count > MaximumCachedRunGlyphs)
                {
                    var oldest = blobOrder.Dequeue(); blobs.Remove(oldest, out var removed);
                    blobCharacters -= TextCost(oldest.Text); blobGlyphs -= removed!.GlyphCount; removed.Blob.Dispose();
                }
                blobs.Add(key, new(blob, count)); blobOrder.Enqueue(key); blobCharacters += cost; blobGlyphs += count; retained = true;
            }
            canvas.DrawText(blob, 0, 0, paint);
        }
        finally { if (!retained) blob.Dispose(); }
    }

    private void ClearTextCaches()
    {
        foreach (var blob in blobs.Values) blob.Blob.Dispose();
        foreach (var font in fonts.Values) font.Font.Dispose();
        blobs.Clear(); blobOrder.Clear(); fonts.Clear(); fontOrder.Clear();
        runs.Clear(); runOrder.Clear(); rasterRuns.Clear(); rasterOrder.Clear();
        runCharacters = runGlyphs = rasterCharacters = rasterGlyphs = blobCharacters = blobGlyphs = 0;
    }
}
