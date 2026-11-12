using System.Runtime.CompilerServices;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace ReportScene.Rendering.Skia;

/// <summary>
/// Immutable positioned glyph data. Spans cannot mutate the metrics owner's
/// cache. A retained run remains readable after cache eviction or owner disposal;
/// it owns no native font, canvas, or GPU resource.
/// </summary>
public sealed class ReportTextRun
{
    private readonly ushort[] glyphs;
    internal SKShaper.Result Shaped { get; }
    public ReadOnlySpan<ushort> Glyphs => glyphs;
    public ReadOnlySpan<SKPoint> Positions => Shaped.Points;
    public ReadOnlySpan<uint> Clusters => Shaped.Clusters;
    public float Width => Shaped.Width;
    public SKFontMetrics FontMetrics { get; }

    internal ReportTextRun(SKShaper.Result shaped, SKFontMetrics metrics)
    {
        Shaped = shaped;
        glyphs = new ushort[shaped.Codepoints.Length];
        for (int i = 0; i < glyphs.Length; i++) glyphs[i] = checked((ushort)shaped.Codepoints[i]);
        FontMetrics = metrics;
    }
}

/// <summary>
/// Exact native glyph ink bounds at one raster scale, paired with the original
/// positioned run. This immutable snapshot contains no native resources.
/// </summary>
public sealed class ReportRasterTextRun
{
    private readonly SKRect[] inkBounds;
    public ReportTextRun TextRun { get; }
    public ReadOnlySpan<SKRect> InkBounds => inkBounds;
    public ReportRasterFont Font { get; }

    internal ReportRasterTextRun(ReportTextRun textRun, SKRect[] bounds, ReportRasterFont font)
    { TextRun = textRun; inkBounds = bounds; Font = font; }
}

/// <summary>
/// Immutable font parameters for managed glyph-atlas lookup. Identity follows
/// the managed typeface's lifetime, so recycled native handles cannot alias old
/// coverage. The descriptor never retains the native typeface.
/// </summary>
public readonly record struct ReportRasterFont(long TypefaceIdentity, float Size, float ScaleX,
    float SkewX, int Flags, bool IntrinsicColor)
{
    private sealed class Identity(SKTypeface face)
    {
        public long Value { get; } = Interlocked.Increment(ref nextIdentity);
        public bool IntrinsicColor { get; } = ReportGlyphRaster.HasIntrinsicColor(face);
    }
    private static readonly ConditionalWeakTable<SKTypeface, Identity> Identities = new();
    private static long nextIdentity;

    public static ReportRasterFont FromFont(SKFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (font.Handle == IntPtr.Zero) throw new ObjectDisposedException(nameof(font));
        var face = font.Typeface is { } typeface ? Identities.GetValue(typeface, static value => new(value)) : null;
        return new(face?.Value ?? 0, font.Size, font.ScaleX, font.SkewX,
            (font.Embolden ? 1 : 0) | (font.Subpixel ? 2 : 0) | (font.LinearMetrics ? 4 : 0)
            | (font.ForceAutoHinting ? 8 : 0) | (font.EmbeddedBitmaps ? 16 : 0) | (font.BaselineSnap ? 32 : 0)
            | ((int)font.Edging << 8) | ((int)font.Hinting << 12), face?.IntrinsicColor is true);
    }
}
