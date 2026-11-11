using SkiaSharp;

namespace ReportScene.Rendering.Skia;

/// <summary>
/// Bounded image staging shared by report texture upload and headless conformance.
/// Pixels are premultiplied sRGB RGBA8. Filtering averages those encoded channels;
/// it does not claim a linear-light or HDR color pipeline.
/// </summary>
public static class ReportImageRaster
{
    public const int MaximumDecodedPixels = 32_000_000;

    public static SKBitmap Create(int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height > int.MaxValue / 4)
            throw new ArgumentOutOfRangeException(nameof(width), "Image staging must fit a positive RGBA byte span.");
        // SKBitmap's native image info retains its own color-space reference.
        using var colorSpace = SKColorSpace.CreateSrgb();
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace));
        if (bitmap.GetPixels() == IntPtr.Zero) { bitmap.Dispose(); throw new InvalidOperationException("Report texture staging allocation failed."); }
        bitmap.Erase(SKColors.Transparent);
        return bitmap;
    }

    public static SKBitmap Decode(SKImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Width <= 0 || image.Height <= 0 || (long)image.Width * image.Height > MaximumDecodedPixels)
            throw new InvalidOperationException("Decoded image exceeds the 32-megapixel report image budget.");
        var bitmap = Create(image.Width, image.Height);
        try
        {
            if (!image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0, SKImageCachingHint.Allow))
                throw new InvalidOperationException("The report image could not be decoded into sRGB pixels.");
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    /// <summary>Area-average one global mip level, including every odd-sized edge row and column.</summary>
    public static SKBitmap Downsample(SKBitmap source)
    {
        ValidatePixels(source);
        int width = Math.Max(1, source.Width / 2), height = Math.Max(1, source.Height / 2);
        var target = Create(width, height);
        try
        {
            ReadOnlySpan<byte> input = source.GetPixelSpan();
            Span<byte> output = target.GetPixelSpan();
            for (int y = 0; y < height; y++)
            {
                // A bounded area still permits very long, narrow images. Widen
                // before multiplying either axis, not merely the resulting sum.
                int top = (int)((long)y * source.Height / height), bottom = (int)(((long)y + 1) * source.Height / height);
                for (int x = 0; x < width; x++)
                {
                    int left = (int)((long)x * source.Width / width), right = (int)(((long)x + 1) * source.Width / width);
                    int count = (right - left) * (bottom - top);
                    int red = 0, green = 0, blue = 0, alpha = 0;
                    for (int sy = top; sy < bottom; sy++)
                        for (int sx = left; sx < right; sx++)
                        {
                            int pixel = sy * source.RowBytes + sx * 4;
                            red += input[pixel]; green += input[pixel + 1]; blue += input[pixel + 2]; alpha += input[pixel + 3];
                        }
                    int destination = y * target.RowBytes + x * 4;
                    output[destination] = (byte)((red + count / 2) / count);
                    output[destination + 1] = (byte)((green + count / 2) / count);
                    output[destination + 2] = (byte)((blue + count / 2) / count);
                    output[destination + 3] = (byte)((alpha + count / 2) / count);
                }
            }
            return target;
        }
        catch { target.Dispose(); throw; }
    }

    /// <summary>
    /// The dimension of a power-of-two mip grid whose final cell may extend
    /// beyond the source edge. Unlike a hardware texture mip, it rounds upward.
    /// Every cell therefore retains the same source-coordinate phase at all levels.
    /// </summary>
    public static int GetAlignedLevelDimension(int sourceDimension, int level)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceDimension);
        if ((uint)level > 31) throw new ArgumentOutOfRangeException(nameof(level));
        long scale = 1L << level;
        return (int)(((long)sourceDimension + scale - 1) / scale);
    }

    /// <summary>
    /// Filters a fixed 2-by-2 source footprint, replicating the final row or
    /// column when a dimension is odd. Tiled images use this aligned global
    /// chain so a tile's second mip matches the next globally selected level.
    /// The original image boundary is retained separately through fractional UVs.
    /// </summary>
    public static SKBitmap DownsampleAligned(SKBitmap source)
    {
        ValidatePixels(source);
        int width = GetAlignedLevelDimension(source.Width, 1), height = GetAlignedLevelDimension(source.Height, 1);
        return FilterAlignedRegion(source, width, height, 0, 0);
    }

    /// <summary>
    /// Builds the next mip of an even-sized padded tile directly from the global
    /// source level. Clamping happens after selecting the coarse source cell, so
    /// outer gutters repeat the filtered edge pixel, not the unfiltered fine edge.
    /// Width and height describe the base tile, including its even-sized gutter.
    /// </summary>
    public static SKBitmap ExtractTileNextMip(SKBitmap source, int originX, int originY, int width, int height, int gutter = 4)
    {
        ValidatePixels(source);
        if (originX < 0 || originX >= source.Width) throw new ArgumentOutOfRangeException(nameof(originX));
        if (originY < 0 || originY >= source.Height) throw new ArgumentOutOfRangeException(nameof(originY));
        if (gutter < 0 || (long)gutter * 2 >= width || (long)gutter * 2 >= height) throw new ArgumentOutOfRangeException(nameof(gutter));
        if (((originX | originY | width | height | gutter) & 1) != 0)
            throw new ArgumentException("Aligned tile coordinates, dimensions and gutter must be even.", nameof(width));
        return FilterAlignedRegion(source, width / 2, height / 2, originX / 2 - gutter / 2, originY / 2 - gutter / 2);
    }

    private static SKBitmap FilterAlignedRegion(SKBitmap source, int width, int height, int originX, int originY)
    {
        int columns = GetAlignedLevelDimension(source.Width, 1), rows = GetAlignedLevelDimension(source.Height, 1);
        var target = Create(width, height);
        try
        {
            ReadOnlySpan<byte> input = source.GetPixelSpan();
            Span<byte> output = target.GetPixelSpan();
            for (int y = 0; y < height; y++)
            {
                int cellY = (int)Math.Clamp((long)originY + y, 0, rows - 1);
                int top = cellY * 2, bottom = Math.Min(top + 1, source.Height - 1);
                var upper = input.Slice(top * source.RowBytes, source.RowBytes);
                var lower = input.Slice(bottom * source.RowBytes, source.RowBytes);
                var row = output.Slice(y * target.RowBytes, target.RowBytes);
                for (int x = 0; x < width; x++)
                {
                    int cellX = (int)Math.Clamp((long)originX + x, 0, columns - 1);
                    int left = cellX * 2, right = Math.Min(left + 1, source.Width - 1);
                    int a = left * 4, b = right * 4, destination = x * 4;
                    for (int channel = 0; channel < 4; channel++)
                        row[destination + channel] = (byte)((upper[a + channel] + upper[b + channel]
                            + lower[a + channel] + lower[b + channel] + 2) / 4);
                }
            }
            return target;
        }
        catch { target.Dispose(); throw; }
    }

    /// <summary>
    /// Copies a tile with neighboring source pixels in its gutter. Only pixels
    /// beyond the original image edges are replicated, avoiding seams between tiles.
    /// Width and height include the gutter and any alignment padding.
    /// </summary>
    public static SKBitmap ExtractTile(SKBitmap source, int originX, int originY, int width, int height, int gutter = 4)
    {
        ValidatePixels(source);
        if (originX < 0 || originX >= source.Width) throw new ArgumentOutOfRangeException(nameof(originX));
        if (originY < 0 || originY >= source.Height) throw new ArgumentOutOfRangeException(nameof(originY));
        if (gutter < 0 || (long)gutter * 2 >= width || (long)gutter * 2 >= height) throw new ArgumentOutOfRangeException(nameof(gutter));
        var result = Create(width, height);
        try
        {
            ReadOnlySpan<byte> input = source.GetPixelSpan();
            Span<byte> output = result.GetPixelSpan();
            int sourceLeft = originX - gutter;
            int copyStart = Math.Max(0, -sourceLeft), copyEnd = Math.Min(width, source.Width - sourceLeft);
            int sourceStart = Math.Max(0, sourceLeft);
            for (int y = 0; y < height; y++)
            {
                int sourceY = (int)Math.Clamp((long)originY + y - gutter, 0, source.Height - 1);
                var sourceRow = input.Slice(sourceY * source.RowBytes, source.RowBytes);
                var destinationRow = output.Slice(y * result.RowBytes, result.RowBytes);
                for (int x = 0; x < copyStart; x++) sourceRow[..4].CopyTo(destinationRow.Slice(x * 4, 4));
                int count = checked((copyEnd - copyStart) * 4);
                sourceRow.Slice(sourceStart * 4, count).CopyTo(destinationRow.Slice(copyStart * 4, count));
                for (int x = copyEnd; x < width; x++) sourceRow.Slice((source.Width - 1) * 4, 4).CopyTo(destinationRow.Slice(x * 4, 4));
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    private static void ValidatePixels(SKBitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Width <= 0 || source.Height <= 0 || source.ColorType != SKColorType.Rgba8888 || source.AlphaType != SKAlphaType.Premul
            || source.GetPixels() == IntPtr.Zero || (long)source.RowBytes * source.Height > int.MaxValue)
            throw new ArgumentException("Image staging requires allocated premultiplied RGBA8 pixels with a bounded byte span.", nameof(source));
    }
}
