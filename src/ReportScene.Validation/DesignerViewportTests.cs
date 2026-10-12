using ReportScene.Designer;
using ReportScene.Model;

namespace ReportScene.Validation;

public static class DesignerViewportTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("designer viewport: " + name, true); } catch (Exception e) { return new("designer viewport: " + name, false, e.ToString()); } }
        DesignObject Item(RectD bounds, double border = 0) => new(new ShapeItem { Style = new() { BorderWidth = border } }, new Band(), bounds);
        yield return Test("nominally offscreen thick borders remain drawable", () =>
        {
            var item = Item(new(100, 100, 100, 100), 40);
            foreach (var viewport in new[] { new RectD(81, 120, 1, 10), new RectD(218, 120, 1, 10), new RectD(120, 81, 10, 1), new RectD(120, 218, 10, 1) })
                Conformance.Equal(true, DesignerPaintCulling.IsVisible(item, viewport, 1));
            Conformance.Equal(false, DesignerPaintCulling.IsVisible(item, new(60, 120, 10, 10), 1));
        });
        yield return Test("selection padding uses device-independent pixels at each zoom", () =>
        {
            var item = Item(new(100, 100, 100, 100));
            Conformance.Equal(true, DesignerPaintCulling.IsVisible(item, new(80, 120, 1, 10), .25));
            Conformance.Equal(false, DesignerPaintCulling.IsVisible(item, new(80, 120, 1, 10), 4));
            Conformance.Equal(true, DesignerPaintCulling.IsVisible(item, new(99, 120, .5, 10), 4));
        });
        yield return Test("empty native viewport emits no report items", () =>
        {
            var item = Item(new(0, 0, 100, 100), 40);
            Conformance.Equal(false, DesignerPaintCulling.IsVisible(item, new(0, 0, 0, 100), 1));
            Conformance.Equal(false, DesignerPaintCulling.IsVisible(item, new(0, 0, 100, 0), 1));
        });
        yield return Test("native scroll geometry locates distant authoring objects", () =>
        {
            var viewport = ReportPreviewViewport.Calculate(new(-4000, -5000, 8000, 9000), 1000, 800, 4);
            Conformance.Equal(true, DesignerPaintCulling.IsVisible(Item(new(1100, 1300, 40, 40)), viewport, 4));
            Conformance.Equal(false, DesignerPaintCulling.IsVisible(Item(new(100, 100, 40, 40)), viewport, 4));
        });
        yield return Test("actual surface edges retain lower right objects across compact scales", () =>
        {
            foreach (double scale in new[] { .51, .85, 1.1333333333333333, 4d })
            {
                var viewport = ReportPreviewViewport.Calculate(new(0, 0, 800 * scale, 1100 * scale), 800 * scale, 1100 * scale, scale);
                Conformance.Equal(true, DesignerPaintCulling.IsVisible(Item(new(775, 1075, 20, 20)), viewport, scale));
            }
        });
        yield return Test("invalid geometry is rejected before draw submission", () =>
        {
            var item = Item(new(0, 0, 10, 10));
            Conformance.Throws<ArgumentOutOfRangeException>(() => DesignerPaintCulling.IsVisible(item, new(0, 0, 10, 10), double.NaN));
            Conformance.Throws<ArgumentException>(() => DesignerPaintCulling.IsVisible(item, new(0, 0, -1, 10), 1));
            Conformance.Throws<ArgumentException>(() => DesignerPaintCulling.IsVisible(Item(new(double.NaN, 0, 10, 10)), new(0, 0, 10, 10), 1));
            Conformance.Throws<ArgumentException>(() => DesignerPaintCulling.IsVisible(Item(new(0, 0, 10, 10), double.PositiveInfinity), new(0, 0, 10, 10), 1));
        });
    }
}
