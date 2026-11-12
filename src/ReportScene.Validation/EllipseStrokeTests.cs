using ReportScene.Rendering.Skia;
using SkiaSharp;

namespace ReportScene.Validation;

/// <summary>Native export references for the same Euclidean stroke union used by the GPU ellipse shader.</summary>
public static class EllipseStrokeTests
{
    private const int CenterX = 160, CenterY = 80;
    private static readonly SKPaintStyle[] StrokeStyles = [SKPaintStyle.Stroke, SKPaintStyle.StrokeAndFill];

    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action body)
        {
            try { body(); return new("ellipse stroke: " + name, true); }
            catch (Exception error) { return new("ellipse stroke: " + name, false, error.ToString()); }
        }

        yield return Test("collapsed circles and full-sweep aliases retain the solid stroke union", () =>
        {
            foreach (var style in StrokeStyles)
            foreach (float halfStroke in new[] { 10f, 20f, 40f })
            {
                // A saturated circle is exactly the disk of radius r+h. Native
                // stroking approximates its outer curve and is not the exact
                // circle reference; native filled-circle rasterization is.
                using var native = Render("oval", 10, 10, halfStroke, 1, 1, 255, false, exactCircleReference: true);
                foreach (string operation in new[] { "oval", "circle", "arc-positive", "arc-negative" })
                {
                    using var actual = Render(operation, 10, 10, halfStroke, 1, 1, 255, true, style: style);
                    int outer = 10 + (int)halfStroke;
                    Conformance.Equal((byte)255, actual.GetPixel(CenterX, CenterY).Alpha);
                    Conformance.Equal((byte)255, actual.GetPixel(CenterX + 1, CenterY).Alpha);
                    Conformance.Equal((byte)255, actual.GetPixel(CenterX + outer - 2, CenterY).Alpha);
                    // h > 2r must not leave a moat between the original fill and
                    // an inverted native inner contour. Check the entire radius.
                    for (int radius = 0; radius <= outer - 2; radius++)
                        Conformance.Equal((byte)255, actual.GetPixel(CenterX + radius, CenterY).Alpha);
                    Conformance.Equal((byte)0, actual.GetPixel(CenterX + outer + 2, CenterY).Alpha);
                    Conformance.Equal(native.GetPixel(CenterX + outer, CenterY), actual.GetPixel(CenterX + outer, CenterY));
                }
            }
        });

        yield return Test("eccentric and nonuniformly transformed ovals preserve outer coverage and remaining holes", () =>
        {
            foreach (var style in StrokeStyles)
            foreach (var shape in new[] { (60f, 10f, 20f, 1f, 1f), (10f, 10f, 20f, 3f, 1f), (60f, 10f, 8f, 1f, 1f) })
            {
                var (rx, ry, halfStroke, sx, sy) = shape;
                using var native = Render("oval", rx, ry, halfStroke, sx, sy, 255, false, exactCircleReference: rx == ry, style: style);
                using var actual = Render("oval", rx, ry, halfStroke, sx, sy, 255, true, style: style);
                int right = CenterX + (int)((rx + halfStroke) * sx);
                int bottom = CenterY + (int)((ry + halfStroke) * sy);
                Conformance.Equal(halfStroke >= ry || style == SKPaintStyle.StrokeAndFill ? (byte)255 : (byte)0, actual.GetPixel(CenterX, CenterY).Alpha);
                EqualOuterCoverage(native, actual, right, CenterY, rx == ry ? 0 : 2);
                EqualOuterCoverage(native, actual, CenterX, bottom, rx == ry ? 0 : 2);
                Conformance.Equal((byte)0, actual.GetPixel(right + 2, CenterY).Alpha);
                Conformance.Equal((byte)0, actual.GetPixel(CenterX, bottom + 2).Alpha);
                if (halfStroke < ry)
                {
                    // At (40,0), the nearest boundary of radii(60,10) is 7.36788
                    // units away, so an eight-unit half stroke covers this point.
                    Conformance.Equal((byte)255, actual.GetPixel(CenterX + 40, CenterY).Alpha);
                    Conformance.Equal(true, actual.Bytes.SequenceEqual(native.Bytes));
                }
            }
        });

        yield return Test("solid translucent strokes composite once and dashed paints retain native behavior", () =>
        {
            foreach (var style in StrokeStyles)
            foreach (var shape in new[] { (10f, 10f, 40f, 1f, 1f), (60f, 10f, 20f, 1f, 1f), (10f, 10f, 20f, 3f, 1f) })
            {
                var (rx, ry, halfStroke, sx, sy) = shape;
                using var native = Render("oval", rx, ry, halfStroke, sx, sy, 128, false, exactCircleReference: rx == ry, style: style);
                using var actual = Render("oval", rx, ry, halfStroke, sx, sy, 128, true, style: style);
                int right = CenterX + (int)((rx + halfStroke) * sx);
                Conformance.Equal((byte)128, actual.GetPixel(CenterX, CenterY).Alpha);
                Conformance.Equal((byte)128, actual.GetPixel(CenterX + 5, CenterY).Alpha);
                for (int radius = 0; radius <= right - CenterX - 2; radius++)
                    Conformance.Equal((byte)128, actual.GetPixel(CenterX + radius, CenterY).Alpha);
                Conformance.Equal(true, actual.Pixels.All(pixel => pixel.Alpha <= 128));
                EqualOuterCoverage(native, actual, right, CenterY, rx == ry ? 0 : 2);
            }
            foreach (var style in StrokeStyles)
            {
                using var dashedNative = Render("oval", 10, 10, 20, 1, 1, 128, false, true, style: style);
                using var dashedAdapter = Render("oval", 10, 10, 20, 1, 1, 128, true, true, style: style);
                Conformance.Equal(true, dashedNative.Bytes.SequenceEqual(dashedAdapter.Bytes));
            }
        });
    }

    private static void EqualOuterCoverage(SKBitmap native, SKBitmap actual, int x, int y, int tolerance)
    {
        // Native eccentric-stroke AA and filled outer-outline AA differ by at
        // most two observed raw bytes at this boundary; circle references are
        // exact. Compare premultiplied storage because GetPixel unpremultiplies
        // low-alpha colors and magnifies those edge quantization differences.
        var expectedBytes = native.Bytes; var actualBytes = actual.Bytes;
        int expectedOffset = y * native.RowBytes + x * 4, actualOffset = y * actual.RowBytes + x * 4;
        for (int channel = 0; channel < 4; channel++)
        {
            int expected = expectedBytes[expectedOffset + channel], observed = actualBytes[actualOffset + channel];
            if (Math.Abs(expected - observed) > tolerance)
                throw new InvalidOperationException($"Outer edge ({x},{y}) channel {channel}: expected premultiplied byte {expected}, observed {observed}.");
        }
    }

    private static SKBitmap Render(string operation, float rx, float ry, float halfStroke,
        float sx, float sy, byte alpha, bool adapted, bool dashed = false, bool exactCircleReference = false,
        SKPaintStyle style = SKPaintStyle.Stroke)
    {
        var bitmap = new SKBitmap(new SKImageInfo(320, 160, SKColorType.Rgba8888, SKAlphaType.Premul));
        try
        {
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);
            canvas.Translate(CenterX + .5f, CenterY + .5f);
            canvas.Scale(sx, sy);
            using var paint = new SKPaint
            {
                Color = new(127, 63, 191, alpha), Style = exactCircleReference ? SKPaintStyle.Fill : style,
                StrokeWidth = halfStroke * 2, IsAntialias = true
            };
            using var effect = dashed ? SKPathEffect.CreateDash([3, 4], 0) : null;
            paint.PathEffect = effect;
            var oval = new SKRect(-rx, -ry, rx, ry);
            if (exactCircleReference) canvas.DrawCircle(0, 0, rx + halfStroke, paint);
            else if (!adapted) canvas.DrawOval(oval, paint);
            else
            {
                var report = new SkiaReportCanvas(canvas);
                switch (operation)
                {
                    case "circle": report.DrawCircle(0, 0, rx, paint); break;
                    case "arc-positive": report.DrawArc(oval, 37, 360, false, paint); break;
                    case "arc-negative": report.DrawArc(oval, 113, -720, true, paint); break;
                    default: report.DrawOval(oval, paint); break;
                }
            }
            // The adapter borrows the caller's paint, including reusable effects.
            Conformance.Equal(exactCircleReference ? SKPaintStyle.Fill : style, paint.Style);
            Conformance.Equal(halfStroke * 2, paint.StrokeWidth);
            Conformance.Equal(alpha, paint.Color.Alpha);
            Conformance.Equal(dashed, paint.PathEffect is not null);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }
}
