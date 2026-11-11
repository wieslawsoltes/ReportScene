using ReportScene.Rendering.Skia;
using System.Runtime.CompilerServices;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Silk.NET.WebGPU;
using SkiaSharp;
using Uno.WinUI.Graphics3DWebGpu;

namespace ReportScene.Rendering.WebGpu;

/// <summary>Owns texture, view and binding references, but never the device or queue.</summary>
internal unsafe sealed class GpuTexture : IDisposable
{
    private readonly WebGPU api;
    public Texture* Handle;
    public TextureView* View;
    public BindGroup* BindGroup;
    public int Width, Height;
    public GpuTexture(WebGPU api) => this.api = api;
    public void Dispose()
    {
        // Release preserves resources referenced by already-submitted GPU work.
        // Destroy would invalidate those resources before the queue has finished.
        if (BindGroup != null) { api.BindGroupRelease(BindGroup); BindGroup = null; }
        if (View != null) { api.TextureViewRelease(View); View = null; }
        if (Handle != null) { api.TextureRelease(Handle); Handle = null; }
    }
}

/// <param name="Uv">Normalized texture coordinates, including the transparent guard pixels.</param>
/// <param name="Bounds">Coverage rectangle in raster pixels, relative to the glyph baseline or path origin.</param>
internal readonly record struct GpuCoverage(GpuTexture Texture, SKRect Uv, SKRect Bounds, bool IsColor = false);

/// <param name="SourceBounds">The tile's interior bounds in original image pixels.</param>
internal readonly record struct GpuImageTile(GpuTexture Texture, SKRect Uv, SKRect SourceBounds);

/// <summary>
/// Bounded CPU-coverage/GPU-texture cache shared by a renderer on one device.
/// Glyphs use their shaped glyph identifiers; text is never reshaped one character
/// at a time. The caller starts a frame before recording and submits it before
/// starting another frame. Entries referenced by the current frame are pinned.
/// </summary>
internal unsafe sealed class GpuTextureCache : IDisposable
{
    private const int ImageGutter = 4;
    internal const int CoverageGuard = ReportGlyphRaster.CoverageGuard;
    private const int AtlasSize = 2048, MaximumAtlasPages = 4, Guard = CoverageGuard, MaximumGlyphs = 65536, MaximumImages = 1024, MaximumMasks = 2048;
    private readonly WebGpuCanvasContext context;
    private readonly BindGroupLayout* textureLayout;
    private readonly Sampler* sampler;
    private readonly int maximumDimension;
    private readonly long imageBudget;
    private static readonly ConditionalWeakTable<SKTypeface, TypefaceIdentity> TypefaceIds = new();
    private static long nextTypefaceId;
    private readonly List<AtlasPage> pages = [];
    private readonly Dictionary<GlyphKey, GlyphEntry> glyphs = [];
    private readonly Dictionary<GlyphKey, GpuCoverage> largeGlyphs = [];
    private readonly Dictionary<ImageKey, ImageEntry> images = [];
    private readonly Dictionary<PathKey, ImageEntry> masks = [];
    private readonly List<GpuTexture> temporary = [];
    private long imageBytes, maskBytes, temporaryBytes;
    private SKBitmap? stagingMip;
    private uint stagedImage;
    private int stagedLevel;
    private ulong frame;
    private bool disposed;

    public GpuTexture White { get; }
    public int TextureUploads { get; private set; }
    public long UploadedBytes { get; private set; }
    public int AtlasPages => pages.Count;
    public long RetainedImageBytes => imageBytes;
    public long RetainedMaskBytes => maskBytes;
    public long TemporaryBytes => temporaryBytes;
    public int MaximumTextureDimension => maximumDimension;
    public int ImageTileSize => Math.Min(2048, maximumDimension - ImageGutter * 2) & ~1;
    public long RetainedStagingBytes => stagingMip is { } bitmap ? (long)bitmap.RowBytes * bitmap.Height : 0;

    public GpuTextureCache(WebGpuCanvasContext context, BindGroupLayout* textureLayout, Sampler* sampler,
        long maximumImageBytes = 256L * 1024 * 1024)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (textureLayout == null || sampler == null) throw new ArgumentException("A valid texture layout and sampler are required.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumImageBytes);
        this.context = context;
        this.textureLayout = textureLayout;
        this.sampler = sampler;
        imageBudget = maximumImageBytes;
        var limits = new SupportedLimits();
        maximumDimension = context.Api.DeviceGetLimits(context.Device, &limits)
            ? (int)Math.Min((uint)int.MaxValue, limits.Limits.MaxTextureDimension2D) : 2048;
        if (maximumDimension < AtlasSize) throw new InvalidOperationException("The WebGPU device cannot allocate a 2048-pixel glyph atlas.");
        White = Create(1, 1, 1);
        try
        {
            uint pixel = 0xffffffff;
            Upload(White, 0, 0, 0, 1, 1, &pixel, 4, 4);
        }
        catch { White.Dispose(); throw; }
    }

    public void BeginFrame()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        // Previous command buffers hold their own references after submission.
        foreach (var texture in temporary) texture.Dispose();
        temporary.Clear(); largeGlyphs.Clear(); temporaryBytes = 0;
        frame = frame == ulong.MaxValue ? 1 : frame + 1;
        if (frame == 1)
        {
            foreach (var page in pages) page.LastFrame = 0;
            foreach (var image in images.Values) image.LastFrame = 0;
            foreach (var mask in masks.Values) mask.LastFrame = 0;
        }
        TextureUploads = 0; UploadedBytes = 0;
    }

    public GpuCoverage GetGlyph(SKFont rasterFont, ushort glyph, SKColor foreground)
    {
        CheckFrame();
        ArgumentNullException.ThrowIfNull(rasterFont);
        if (!float.IsFinite(rasterFont.Size) || rasterFont.Size <= 0
            || !float.IsFinite(rasterFont.ScaleX) || !float.IsFinite(rasterFont.SkewX))
            throw new ArgumentException("Glyph raster parameters must be finite and the font size must be positive.", nameof(rasterFont));
        var key = GlyphKey.Create(rasterFont, glyph, foreground, out bool intrinsicColor);
        if (glyphs.TryGetValue(key, out var cached))
        {
            cached.Page.LastFrame = frame;
            return cached.Coverage;
        }
        if (largeGlyphs.TryGetValue(key, out var large)) return large;

        Span<ushort> glyphIds = stackalloc ushort[1] { glyph };
        Span<SKRect> measured = stackalloc SKRect[1];
        rasterFont.GetGlyphWidths(glyphIds, Span<float>.Empty, measured);
        if (measured[0].IsEmpty) return Empty;
        var bounds = RasterBounds(measured[0], 1);
        int width = checked((int)bounds.Width), height = checked((int)bounds.Height);
        if (width > AtlasSize || height > AtlasSize) EnsureBudget(checked((long)width * height * 4));
        using var bitmap = ReportGlyphRaster.Rasterize(rasterFont, glyph, bounds, foreground, intrinsicColor);

        if (width > AtlasSize || height > AtlasSize)
        {
            // Very large report headings retain their actual raster resolution.
            // These exceptional textures are pinned until the next frame.
            var texture = CreateTemporary(bitmap);
            var coverage = new GpuCoverage(texture, new(0, 0, 1, 1), bounds, intrinsicColor);
            largeGlyphs.Add(key, coverage);
            return coverage;
        }

        if (glyphs.Count >= MaximumGlyphs) EvictColdAtlas();
        AtlasPage? selected = null; int x = 0, y = 0;
        foreach (var page in pages)
            if (page.TryAllocate(width, height, out x, out y)) { selected = page; break; }
        if (selected is null)
        {
            if (pages.Count == MaximumAtlasPages) EvictColdAtlas();
            selected = new(Create(AtlasSize, AtlasSize, 1));
            pages.Add(selected);
            if (!selected.TryAllocate(width, height, out x, out y)) throw new InvalidOperationException("The glyph does not fit an empty atlas.");
        }
        selected.LastFrame = frame;
        Upload(selected.Texture, 0, x, y, width, height, bitmap.GetPixels().ToPointer(), bitmap.RowBytes, checked(bitmap.RowBytes * height));
        var result = new GpuCoverage(selected.Texture,
            new((float)x / AtlasSize, (float)y / AtlasSize, (float)(x + width) / AtlasSize, (float)(y + height) / AtlasSize), bounds, intrinsicColor);
        selected.Keys.Add(key);
        glyphs.Add(key, new(selected, result));
        return result;
    }

    public GpuTexture GetImage(SKImage image)
    {
        CheckFrame();
        ArgumentNullException.ThrowIfNull(image);
        var key = new ImageKey(image.UniqueId, -1, 0, 0);
        if (images.TryGetValue(key, out var cached))
        {
            cached.LastFrame = frame;
            return cached.Texture;
        }
        ValidateDimensions(image.Width, image.Height);
        int levels = 1 + (int)Math.Floor(Math.Log2(Math.Max(image.Width, image.Height)));
        long bytes = TextureBytes(image.Width, image.Height, levels);
        EnsureBudget(bytes, addImage: true);
        using var bitmap = ReportImageRaster.Decode(image);
        var texture = Create(image.Width, image.Height, levels);
        try
        {
            SKBitmap current = bitmap;
            try
            {
                for (int level = 0; level < levels; level++)
                {
                    Upload(texture, level, 0, 0, current.Width, current.Height, current.GetPixels().ToPointer(),
                        current.RowBytes, checked(current.RowBytes * current.Height));
                    if (level + 1 == levels) break;
                    var next = Downsample(current);
                    if (!ReferenceEquals(current, bitmap)) current.Dispose();
                    current = next;
                }
            }
            finally { if (!ReferenceEquals(current, bitmap)) current.Dispose(); }
            images.Add(key, new(texture, bytes, frame));
            imageBytes += bytes;
            return texture;
        }
        catch { texture.Dispose(); throw; }
    }

    /// <summary>
    /// Returns a viewport-selected tile from a globally filtered image level.
    /// Four source-level pixels surround its interior on every side; the even
    /// texture dimensions permit a second mip with an aligned two-pixel gutter.
    /// Tile coordinates address a ceil-sized level with exact power-of-two source
    /// strides. Fractional UVs preserve the original odd-sized image boundary.
    /// </summary>
    public GpuImageTile GetImageTile(SKImage image, int mipLevel, int tileX, int tileY)
    {
        CheckFrame();
        ArgumentNullException.ThrowIfNull(image);
        ValidateSourceImage(image);
        int maximumLevel = (int)Math.Ceiling(Math.Log2(Math.Max(image.Width, image.Height)));
        if (mipLevel < 0 || mipLevel > maximumLevel) throw new ArgumentOutOfRangeException(nameof(mipLevel));
        ArgumentOutOfRangeException.ThrowIfNegative(tileX); ArgumentOutOfRangeException.ThrowIfNegative(tileY);
        int levelWidth = ReportImageRaster.GetAlignedLevelDimension(image.Width, mipLevel);
        int levelHeight = ReportImageRaster.GetAlignedLevelDimension(image.Height, mipLevel);
        long ox = (long)tileX * ImageTileSize, oy = (long)tileY * ImageTileSize;
        if (ox >= levelWidth || oy >= levelHeight) throw new ArgumentOutOfRangeException(nameof(tileX), "The tile lies outside the selected image level.");
        int x = (int)ox, y = (int)oy;
        int coreWidth = Math.Min(ImageTileSize, levelWidth - x), coreHeight = Math.Min(ImageTileSize, levelHeight - y);
        int width = (coreWidth + ImageGutter * 2 + 1) & ~1, height = (coreHeight + ImageGutter * 2 + 1) & ~1;
        var key = new ImageKey(image.UniqueId, mipLevel, tileX, tileY);
        GpuTexture texture;
        if (images.TryGetValue(key, out var found))
        {
            found.LastFrame = frame;
            texture = found.Texture;
        }
        else
        {
            long bytes = TextureBytes(width, height, 2);
            EnsureBudget(bytes, addImage: true);
            var globalLevel = GetStagingMip(image, mipLevel);
            using var pixels = ExtractTile(globalLevel, x, y, width, height);
            using var nextLevel = ReportImageRaster.ExtractTileNextMip(globalLevel, x, y, width, height, ImageGutter);
            texture = Create(width, height, 2);
            try
            {
                Upload(texture, 0, 0, 0, width, height, pixels.GetPixels().ToPointer(), pixels.RowBytes, checked(pixels.RowBytes * height));
                Upload(texture, 1, 0, 0, nextLevel.Width, nextLevel.Height, nextLevel.GetPixels().ToPointer(), nextLevel.RowBytes, checked(nextLevel.RowBytes * nextLevel.Height));
                images.Add(key, new(texture, bytes, frame));
                imageBytes += bytes;
            }
            catch { texture.Dispose(); throw; }
        }
        long scale = 1L << mipLevel;
        long left = (long)x * scale, top = (long)y * scale;
        long right = Math.Min(image.Width, ((long)x + coreWidth) * scale);
        long bottom = Math.Min(image.Height, ((long)y + coreHeight) * scale);
        return new(texture,
            new((float)ImageGutter / width, (float)ImageGutter / height,
                (float)((ImageGutter + (double)(right - left) / scale) / width),
                (float)((ImageGutter + (double)(bottom - top) / scale) / height)),
            new(left, top, right, bottom));
    }

    private SKBitmap GetStagingMip(SKImage image, int level)
    {
        if (stagingMip is null || stagedImage != image.UniqueId || stagedLevel > level)
        {
            stagingMip?.Dispose(); stagingMip = null; stagedImage = 0; stagedLevel = 0;
            stagingMip = ReportImageRaster.Decode(image);
            stagedImage = image.UniqueId;
        }
        while (stagedLevel < level)
        {
            var next = ReportImageRaster.DownsampleAligned(stagingMip);
            stagingMip.Dispose(); stagingMip = next; stagedLevel++;
        }
        return stagingMip;
    }

    private static SKBitmap ExtractTile(SKBitmap source, int originX, int originY, int width, int height) =>
        ReportImageRaster.ExtractTile(source, originX, originY, width, height, ImageGutter);

    private static void ValidateSourceImage(SKImage image)
    {
        if (image.Width <= 0 || image.Height <= 0 || (long)image.Width * image.Height > ReportImageRaster.MaximumDecodedPixels)
            throw new InvalidOperationException("Decoded image exceeds the 32-megapixel report image budget.");
    }

    /// <summary>
    /// Exact coverage fallback for arbitrary vector paths. Simple report shapes
    /// use analytic shaders; callers use this only when a path cannot be represented
    /// by those primitives. The returned texture is pinned for the current frame.
    /// </summary>
    public GpuCoverage GetPathMask(SKPath path, SKPaint paint, float rasterScale, SKRect? clip = null)
    {
        CheckFrame();
        ArgumentNullException.ThrowIfNull(path); ArgumentNullException.ThrowIfNull(paint);
        if (path.IsEmpty) return Empty;
        if (!float.IsFinite(rasterScale) || rasterScale <= 0) throw new ArgumentOutOfRangeException(nameof(rasterScale));
        if (paint.ImageFilter is not null || paint.MaskFilter is not null)
            throw new NotSupportedException("Path coverage filters require an explicit effect pass.");
        if (path.FillType is SKPathFillType.InverseEvenOdd or SKPathFillType.InverseWinding)
            throw new NotSupportedException("Inverse path coverage requires an explicit viewport fill.");
        // Bake stroke joins, caps and path effects into the coverage geometry.
        // This makes the cache independent of temporary paint/path lifetimes.
        using var outlineBuilder = new SKPathBuilder();
        using (var stroke = paint.Clone())
        {
            if (stroke.Style != SKPaintStyle.Fill && stroke.StrokeWidth == 0) stroke.StrokeWidth = 1 / rasterScale;
            if (!stroke.GetFillPath(path, outlineBuilder, rasterScale))
                throw new InvalidOperationException("The path could not be converted to finite coverage geometry.");
        }
        using var outline = outlineBuilder.Detach();
        var geometry = outline.TightBounds;
        if (geometry.IsEmpty) return Empty;
        if (clip is { } visible)
        {
            // Antialias coverage extends past the geometric edge. Retain the
            // small fringe; the GPU still applies the exact viewport clip.
            visible.Inflate(Guard / rasterScale, Guard / rasterScale);
            float left = Math.Max(geometry.Left, visible.Left), top = Math.Max(geometry.Top, visible.Top);
            float right = Math.Min(geometry.Right, visible.Right), bottom = Math.Min(geometry.Bottom, visible.Bottom);
            if (right <= left || bottom <= top) return Empty;
            geometry = new(left, top, right, bottom);
        }
        var bounds = RasterBounds(geometry, rasterScale);
        int width = checked((int)bounds.Width), height = checked((int)bounds.Height);
        using var rasterBuilder = new SKPathBuilder { FillType = outline.FillType };
        var transform = new SKMatrix
        {
            ScaleX = rasterScale, ScaleY = rasterScale,
            TransX = -bounds.Left, TransY = -bounds.Top, Persp2 = 1
        };
        rasterBuilder.AddPath(outline, in transform);
        using var rasterOutline = rasterBuilder.Detach();
        var key = PathKey.Create(rasterOutline, width, height, paint.IsAntialias);
        if (masks.TryGetValue(key, out var found))
        {
            found.LastFrame = frame;
            return new(found.Texture, new(0, 0, 1, 1), bounds);
        }
        long bytes = checked((long)width * height * 4);
        EnsureBudget(bytes, addMask: true);
        using var bitmap = Bitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        using (var coveragePaint = new SKPaint { Color = SKColors.White, IsAntialias = paint.IsAntialias, Style = SKPaintStyle.Fill })
        {
            canvas.DrawPath(rasterOutline, coveragePaint);
            canvas.Flush();
        }
        var texture = Create(width, height, 1);
        try
        {
            Upload(texture, 0, 0, 0, width, height, bitmap.GetPixels().ToPointer(), bitmap.RowBytes, checked(bitmap.RowBytes * height));
            masks.Add(key, new(texture, bytes, frame));
            maskBytes += bytes;
            return new(texture, new(0, 0, 1, 1), bounds);
        }
        catch { texture.Dispose(); throw; }
    }

    private GpuCoverage Empty => new(White, SKRect.Empty, SKRect.Empty);

    private void CheckFrame()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (frame == 0) throw new InvalidOperationException("BeginFrame must precede report command recording.");
    }

    private void EvictColdAtlas()
    {
        AtlasPage? oldest = null;
        foreach (var page in pages)
            if (page.LastFrame != frame && (oldest is null || page.LastFrame < oldest.LastFrame)) oldest = page;
        if (oldest is null) throw new InvalidOperationException("Visible text exceeds the four-page GPU glyph atlas budget.");
        foreach (var key in oldest.Keys) glyphs.Remove(key);
        pages.Remove(oldest);
        oldest.Texture.Dispose();
    }

    private void EnsureBudget(long incoming, bool addImage = false, bool addMask = false)
    {
        if (incoming > imageBudget) throw new InvalidOperationException("One report texture exceeds the configured GPU image budget.");
        ImageEntry? OldestImage(out ImageKey key)
        {
            key = default; ImageEntry? result = null;
            foreach (var pair in images)
                if (pair.Value.LastFrame != frame && (result is null || pair.Value.LastFrame < result.LastFrame))
                { key = pair.Key; result = pair.Value; }
            return result;
        }
        ImageEntry? OldestMask(out PathKey key)
        {
            key = default; ImageEntry? result = null;
            foreach (var pair in masks)
                if (pair.Value.LastFrame != frame && (result is null || pair.Value.LastFrame < result.LastFrame))
                { key = pair.Key; result = pair.Value; }
            return result;
        }
        void EvictImage(ImageKey key, ImageEntry entry)
        { images.Remove(key); imageBytes -= entry.Bytes; entry.Texture.Dispose(); }
        void EvictMask(PathKey key, ImageEntry entry)
        { masks.Remove(key); maskBytes -= entry.Bytes; entry.Texture.Dispose(); }

        if (addImage && images.Count >= MaximumImages)
        {
            var image = OldestImage(out var key) ?? throw new InvalidOperationException("Visible report images exceed the GPU image-entry budget.");
            EvictImage(key, image);
        }
        if (addMask && masks.Count >= MaximumMasks)
        {
            var mask = OldestMask(out var key) ?? throw new InvalidOperationException("Visible vector paths exceed the GPU mask-entry budget.");
            EvictMask(key, mask);
        }
        while (imageBytes + maskBytes + temporaryBytes + incoming > imageBudget)
        {
            var image = OldestImage(out var imageKey);
            var mask = OldestMask(out var maskKey);
            if (image is null && mask is null)
                throw new InvalidOperationException("Visible report textures exceed the configured GPU image budget.");
            if (image is not null && (mask is null || image.LastFrame <= mask.LastFrame)) EvictImage(imageKey, image);
            else EvictMask(maskKey, mask!);
        }
    }

    private GpuTexture CreateTemporary(SKBitmap bitmap)
    {
        long bytes = checked((long)bitmap.Width * bitmap.Height * 4);
        EnsureBudget(bytes);
        var texture = Create(bitmap.Width, bitmap.Height, 1);
        try
        {
            Upload(texture, 0, 0, 0, bitmap.Width, bitmap.Height, bitmap.GetPixels().ToPointer(),
                bitmap.RowBytes, checked(bitmap.RowBytes * bitmap.Height));
            temporary.Add(texture); temporaryBytes += bytes;
            return texture;
        }
        catch { texture.Dispose(); throw; }
    }

    private GpuTexture Create(int width, int height, int levels)
    {
        ValidateDimensions(width, height);
        var texture = new GpuTexture(context.Api) { Width = width, Height = height };
        try
        {
            var descriptor = new TextureDescriptor
            {
                Dimension = TextureDimension.Dimension2D,
                Size = new((uint)width, (uint)height, 1),
                Format = TextureFormat.Rgba8Unorm,
                Usage = TextureUsage.CopyDst | TextureUsage.TextureBinding,
                MipLevelCount = (uint)levels,
                SampleCount = 1
            };
            texture.Handle = context.Api.DeviceCreateTexture(context.Device, &descriptor);
            if (texture.Handle == null) throw new InvalidOperationException("WebGPU texture allocation failed.");
            var view = new TextureViewDescriptor
            {
                Dimension = TextureViewDimension.Dimension2D,
                Format = TextureFormat.Rgba8Unorm,
                MipLevelCount = (uint)levels,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };
            texture.View = context.Api.TextureCreateView(texture.Handle, &view);
            if (texture.View == null) throw new InvalidOperationException("WebGPU texture-view allocation failed.");
            BindGroupEntry* entries = stackalloc BindGroupEntry[2];
            entries[0] = new() { Binding = 0, Sampler = sampler };
            entries[1] = new() { Binding = 1, TextureView = texture.View };
            var binding = new BindGroupDescriptor { Layout = textureLayout, EntryCount = 2, Entries = entries };
            texture.BindGroup = context.Api.DeviceCreateBindGroup(context.Device, &binding);
            if (texture.BindGroup == null) throw new InvalidOperationException("WebGPU texture binding failed.");
            return texture;
        }
        catch { texture.Dispose(); throw; }
    }

    private void Upload(GpuTexture texture, int mipLevel, int x, int y, int width, int height, void* data, int rowBytes, int length)
    {
        var destination = new ImageCopyTexture
        {
            Texture = texture.Handle, MipLevel = (uint)mipLevel, Origin = new((uint)x, (uint)y, 0), Aspect = TextureAspect.All
        };
        // QueueWriteTexture does not require the 256-byte row padding imposed by
        // buffer-to-texture command copies. Four-byte RGBA rows are sufficient.
        var layout = new TextureDataLayout { Offset = 0, BytesPerRow = (uint)rowBytes, RowsPerImage = (uint)height };
        var size = new Extent3D((uint)width, (uint)height, 1);
        context.Api.QueueWriteTexture(context.Queue, &destination, data, (nuint)length, &layout, &size);
        TextureUploads++; UploadedBytes += length;
    }

    private SKRect RasterBounds(SKRect bounds, float scale)
    {
        double left = Math.Floor((double)bounds.Left * scale) - Guard;
        double top = Math.Floor((double)bounds.Top * scale) - Guard;
        double right = Math.Ceiling((double)bounds.Right * scale) + Guard;
        double bottom = Math.Ceiling((double)bounds.Bottom * scale) + Guard;
        if (!double.IsFinite(left) || !double.IsFinite(top) || !double.IsFinite(right) || !double.IsFinite(bottom)
            || Math.Abs(left) > 16_777_216 || Math.Abs(top) > 16_777_216 || Math.Abs(right) > 16_777_216 || Math.Abs(bottom) > 16_777_216)
            throw new ArgumentException("Coverage geometry exceeds stable raster-coordinate precision.");
        ValidateDimensions(checked((int)(right - left)), checked((int)(bottom - top)));
        return new((float)left, (float)top, (float)right, (float)bottom);
    }

    private void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > maximumDimension || height > maximumDimension)
            throw new InvalidOperationException($"Report texture dimensions must be between 1 and {maximumDimension} pixels.");
    }

    private static SKBitmap Bitmap(int width, int height) => ReportImageRaster.Create(width, height);

    private static long TextureBytes(int width, int height, int levels)
    {
        long result = 0;
        for (int level = 0; level < levels; level++)
        {
            result = checked(result + (long)width * height * 4);
            width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
        }
        return result;
    }

    private static SKBitmap Downsample(SKBitmap source) => ReportImageRaster.Downsample(source);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var page in pages) page.Texture.Dispose();
        foreach (var entry in images.Values) entry.Texture.Dispose();
        foreach (var entry in masks.Values) entry.Texture.Dispose();
        foreach (var texture in temporary) texture.Dispose();
        White.Dispose();
        stagingMip?.Dispose(); stagingMip = null;
        pages.Clear(); glyphs.Clear(); images.Clear(); masks.Clear(); temporary.Clear(); largeGlyphs.Clear();
        imageBytes = maskBytes = temporaryBytes = 0;
    }

    private sealed class TypefaceIdentity(SKTypeface face)
    {
        public long Value { get; } = Interlocked.Increment(ref nextTypefaceId);
        public bool IntrinsicColor { get; } = ReportGlyphRaster.HasIntrinsicColor(face);
    }
    private static TypefaceIdentity? FontIdentity(SKFont font)
    {
        // Native handles may be recycled after a metrics owner evicts a face.
        // A weak managed identity prevents stale atlas entries from aliasing a
        // subsequently allocated typeface without retaining native font objects.
        var face = font.Typeface;
        return face is null ? null : TypefaceIds.GetValue(face, static value => new(value));
    }
    private readonly record struct GlyphKey(long Typeface, ushort Glyph, float Size, float ScaleX, float SkewX, int Flags, SKColor Foreground)
    {
        public static GlyphKey Create(SKFont font, ushort glyph, SKColor foreground, out bool intrinsicColor)
        {
            var face = FontIdentity(font);
            intrinsicColor = face?.IntrinsicColor is true;
            return new(face?.Value ?? 0, glyph, font.Size, font.ScaleX, font.SkewX,
                (font.Embolden ? 1 : 0) | (font.Subpixel ? 2 : 0) | (font.LinearMetrics ? 4 : 0)
                | (font.ForceAutoHinting ? 8 : 0) | (font.EmbeddedBitmaps ? 16 : 0) | (font.BaselineSnap ? 32 : 0)
                | ((int)font.Edging << 8) | ((int)font.Hinting << 12), intrinsicColor ? foreground : default);
        }
    }
    private readonly record struct PathKey(ulong A, ulong B, ulong C, ulong D)
    {
        public static PathKey Create(SKPath path, int width, int height, bool antialias)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> payload = stackalloc byte[40];
            BinaryPrimitives.WriteInt32LittleEndian(payload, width);
            BinaryPrimitives.WriteInt32LittleEndian(payload[4..], height);
            BinaryPrimitives.WriteInt32LittleEndian(payload[8..], (int)path.FillType);
            BinaryPrimitives.WriteInt32LittleEndian(payload[12..], antialias ? 1 : 0);
            hash.AppendData(payload[..16]);
            using var iterator = path.CreateRawIterator();
            Span<SKPoint> points = stackalloc SKPoint[4];
            SKPathVerb verb;
            int segments = 0;
            while ((verb = iterator.Next(points)) != SKPathVerb.Done)
            {
                if (++segments > 1_048_576) throw new InvalidOperationException("Vector coverage exceeds the path-segment budget.");
                int count = verb switch
                {
                    SKPathVerb.Move => 1, SKPathVerb.Line => 2, SKPathVerb.Quad or SKPathVerb.Conic => 3,
                    SKPathVerb.Cubic => 4, _ => 0
                };
                BinaryPrimitives.WriteInt32LittleEndian(payload, (int)verb);
                for (int i = 0; i < count; i++)
                {
                    BinaryPrimitives.WriteSingleLittleEndian(payload[(4 + i * 8)..], points[i].X);
                    BinaryPrimitives.WriteSingleLittleEndian(payload[(8 + i * 8)..], points[i].Y);
                }
                int length = 4 + count * 8;
                if (verb == SKPathVerb.Conic)
                {
                    BinaryPrimitives.WriteSingleLittleEndian(payload[length..], iterator.ConicWeight());
                    length += 4;
                }
                hash.AppendData(payload[..length]);
            }
            byte[] digest = hash.GetHashAndReset();
            return new(BinaryPrimitives.ReadUInt64LittleEndian(digest), BinaryPrimitives.ReadUInt64LittleEndian(digest.AsSpan(8)),
                BinaryPrimitives.ReadUInt64LittleEndian(digest.AsSpan(16)), BinaryPrimitives.ReadUInt64LittleEndian(digest.AsSpan(24)));
        }
    }
    private sealed record GlyphEntry(AtlasPage Page, GpuCoverage Coverage);
    private readonly record struct ImageKey(uint Image, int Level, int X, int Y);
    private sealed class ImageEntry(GpuTexture texture, long bytes, ulong lastFrame)
    {
        public GpuTexture Texture { get; } = texture;
        public long Bytes { get; } = bytes;
        public ulong LastFrame { get; set; } = lastFrame;
    }
    private sealed class AtlasPage(GpuTexture texture)
    {
        public GpuTexture Texture { get; } = texture;
        public HashSet<GlyphKey> Keys { get; } = [];
        public ulong LastFrame { get; set; }
        private int x, y, rowHeight;
        public bool TryAllocate(int width, int height, out int left, out int top)
        {
            left = top = 0;
            if (width > AtlasSize || height > AtlasSize) return false;
            int nextX = x, nextY = y, nextRowHeight = rowHeight;
            if (nextX + width > AtlasSize) { nextX = 0; nextY += nextRowHeight; nextRowHeight = 0; }
            if (nextY + height > AtlasSize) return false;
            left = nextX; top = nextY;
            x = nextX + width; y = nextY; rowHeight = Math.Max(nextRowHeight, height);
            return true;
        }
    }
}
