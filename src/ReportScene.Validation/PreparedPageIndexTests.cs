using ReportScene.Model;
using ReportScene.Rendering.Skia;
using SkiaSharp;

namespace ReportScene.Validation;

public static class PreparedPageIndexTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("page index: " + name, true); } catch (Exception e) { return new("page index: " + name, false, e.ToString()); } }
        PreparedItem Item(int id, RectD bounds, RectD? clip = null) => new(new TextItem { Id = id.ToString() }, bounds, "Row " + id, Clip: clip);
        yield return Test("queries preserve original painter order rather than spatial sort order", () =>
        {
            var p = new PreparedPage(1, 100, 100, [Item(0, new(0, 50, 50, 20)), Item(1, new(0, 0, 50, 20)), Item(2, new(0, 20, 50, 20))]);
            Conformance.Equal("0,1,2", string.Join(',', new PreparedPageIndex(p).Query(new(0, 0, 100, 100)).Select(i => i.Source.Id)));
        });
        yield return Test("randomized queries equal brute-force clipped visibility", () =>
        {
            var r = new Random(191); var items = Enumerable.Range(0, 1000).Select(n => Item(n, new(r.Next(-20, 1000), r.Next(-20, 1000), r.Next(1, 120), r.Next(1, 120)), n % 3 == 0 ? new(100, 100, 600, 600) : null)).ToArray();
            var p = new PreparedPage(1, 1000, 1000, items); var index = new PreparedPageIndex(p);
            for (int n = 0; n < 80; n++)
            {
                var q = new RectD(r.Next(0, 900), r.Next(0, 900), 100, 100);
                var expected = items.Where(i => PreparedVisibility.VisibleBounds(p, i) is { } b && PreparedVisibility.Intersect(b, q) is { Width: > 0, Height: > 0 });
                Conformance.Equal(string.Join(',', expected.Select(i => i.Source.Id)), string.Join(',', index.Query(q).Select(i => i.Source.Id)));
            }
        });
        yield return Test("narrow viewport examines a bounded part of a 100000-row page", () =>
        {
            var items = Enumerable.Range(0, 100000).Select(n => Item(n, new(0, n * 20, 100, 18))).ToArray();
            var index = new PreparedPageIndex(new(1, 200, 2000000, items));
            var hits = index.Query(new(0, 1000000, 200, 200), out int examined);
            Conformance.Equal(10, hits.Count); Conformance.Equal(true, examined < 64);
        });
        yield return Test("long spanning objects do not hide shorter overlapping objects", () =>
        {
            var items = new List<PreparedItem> { Item(999, new(0, 0, 100, 10000)) };
            items.AddRange(Enumerable.Range(0, 1000).Select(n => Item(n, new(0, n * 10, 100, 8))));
            var hits = new PreparedPageIndex(new(1, 100, 10000, items)).Query(new(0, 5000, 100, 10));
            Conformance.Equal("999,500", string.Join(',', hits.Select(i => i.Source.Id)));
        });
        yield return Test("snapshot remains consistent after input-list replacement", () =>
        {
            var items = new List<PreparedItem> { Item(0, new(0, 0, 10, 10)) }; var index = new PreparedPageIndex(new(1, 100, 100, items));
            items.Clear(); items.Add(Item(1, new(50, 50, 10, 10)));
            Conformance.Equal("0", index.Query(new(0, 0, 10, 10)).Single().Source.Id); Conformance.Equal("0", index.Page.Items.Single().Source.Id);
        });
        yield return Test("empty and negative-size viewports have no hits", () =>
        { var index = new PreparedPageIndex(new(1, 100, 100, [Item(0, new(0, 0, 50, 50))])); Conformance.Equal(0, index.Query(new(0, 0, 0, 10)).Count); Conformance.Equal(0, index.Query(new(0, 0, -1, 10)).Count); });
        yield return Test("edge-touch alone is not visible", () =>
        { var index = new PreparedPageIndex(new(1, 100, 100, [Item(0, new(0, 0, 10, 10))])); Conformance.Equal(0, index.Query(new(10, 0, 10, 10)).Count); });
        yield return Test("stroke extents are conservative near a viewport border", () =>
        {
            var s = new ShapeItem { Bounds = new(20, 20, 20, 20), Style = new() { BorderWidth = 4 } };
            var index = new PreparedPageIndex(new(1, 100, 100, [new(s, s.Bounds)])); Conformance.Equal(1, index.Query(new(17, 20, 2, 10)).Count);
        });
        yield return Test("invalid geometry and cancellation are explicit", () =>
        {
            Conformance.Throws<ArgumentException>(() => new PreparedPageIndex(new(1, double.NaN, 100, [])));
            Conformance.Throws<OperationCanceledException>(() => new PreparedPageIndex(new(1, 100, 100, []), new(true)));
            var index = new PreparedPageIndex(new(1, 100, 100, [])); Conformance.Throws<OperationCanceledException>(() => index.Query(new(0, 0, 10, 10), out _, new(true)));
        });
        yield return Test("indexed drawing matches full drawing pixels with clips and painter order", () =>
        {
            using var a = SKSurface.Create(new SKImageInfo(100, 100)); using var b = SKSurface.Create(new SKImageInfo(100, 100)); using var renderer = new ReportRenderer();
            var items = Enumerable.Range(0, 60).Select(n => { var s = new ShapeItem { Bounds = new(n % 8 * 12, n % 9 * 10, 30, 25), Style = new() { Fill = n % 2 == 0 ? "#376A90" : "#DAAB55", BorderWidth = 2 } }; return new PreparedItem(s, s.Bounds, Clip: n % 3 == 0 ? new RectD(20, 20, 50, 50) : null); }).ToArray();
            var page = new PreparedPage(1, 100, 100, items); var viewport = new RectD(15, 25, 65, 40);
            a.Canvas.Clear(SKColors.Transparent); b.Canvas.Clear(SKColors.Transparent);
            a.Canvas.Save(); a.Canvas.ClipRect(ReportRenderer.Rect(viewport)); renderer.DrawPage(a.Canvas, page); a.Canvas.Restore();
            renderer.DrawIndexedPage(b.Canvas, new PreparedPageIndex(page), viewport);
            using var ai = a.Snapshot(); using var bi = b.Snapshot(); using var ab = SKBitmap.FromImage(ai); using var bb = SKBitmap.FromImage(bi);
            Conformance.Equal(true, ab.Bytes.SequenceEqual(bb.Bytes));
        });
    }
}
