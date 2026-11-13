using System.Numerics;
using System.Runtime.InteropServices;

namespace ReportScene.Rendering.WebGpu;

/// <summary>Submission counters, not GPU timings. Texture counts include shared-cache misses for this frame.</summary>
public readonly record struct WebGpuRenderStatistics(int PrimitiveCount, int DrawCalls, int TextureUploads, long UploadedBytes, int AtlasPages)
{
    public int MaximumTextureDimension { get; init; }
    public int ImageTileSize { get; init; }
    public long CachedImageBytes { get; init; }
    public long CachedPathMaskBytes { get; init; }
    public long ImageStagingBytes { get; init; }
    /// <summary>Native operations performed while recording text commands, excluding preparation and GPU execution.</summary>
    public long TextShapingOperations { get; init; }
    public long TextBoundsQueries { get; init; }
    public long TextFontCreations { get; init; }
    public int GlyphCacheHits { get; init; }
    public int GlyphRasterizations { get; init; }
}

/// <summary>Eight aligned vec4 fields; the exact 128-byte layout is shared with Report2D.wgsl.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct GpuPrimitive
{
    public Vector4 Rect;
    public Vector4 Color;
    public Vector4 Clip;
    public Vector4 Shape;
    public Vector4 Data;
    public Vector4 Uv;
    public Vector4 Quad;
    public Vector4 Extra;
}

internal readonly record struct GpuBatch(GpuTexture Texture, int First, int Count);
