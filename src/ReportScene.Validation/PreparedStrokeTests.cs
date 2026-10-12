using ReportScene.Model;
using ReportScene.Rendering.Skia;
using SkiaSharp;

namespace ReportScene.Validation;

public static class PreparedStrokeTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("prepared strokes: " + name, true); } catch (Exception e) { return new("prepared strokes: " + name, false, e.ToString()); } }
        PreparedItem Check(RectD? bounds = null, RectD? clip = null) => new(new CheckBoxItem { Id = "check", Bounds = new(0, 0, 1, 1), Style = new() { Foreground = "#108840" } }, bounds ?? new(30, 30, 140, 140), "False", Clip: clip);
        var edges = new (string Name, RectD Viewport)[] { ("left", new(26, 70, 3, 20)), ("right", new(171, 70, 3, 20)), ("top", new(70, 26, 20, 3)), ("bottom", new(70, 171, 20, 3)) };
        foreach (var edge in edges)
        {
            var current = edge;
            yield return Test(current.Name + " checkbox edge survives viewport culling", () =>
            {
                var item = Check(); var page = new PreparedPage(1, 200, 200, [item]);
                Conformance.Equal(1, new PreparedPageIndex(page).Query(current.Viewport).Count);
                Conformance.Equal(false, item.Bounds.Intersects(current.Viewport));
                EqualPixels(page, current.Viewport, requireInk: true);
            });
        }
        yield return Test("stroke uses prepared size and rounds conservatively to renderer precision", () =>
        {
            var source = Check().Source;
            Conformance.Equal(5d, PreparedPaintBounds.StrokeOutset(source, new(30, 30, 140, 140)));
            Conformance.Equal(.5, PreparedPaintBounds.StrokeOutset(source, new(30, 30, 1, 1)));
            foreach (double width in new[] { .1, 1.00000006, 9.3, 140.00001 })
            {
                var shape = new ShapeItem { Style = new() { BorderWidth = width } };
                Conformance.Equal(true, PreparedPaintBounds.StrokeOutset(shape, new(0, 0, 10, 10)) >= (double)(float)width / 2);
                Conformance.Equal(true, PreparedPaintBounds.StrokeOutset(source, new(0, 0, width, width)) >= Math.Max(1f, (float)width / 14f) / 2d);
            }
        });
        yield return Test("minimum checkbox stroke is visible at magnification", () =>
        {
            var item = Check(new(10, 10, 4, 4)); var page = new PreparedPage(1, 30, 30, [item]);
            var query = new RectD(9.5, 11, .375, 2);
            Conformance.Equal(1, new PreparedPageIndex(page).Query(query).Count);
            EqualPixels(page, query, requireInk: true, scale: 8);
        });
        yield return Test("minimum line and explicit border retain their paint", () =>
        {
            var line = new ShapeItem { Shape = ShapeKind.Line, Style = new() { BorderColor = "#108840" } };
            var page = new PreparedPage(1, 30, 30, [new(line, new(10, 5, 0, 20))]);
            Conformance.Equal(.25, PreparedPaintBounds.StrokeOutset(line, page.Items[0].Bounds));
            EqualPixels(page, new(9.75, 10, .125, 3), requireInk: true, scale: 8);
            var text = new TextItem { Style = new() { BorderColor = "#108840", BorderWidth = 10 } };
            EqualPixels(new PreparedPage(1, 200, 200, [new(text, new(30, 30, 140, 140), "")]), edges[0].Viewport, requireInk: true);
        });
        yield return Test("item clips win over expanded strokes", () =>
        {
            var page = new PreparedPage(1, 200, 200, [Check(clip: new(28, 50, 150, 100))]);
            Conformance.Equal(0, new PreparedPageIndex(page).Query(new(25, 70, 2, 20)).Count);
            EqualPixels(page, edges[0].Viewport, requireInk: true);
            foreach (var clip in new[] { new RectD(0, 0, 0, 10), new RectD(0, 0, -1, 10) })
                Conformance.Equal(0, new PreparedPageIndex(new PreparedPage(1, 200, 200, [Check(clip: clip)])).Query(new(0, 0, 200, 200)).Count);
        });
        yield return Test("off-paper checkbox still paints its clipped extended edge", () =>
        {
            var page = new PreparedPage(1, 200, 200, [Check(new(-138, 30, 140, 140))]);
            EqualPixels(page, new(3, 70, 3, 20), requireInk: true);
            Conformance.Equal(0, new PreparedPageIndex(page).Query(new(-10, 70, 9, 20)).Count);
        });
        yield return Test("search and bookmark geometry stays nominal", () =>
        {
            var item = Check(); var page = new PreparedPage(1, 200, 200, [item]);
            Conformance.Equal(item.Bounds, PreparedVisibility.VisibleBounds(page, item)!.Value);
            Conformance.Equal(new RectD(25, 25, 150, 150), PreparedPaintBounds.VisibleBounds(page, item)!.Value);
        });
        yield return Test("mixed strokes clips and painter order match full raster output", () =>
        {
            var random = new Random(6715); var items = new List<PreparedItem>();
            for (int n = 0; n < 80; n++)
            {
                var style = new ItemStyle { Fill = n % 4 == 0 ? "#70DAAB55" : "#00000000", Foreground = "#108840", BorderColor = "#376A90", BorderWidth = n % 3 == 0 ? 7 : 0 };
                ReportItem source = n % 2 == 0 ? new CheckBoxItem { Style = style } : new ShapeItem { Style = style };
                items.Add(new(source, new(random.Next(0, 160), random.Next(0, 160), random.Next(10, 45), random.Next(10, 45)), "True", Clip: n % 5 == 0 ? new RectD(20, 20, 150, 150) : null));
            }
            var page = new PreparedPage(1, 220, 220, items);
            foreach (var query in new[] { new RectD(10, 10, 190, 190), new RectD(37, 53, 50, 70), new RectD(143, 11, 24, 165) }) EqualPixels(page, query, requireInk: true);
        });
        yield return Test("invalid stroke values cannot silently remove indexed content", () =>
        {
            foreach (double width in new[] { -1d, double.NaN, double.PositiveInfinity, double.MaxValue })
            {
                var item = new PreparedItem(new CheckBoxItem { Style = new() { BorderWidth = width } }, new(30, 30, 140, 140));
                Conformance.Throws<ArgumentException>(() => new PreparedPageIndex(new PreparedPage(1, 200, 200, [item])));
            }
            Conformance.Throws<ArgumentException>(() => PreparedPaintBounds.StrokeOutset(new CheckBoxItem(), new(double.NaN, 0, 10, 10)));
        });
    }
    private static void EqualPixels(PreparedPage page, RectD viewport, bool requireInk, int scale = 1)
    {
        using var reference = SKSurface.Create(new SKImageInfo((int)Math.Ceiling(page.Width * scale), (int)Math.Ceiling(page.Height * scale)));
        using var indexed = SKSurface.Create(new SKImageInfo((int)Math.Ceiling(page.Width * scale), (int)Math.Ceiling(page.Height * scale)));
        using var renderer = new ReportRenderer();
        reference.Canvas.Clear(SKColors.Transparent); indexed.Canvas.Clear(SKColors.Transparent);
        reference.Canvas.Scale(scale); indexed.Canvas.Scale(scale);
        reference.Canvas.Save(); reference.Canvas.ClipRect(ReportRenderer.Rect(viewport)); renderer.DrawPage(reference.Canvas, page); reference.Canvas.Restore();
        renderer.DrawIndexedPage(indexed.Canvas, new PreparedPageIndex(page), viewport);
        using var a = reference.Snapshot(); using var b = indexed.Snapshot(); using var aa = SKBitmap.FromImage(a); using var bb = SKBitmap.FromImage(b);
        if (requireInk) Conformance.Equal(true, aa.Pixels.Any(c => c.Alpha > 0 && (c.Red < 230 || c.Green < 230 || c.Blue < 230)));
        Conformance.Equal(true, aa.Bytes.SequenceEqual(bb.Bytes));
    }
}
