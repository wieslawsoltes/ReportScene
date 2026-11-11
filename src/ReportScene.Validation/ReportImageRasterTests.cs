using ReportScene.Rendering.Skia;
using SkiaSharp;

namespace ReportScene.Validation;

public static class ReportImageRasterTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action)
        { try { action(); return new("image raster: " + name, true); } catch (Exception error) { return new("image raster: " + name, false, error.ToString()); } }
        yield return Test("odd dimensions retain every edge pixel", () =>
        {
            using var source = ReportImageRaster.Create(5, 3);
            for (int y = 0; y < 3; y++) for (int x = 0; x < 5; x++) source.SetPixel(x, y, new((byte)(x * 20 + y * 40), 0, 0));
            using var mip = ReportImageRaster.Downsample(source);
            Conformance.Equal(2, mip.Width); Conformance.Equal(1, mip.Height);
            Conformance.Equal((byte)50, mip.GetPixel(0, 0).Red); Conformance.Equal((byte)100, mip.GetPixel(1, 0).Red);
        });
        yield return Test("long narrow images cannot overflow partition coordinates", () =>
        {
            using var source = ReportImageRaster.Create(100_001, 1);
            source.Erase(new(19, 37, 53));
            using var mip = ReportImageRaster.Downsample(source);
            Conformance.Equal(50_000, mip.Width); Conformance.Equal(1, mip.Height);
            foreach (int x in new[] { 0, 20_000, 25_000, 49_999 }) Conformance.Equal(new SKColor(19, 37, 53), mip.GetPixel(x, 0));
        });
        yield return Test("premultiplied transparent pixels do not bleed colored fringes", () =>
        {
            using var source = ReportImageRaster.Create(2, 1);
            var data = source.GetPixelSpan(); data[0] = 255; data[3] = 255;
            using var mip = ReportImageRaster.Downsample(source);
            Conformance.Equal(true, mip.GetPixelSpan().SequenceEqual(new byte[] { 128, 0, 0, 128 }));
        });
        yield return Test("adjacent tile gutters and second mip samples agree", () =>
        {
            using var source = ReportImageRaster.Create(12, 4);
            for (int y = 0; y < 4; y++) for (int x = 0; x < 12; x++) source.SetPixel(x, y, new((byte)(x * 17), (byte)(y * 40), 80));
            using var left = ReportImageRaster.ExtractTile(source, 0, 0, 12, 12);
            using var right = ReportImageRaster.ExtractTile(source, 4, 0, 12, 12);
            for (int y = 0; y < 12; y++) for (int x = 0; x < 8; x++) Conformance.Equal(left.GetPixel(x + 4, y), right.GetPixel(x, y));
            using var leftMip = ReportImageRaster.Downsample(left); using var rightMip = ReportImageRaster.Downsample(right);
            for (int y = 0; y < 6; y++) for (int x = 0; x < 4; x++) Conformance.Equal(leftMip.GetPixel(x + 2, y), rightMip.GetPixel(x, y));
            Conformance.Equal(source.GetPixel(0, 0), left.GetPixel(0, 0));
            using var last = ReportImageRaster.ExtractTile(source, 8, 0, 12, 12);
            Conformance.Equal(source.GetPixel(11, 3), last.GetPixel(11, 11));
        });
        yield return Test("odd tiled images preserve sample phase when the selected mip changes", () =>
        {
            using var source = ReportImageRaster.Create(9001, 257);
            var data = source.GetPixelSpan();
            for (int y = 0; y < source.Height; y++) for (int x = 0; x < source.Width; x++)
            {
                int pixel = y * source.RowBytes + x * 4;
                data[pixel] = x >= 8192 ? (byte)255 : (byte)0;
                data[pixel + 1] = y == 256 ? (byte)127 : (byte)0;
                data[pixel + 2] = x == 9000 ? (byte)63 : (byte)0;
                data[pixel + 3] = 255;
            }
            using var level1 = ReportImageRaster.DownsampleAligned(source);
            Conformance.Equal(4501, level1.Width); Conformance.Equal(129, level1.Height);
            Conformance.Equal(source.GetPixel(9000, 256), level1.GetPixel(4500, 128));
            AssertAlignedTransition(source, level1, 8192, 0);
            using var level2 = ReportImageRaster.DownsampleAligned(level1);
            using var level3 = ReportImageRaster.DownsampleAligned(level2);
            using var level4 = ReportImageRaster.DownsampleAligned(level3);
            Conformance.Equal(1126, level3.Width); Conformance.Equal(33, level3.Height);
            Conformance.Equal(563, level4.Width); Conformance.Equal(17, level4.Height);
            AssertAlignedTransition(level3, level4, 0, 0);
        });
        yield return Test("aligned mip dimensions retain partial final source cells", () =>
        {
            Conformance.Equal(9001, ReportImageRaster.GetAlignedLevelDimension(9001, 0));
            Conformance.Equal(4501, ReportImageRaster.GetAlignedLevelDimension(9001, 1));
            Conformance.Equal(1126, ReportImageRaster.GetAlignedLevelDimension(9001, 3));
            Conformance.Equal(563, ReportImageRaster.GetAlignedLevelDimension(9001, 4));
            Conformance.Equal(1, ReportImageRaster.GetAlignedLevelDimension(9001, 14));
            Conformance.Equal(33, ReportImageRaster.GetAlignedLevelDimension(257, 3));
            Conformance.Equal(1, ReportImageRaster.GetAlignedLevelDimension(32_000_000, 25));
            Conformance.Equal(1, ReportImageRaster.GetAlignedLevelDimension(int.MaxValue, 31));
            Conformance.Throws<ArgumentOutOfRangeException>(() => ReportImageRaster.GetAlignedLevelDimension(9001, -1));
            Conformance.Throws<ArgumentOutOfRangeException>(() => ReportImageRaster.GetAlignedLevelDimension(9001, 32));
        });
        yield return Test("tagged linear images are converted to sRGB before upload", () =>
        {
            using var space = SKColorSpace.CreateSrgbLinear();
            using var source = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul, space));
            var data = source.GetPixelSpan(); data[0] = data[1] = data[2] = 128; data[3] = 255;
            using var image = SKImage.FromBitmap(source); using var decoded = ReportImageRaster.Decode(image);
            var pixel = decoded.GetPixel(0, 0);
            Conformance.Equal(true, pixel.Red is >= 187 and <= 189 && pixel.Green == pixel.Red && pixel.Blue == pixel.Red && pixel.Alpha == 255);
        });
        yield return Test("invalid staging and tile geometry fail before allocation", () =>
        {
            Conformance.Throws<ArgumentOutOfRangeException>(() => ReportImageRaster.Create(int.MaxValue, 2));
            using var source = ReportImageRaster.Create(2, 2);
            Conformance.Throws<ArgumentOutOfRangeException>(() => ReportImageRaster.ExtractTile(source, 2, 0, 10, 10));
            Conformance.Throws<ArgumentOutOfRangeException>(() => ReportImageRaster.ExtractTile(source, 0, 0, 8, 8, 4));
        });
    }

    private static void AssertAlignedTransition(SKBitmap source, SKBitmap globalNext, int originX, int originY)
    {
        int coreWidth = Math.Min(2048, source.Width - originX), coreHeight = Math.Min(2048, source.Height - originY);
        int width = (coreWidth + 9) & ~1, height = (coreHeight + 9) & ~1;
        using var tileNext = ReportImageRaster.ExtractTileNextMip(source, originX, originY, width, height);
        // A selected-level transition samples these two representations at the
        // same coordinates. The entire patch, including gutters and odd image
        // edges, must match, not just a constant-color interior.
        for (int y = 0; y < tileNext.Height; y++) for (int x = 0; x < tileNext.Width; x++)
        {
            int globalX = Math.Clamp(originX / 2 + x - 2, 0, globalNext.Width - 1);
            int globalY = Math.Clamp(originY / 2 + y - 2, 0, globalNext.Height - 1);
            Conformance.Equal(globalNext.GetPixel(globalX, globalY), tileNext.GetPixel(x, y));
        }
    }
}
