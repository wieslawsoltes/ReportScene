using System.Numerics;
using System.Runtime.CompilerServices;
using ReportScene.Model;
using ReportScene.Rendering.Skia;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace ReportScene.Rendering.WebGpu;

/// <summary>
/// Encodes visible report commands into an ordered instanced stream. Geometry is
/// analytic in WGSL; shaped glyphs and complex-path coverage use bounded atlases.
/// </summary>
internal sealed class WebGpuReportCanvas : ReportCanvas
{
    public const int MaximumPrimitives = 262_144;
    private readonly GpuTextureCache textures;
    private float pixelsX, pixelsY;
    private readonly Stack<State> states = new();
    private State state;
    internal List<GpuPrimitive> Primitives { get; } = new(256);
    internal List<GpuBatch> Batches { get; } = new(32);
    private readonly record struct State(float ScaleX, float ScaleY, float X, float Y, SKRect Clip);
    private readonly record struct ShapeKey(string Text, string Family, bool Bold, bool Italic, double Size);
    private sealed class ShapeCache
    {
        public ShapeCache() { }
        internal readonly Dictionary<ShapeKey, SKShaper.Result> Runs = new();
        internal readonly Queue<ShapeKey> Order = new();
        internal int Characters;
    }
    private static readonly ConditionalWeakTable<SkiaTextMeasurer, ShapeCache> Shapes = new();

    public WebGpuReportCanvas(GpuTextureCache textures, RectD viewport, int pixelWidth, int pixelHeight)
    {
        this.textures = textures;
        Reset(viewport, pixelWidth, pixelHeight);
    }

    internal void Reset(RectD viewport, int pixelWidth, int pixelHeight)
    {
        states.Clear();
        Primitives.Clear();
        Batches.Clear();
        pixelsX = (float)(pixelWidth / viewport.Width);
        pixelsY = (float)(pixelHeight / viewport.Height);
        state = new(1, 1, (float)-viewport.X, (float)-viewport.Y, SKRect.Create((float)viewport.Width, (float)viewport.Height));
    }

    public override SKMatrix TotalMatrix => new()
    {
        ScaleX = state.ScaleX, ScaleY = state.ScaleY,
        TransX = state.X, TransY = state.Y, Persp2 = 1
    };
    public override SKRect LocalClipBounds => new(
        (state.Clip.Left - state.X) / state.ScaleX, (state.Clip.Top - state.Y) / state.ScaleY,
        (state.Clip.Right - state.X) / state.ScaleX, (state.Clip.Bottom - state.Y) / state.ScaleY);
    public override void Save() => states.Push(state);
    public override void Restore()
    {
        if (states.Count == 0) throw new InvalidOperationException("Unbalanced report canvas Restore.");
        state = states.Pop();
    }
    internal void ValidateBalancedState()
    {
        if (states.Count != 0) throw new InvalidOperationException("Report drawing left an unbalanced transform or clip scope.");
    }
    public override void Translate(float x, float y)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(x));
        state = state with { X = state.X + state.ScaleX * x, Y = state.Y + state.ScaleY * y };
    }
    public override void Scale(float x, float y)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || x <= 0 || y <= 0)
            throw new ArgumentOutOfRangeException(nameof(x), "Report canvas scales must be finite and positive.");
        state = state with { ScaleX = state.ScaleX * x, ScaleY = state.ScaleY * y };
    }
    private SKRect Map(SKRect r) => new(
        r.Left * state.ScaleX + state.X, r.Top * state.ScaleY + state.Y,
        r.Right * state.ScaleX + state.X, r.Bottom * state.ScaleY + state.Y);
    private Vector2 Map(float x, float y) => new(x * state.ScaleX + state.X, y * state.ScaleY + state.Y);
    private static SKRect Intersection(SKRect a, SKRect b)
    {
        float left = Math.Max(a.Left, b.Left), top = Math.Max(a.Top, b.Top);
        return new(left, top, Math.Max(left, Math.Min(a.Right, b.Right)), Math.Max(top, Math.Min(a.Bottom, b.Bottom)));
    }
    public override void ClipRect(SKRect rect, SKClipOperation operation = SKClipOperation.Intersect, bool antialias = false)
    {
        if (operation != SKClipOperation.Intersect) throw new NotSupportedException("Report drawing uses rectangular intersection clips.");
        state = state with { Clip = Intersection(state.Clip, Map(rect)) };
    }

    private float HalfStroke(SKPaint paint)
    {
        if (paint.Style == SKPaintStyle.Fill) return -1;
        if (paint.StrokeWidth == 0) return .5f / Math.Max(pixelsX, pixelsY);
        return paint.StrokeWidth * (state.ScaleX + state.ScaleY) * .25f;
    }
    private static Vector4 Premultiplied(SKColor color)
    {
        float alpha = color.Alpha / 255f;
        return new(color.Red / 255f * alpha, color.Green / 255f * alpha, color.Blue / 255f * alpha, alpha);
    }
    private GpuPrimitive Primitive(SKRect rect, SKPaint paint, int kind)
    {
        float stroke = HalfStroke(paint);
        float fringe = paint.IsAntialias ? 1.5f / Math.Min(pixelsX, pixelsY) : 0;
        float pad = Math.Max(0, stroke) + fringe;
        var quad = new SKRect(rect.Left - pad, rect.Top - pad, rect.Right + pad, rect.Bottom + pad);
        return new()
        {
            Rect = new(rect.Left, rect.Top, rect.Width, rect.Height),
            Quad = new(quad.Left, quad.Top, quad.Width, quad.Height),
            Clip = new(state.Clip.Left, state.Clip.Top, state.Clip.Right, state.Clip.Bottom),
            Color = Premultiplied(paint.Color),
            Shape = new(kind, stroke, paint.IsAntialias ? 1 : 0,
                paint.StrokeJoin switch { SKStrokeJoin.Round => 2, SKStrokeJoin.Bevel => 1, _ => paint.StrokeMiter < 1.414214f ? 1 : 0 })
        };
    }
    private void Add(GpuPrimitive primitive, GpuTexture? texture = null)
    {
        if (primitive.Color.W <= 0 || state.Clip.IsEmpty) return;
        var q = primitive.Quad;
        var clip = primitive.Clip;
        if (!float.IsFinite(q.X) || !float.IsFinite(q.Y) || !float.IsFinite(q.Z) || !float.IsFinite(q.W))
            throw new ArgumentException("Report drawing coordinates must be finite.");
        float left = Math.Max(q.X, clip.X), top = Math.Max(q.Y, clip.Y);
        float right = Math.Min(q.X + q.Z, clip.Z), bottom = Math.Min(q.Y + q.W, clip.W);
        if (right <= left || bottom <= top) return;
        primitive.Quad = new(left, top, right - left, bottom - top);
        if (Primitives.Count == MaximumPrimitives) throw new InvalidOperationException("Visible report geometry exceeds the primitive budget.");
        texture ??= textures.White;
        int index = Primitives.Count;
        Primitives.Add(primitive);
        if (Batches.Count > 0 && ReferenceEquals(Batches[^1].Texture, texture))
        {
            var last = Batches[^1];
            Batches[^1] = last with { Count = last.Count + 1 };
        }
        else Batches.Add(new(texture, index, 1));
    }

    public override void DrawRect(SKRect rect, SKPaint paint)
    {
        if (paint.Style == SKPaintStyle.StrokeAndFill)
        {
            using var builder = new SKPathBuilder(); builder.AddRect(rect); using var path = builder.Detach(); DrawPath(path, paint); return;
        }
        var primitive = Primitive(Map(rect), paint, 0);
        if (primitive.Shape.Y < 0) Add(primitive);
        else AddRectangleStroke(primitive);
    }

    private void AddRectangleStroke(GpuPrimitive primitive)
    {
        // Keep the exact signed-distance geometry while avoiding shading the
        // transparent center of large page, table and selection outlines.
        float pad = primitive.Shape.Y + (primitive.Shape.Z > .5f ? 1.5f / Math.Min(pixelsX, pixelsY) : 0);
        var rect = primitive.Rect;
        float left = rect.X + pad, top = rect.Y + pad;
        float right = rect.X + rect.Z - pad, bottom = rect.Y + rect.W - pad;
        if (right <= left || bottom <= top) { Add(primitive); return; }
        var outer = primitive.Quad;
        void Strip(float x, float y, float width, float height)
        {
            var part = primitive;
            part.Quad = new(x, y, width, height);
            Add(part);
        }
        // Disjoint strips prevent double blending even for translucent strokes.
        Strip(outer.X, outer.Y, outer.Z, top - outer.Y);
        Strip(outer.X, bottom, outer.Z, outer.Y + outer.W - bottom);
        Strip(outer.X, top, left - outer.X, bottom - top);
        Strip(right, top, outer.X + outer.Z - right, bottom - top);
    }
    public override void DrawRoundRect(SKRect rect, float radiusX, float radiusY, SKPaint paint)
    {
        if (Math.Abs(radiusX * state.ScaleX - radiusY * state.ScaleY) > .001f || paint.Style == SKPaintStyle.StrokeAndFill)
        {
            using var builder = new SKPathBuilder(); builder.AddRoundRect(rect, radiusX, radiusY); using var path = builder.Detach(); DrawPath(path, paint); return;
        }
        var p = Primitive(Map(rect), paint, 1);
        p.Data = new(radiusX * state.ScaleX, radiusY * state.ScaleY, 0, 0);
        Add(p);
    }
    public override void DrawOval(SKRect rect, SKPaint paint)
    {
        if (paint.Style == SKPaintStyle.StrokeAndFill)
        {
            using var builder = new SKPathBuilder(); builder.AddOval(rect); using var path = builder.Detach(); DrawPath(path, paint); return;
        }
        Add(Primitive(Map(rect), paint, 2));
    }
    public override void DrawLine(float x1, float y1, float x2, float y2, SKPaint paint)
    {
        var a = Map(x1, y1); var b = Map(x2, y2);
        float half = paint.StrokeWidth > 0 ? paint.StrokeWidth * (state.ScaleX + state.ScaleY) * .25f : .5f / Math.Max(pixelsX, pixelsY);
        float fringe = paint.IsAntialias ? 1.5f / Math.Min(pixelsX, pixelsY) : 0;
        float pad = half + fringe;
        var rect = new SKRect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        var p = Primitive(rect, paint, 3);
        p.Quad = new(rect.Left - pad, rect.Top - pad, rect.Width + 2 * pad, rect.Height + 2 * pad);
        p.Shape.Y = half;
        p.Shape.W = paint.StrokeCap switch { SKStrokeCap.Square => 1, SKStrokeCap.Round => 2, _ => 0 };
        p.Data = new(a.X, a.Y, b.X, b.Y);
        Add(p);
    }

    public override void DrawPath(SKPath path, SKPaint paint)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.IsEmpty) return;
        // Opaque round-joined polylines (icons/check marks) are direct analytic
        // GPU segments. Compound contours, joins and curves retain exact Skia
        // coverage through a bounded mask; they are never flattened imprecisely.
        if (path.SegmentMasks == SKPathSegmentMask.Line && paint.Style == SKPaintStyle.Stroke
            && paint.StrokeJoin == SKStrokeJoin.Round && paint.StrokeCap == SKStrokeCap.Round && paint.Color.Alpha == 255
            && paint.PathEffect is null)
        {
            using var iterator = path.CreateIterator(false);
            Span<SKPoint> points = stackalloc SKPoint[4];
            SKPathVerb verb;
            while ((verb = iterator.Next(points)) != SKPathVerb.Done)
                if (verb == SKPathVerb.Line) DrawLine(points[0].X, points[0].Y, points[1].X, points[1].Y, paint);
            return;
        }
        float scale = RasterScale();
        var coverage = textures.GetPathMask(path, paint, scale, LocalClipBounds);
        DrawCoverage(coverage, 0, 0, scale, paint.Color);
    }
    public override void DrawArc(SKRect oval, float startAngle, float sweepAngle, bool useCenter, SKPaint paint)
    {
        if (Math.Abs(sweepAngle) >= 360) { DrawOval(oval, paint); return; }
        // Analytic pie interiors; curved strokes and chord fills preserve exact
        // contour/cap rules via the common path coverage route.
        if (useCenter && paint.Style == SKPaintStyle.Fill && sweepAngle > 0)
        {
            var p = Primitive(Map(oval), paint, 5);
            p.Data = new(startAngle * MathF.PI / 180, sweepAngle * MathF.PI / 180, 0, 0);
            Add(p);
        }
        else
        {
            using var builder = new SKPathBuilder();
            if (useCenter) { builder.MoveTo(oval.MidX, oval.MidY); builder.ArcTo(oval, startAngle, sweepAngle, false); builder.Close(); }
            else { builder.ArcTo(oval, startAngle, sweepAngle, true); if (paint.Style != SKPaintStyle.Stroke) builder.Close(); }
            using var path = builder.Detach();
            DrawPath(path, paint);
        }
    }
    public override void DrawImage(SKImage image, SKRect destination, SKSamplingOptions sampling)
    {
        ArgumentNullException.ThrowIfNull(image);
        var rect = Map(destination);
        if (rect.IsEmpty) return;
        var coverage = rect;
        coverage.Inflate(1.5f / Math.Min(pixelsX, pixelsY), 1.5f / Math.Min(pixelsX, pixelsY));
        var visible = Intersection(coverage, state.Clip);
        if (visible.IsEmpty) return;
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        if (image.Width > textures.ImageTileSize || image.Height > textures.ImageTileSize)
        {
            DrawTiledImage(image, rect, visible, paint);
            return;
        }
        var texture = textures.GetImage(image);
        var p = Primitive(rect, paint, 4);
        p.Uv = new(0, 0, 1, 1);
        Add(p, texture);
    }

    private void DrawTiledImage(SKImage image, SKRect rect, SKRect visible, SKPaint paint)
    {
        if (image.Width <= 0 || image.Height <= 0 || (long)image.Width * image.Height > ReportImageRaster.MaximumDecodedPixels)
            throw new InvalidOperationException("Decoded image exceeds the 32-megapixel report image budget.");
        // Select the same isotropic footprint used by the texture sampler. The
        // global chain uses aligned 2:1 reductions and edge padding, so the next
        // selected level is identical to the previous tile's filtered second mip.
        double footprint = Math.Max(image.Width / ((double)rect.Width * pixelsX),
            image.Height / ((double)rect.Height * pixelsY));
        int maximumDimension = Math.Max(image.Width, image.Height);
        int maximumLevel = maximumDimension <= 1 ? 0 : 1 + BitOperations.Log2((uint)(maximumDimension - 1));
        int level = (int)Math.Clamp(Math.Floor(Math.Log2(Math.Max(1, footprint))), 0, maximumLevel);
        int levelWidth = ReportImageRaster.GetAlignedLevelDimension(image.Width, level);
        int levelHeight = ReportImageRaster.GetAlignedLevelDimension(image.Height, level);
        int tileSize = textures.ImageTileSize;
        int columns = (levelWidth + tileSize - 1) / tileSize, rows = (levelHeight + tileSize - 1) / tileSize;
        double sourcePixelsPerTile = tileSize * (double)(1L << level);
        int firstX = Math.Clamp((int)Math.Floor((visible.Left - rect.Left) / (double)rect.Width * image.Width / sourcePixelsPerTile), 0, columns - 1);
        int firstY = Math.Clamp((int)Math.Floor((visible.Top - rect.Top) / (double)rect.Height * image.Height / sourcePixelsPerTile), 0, rows - 1);
        int lastX = Math.Clamp((int)Math.Ceiling((visible.Right - rect.Left) / (double)rect.Width * image.Width / sourcePixelsPerTile) - 1, firstX, columns - 1);
        int lastY = Math.Clamp((int)Math.Ceiling((visible.Bottom - rect.Top) / (double)rect.Height * image.Height / sourcePixelsPerTile) - 1, firstY, rows - 1);
        var whole = Primitive(rect, paint, 4);
        for (int y = firstY; y <= lastY; y++)
            for (int x = firstX; x <= lastX; x++)
            {
                var tile = textures.GetImageTile(image, level, x, y);
                var source = tile.SourceBounds;
                var target = new SKRect(
                    rect.Left + source.Left / image.Width * rect.Width,
                    rect.Top + source.Top / image.Height * rect.Height,
                    rect.Left + source.Right / image.Width * rect.Width,
                    rect.Top + source.Bottom / image.Height * rect.Height);
                var p = Primitive(target, paint, 4);
                p.Uv = new(tile.Uv.Left, tile.Uv.Top, tile.Uv.Right, tile.Uv.Bottom);
                p.Data = whole.Rect;
                p.Extra.Z = 1; // Sample this tile; antialias only the whole image boundary.
                float left = x == 0 ? whole.Quad.X : target.Left;
                float top = y == 0 ? whole.Quad.Y : target.Top;
                float right = x == columns - 1 ? whole.Quad.X + whole.Quad.Z : target.Right;
                float bottom = y == rows - 1 ? whole.Quad.Y + whole.Quad.W : target.Bottom;
                // Internal quads are disjoint. Their four-pixel texture gutters
                // provide filtering continuity without translucent seam overdraw.
                p.Quad = new(left, top, right - left, bottom - top);
                Add(p, tile.Texture);
            }
    }

    private float RasterScale()
    {
        float scale = Math.Max(pixelsX * state.ScaleX, pixelsY * state.ScaleY);
        // Quantize upward to 1/8 pixel per report unit: stable caches during
        // fractional zoom without undersampling the screen's physical density.
        return Math.Max(.125f, MathF.Ceiling(scale * 8) / 8);
    }
    public override void DrawText(SkiaTextMeasurer metrics, string text, float x, float baseline, ItemStyle style, SKPaint paint)
    {
        if (string.IsNullOrEmpty(text) || state.Clip.IsEmpty || paint.Color.Alpha == 0) return;
        var shaped = Shape(metrics, text, style);
        float scale = RasterScale();
        using var font = metrics.CreateFont(style);
        font.Size *= scale;
        for (int i = 0; i < shaped.Codepoints.Length; i++)
        {
            var glyph = checked((ushort)shaped.Codepoints[i]);
            var point = shaped.Points[i];
            var coverage = textures.GetGlyph(font, glyph);
            DrawCoverage(coverage, x + point.X, baseline + point.Y, scale, paint.Color);
        }
    }
    private static SKShaper.Result Shape(SkiaTextMeasurer metrics, string text, ItemStyle style)
    {
        var cache = Shapes.GetOrCreateValue(metrics);
        var key = new ShapeKey(text, style.FontFamily, style.Bold, style.Italic, style.FontSize);
        if (cache.Runs.TryGetValue(key, out var found)) return found;
        var shaped = metrics.ShapeText(text, style);
        int cost = text.Length + style.FontFamily.Length;
        if (cost > 4096) return shaped;
        while (cache.Runs.Count >= 2048 || cache.Characters + cost > 131_072)
        {
            var oldest = cache.Order.Dequeue(); cache.Runs.Remove(oldest);
            cache.Characters -= oldest.Text.Length + oldest.Family.Length;
        }
        cache.Runs.Add(key, shaped); cache.Order.Enqueue(key); cache.Characters += cost;
        return shaped;
    }
    private void DrawCoverage(GpuCoverage coverage, float x, float baseline, float scale, SKColor color)
    {
        if (coverage.Bounds.IsEmpty) return;
        var b = coverage.Bounds;
        var rect = Map(new SKRect(x + b.Left / scale, baseline + b.Top / scale, x + b.Right / scale, baseline + b.Bottom / scale));
        using var paint = new SKPaint { Color = color, IsAntialias = false };
        var p = Primitive(rect, paint, 4);
        var uv = coverage.Uv;
        p.Uv = new(uv.Left, uv.Top, uv.Right, uv.Bottom);
        p.Extra.W = 1;
        Add(p, coverage.Texture);
    }

    public override GridPaintStatistics DrawGrid(RectD bounds, RectD viewport, double spacing, SKPaint paint,
        int maximumPoints = 1_048_576, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateGrid(bounds); ValidateGrid(viewport);
        if (!double.IsFinite(spacing) || spacing < 4) throw new ArgumentOutOfRangeException(nameof(spacing));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPoints);
        if (bounds.Width == 0 || bounds.Height == 0 || viewport.Width == 0 || viewport.Height == 0) return default;
        double firstX = bounds.X + Math.Max(0, Math.Ceiling((viewport.X - bounds.X) / spacing)) * spacing;
        double firstY = bounds.Y + Math.Max(0, Math.Ceiling((viewport.Y - bounds.Y) / spacing)) * spacing;
        double endX = Math.Min(bounds.Right, viewport.Right), endY = Math.Min(bounds.Bottom, viewport.Bottom);
        if (firstX >= endX || firstY >= endY) return default;
        double magnitude = Math.Max(Math.Max(Math.Abs(firstX), Math.Abs(firstY)), Math.Max(Math.Abs(endX), Math.Abs(endY)));
        if (!double.IsFinite(firstX) || !double.IsFinite(firstY) || spacing < Math.BitIncrement(magnitude) - magnitude)
            throw new ArgumentException("Grid spacing cannot advance at this coordinate precision.");
        double columns = Math.Ceiling((endX - firstX) / spacing), rows = Math.Ceiling((endY - firstY) / spacing);
        if (columns * rows > maximumPoints) throw new ArgumentException("Visible grid exceeds the configured point budget.");
        float sizeX = paint.StrokeWidth > 0 ? paint.StrokeWidth * state.ScaleX : 1 / pixelsX;
        float sizeY = paint.StrokeWidth > 0 ? paint.StrokeWidth * state.ScaleY : 1 / pixelsY;
        var first = Map((float)firstX, (float)firstY);
        var last = Map((float)(firstX + (columns - 1) * spacing), (float)(firstY + (rows - 1) * spacing));
        var rect = new SKRect(first.X - sizeX * .5f, first.Y - sizeY * .5f, last.X + sizeX * .5f, last.Y + sizeY * .5f);
        var p = Primitive(rect, paint, 6);
        p.Shape.Y = -1;
        p.Data = new(first.X, first.Y, (float)spacing * state.ScaleX, (float)spacing * state.ScaleY);
        p.Extra = new(sizeX, sizeY, (float)columns, (float)rows);
        Add(p);
        return new(checked((int)(columns * rows)), 1);
    }
    private static void ValidateGrid(RectD rect)
    {
        if (!double.IsFinite(rect.X) || !double.IsFinite(rect.Y) || !double.IsFinite(rect.Right) || !double.IsFinite(rect.Bottom)
            || !double.IsFinite(rect.Width) || !double.IsFinite(rect.Height) || rect.Width < 0 || rect.Height < 0
            || !float.IsFinite((float)rect.X) || !float.IsFinite((float)rect.Y) || !float.IsFinite((float)rect.Right) || !float.IsFinite((float)rect.Bottom))
            throw new ArgumentException("Grid coordinates must be finite and representable in the GPU command stream.");
    }
}
