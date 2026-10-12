using ReportScene.Designer;
using ReportScene.Model;

namespace ReportScene.Validation;

public static class DesignerGeometryTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("designer geometry: " + name, true); } catch (Exception e) { return new("designer geometry: " + name, false, e.ToString()); } }
        var b = new RectD(20, 30, 100, 80);
        var expected = new Dictionary<ResizeHandle, RectD>
        {
            [ResizeHandle.TopLeft] = new(30, 45, 90, 65), [ResizeHandle.Top] = new(20, 45, 100, 65), [ResizeHandle.TopRight] = new(20, 45, 110, 65),
            [ResizeHandle.Right] = new(20, 30, 110, 80), [ResizeHandle.BottomRight] = new(20, 30, 110, 95), [ResizeHandle.Bottom] = new(20, 30, 100, 95),
            [ResizeHandle.BottomLeft] = new(30, 30, 90, 95), [ResizeHandle.Left] = new(30, 30, 90, 80)
        };
        foreach (var entry in expected)
            yield return Test("opposite edge stays anchored for " + entry.Key, () => Conformance.Equal(entry.Value, DesignerGeometry.Resize(b, 10, 15, entry.Key)));
        yield return Test("left and top stop at band origin", () => Conformance.Equal(new RectD(0, 0, 120, 110), DesignerGeometry.Resize(b, -1000, -1000, ResizeHandle.TopLeft)));
        yield return Test("minimum size prevents inverted geometry", () =>
        {
            Conformance.Equal(new RectD(116, 106, 4, 4), DesignerGeometry.Resize(b, 1000, 1000, ResizeHandle.TopLeft));
            Conformance.Equal(new RectD(20, 30, 4, 4), DesignerGeometry.Resize(b, -1000, -1000, ResizeHandle.BottomRight));
        });
        yield return Test("narrow edge handles do not change the orthogonal axis", () =>
        { Conformance.Equal(b, DesignerGeometry.Resize(b, 0, 500, ResizeHandle.Right)); Conformance.Equal(b, DesignerGeometry.Resize(b, 500, 0, ResizeHandle.Bottom)); });
        yield return Test("all eight handles can be hit", () =>
        { foreach (var h in Enum.GetValues<ResizeHandle>()) { var p = DesignerGeometry.HandlePoint(b, h); Conformance.Equal(h, DesignerGeometry.HitHandle(b, p.X, p.Y, 2)!.Value); } });
        yield return Test("center and invalid pointer geometry are not handles", () =>
        { Conformance.Equal(true, DesignerGeometry.HitHandle(b, 70, 70, 2) is null); Conformance.Equal(true, DesignerGeometry.HitHandle(b, double.NaN, 0, 2) is null); });
        yield return Test("invalid deltas and overflow are rejected", () =>
        {
            Conformance.Throws<ArgumentException>(() => DesignerGeometry.Resize(b, double.NaN, 0, ResizeHandle.Right));
            Conformance.Throws<ArgumentException>(() => DesignerGeometry.Resize(new(0, 0, double.MaxValue, 80), double.MaxValue, 0, ResizeHandle.Right));
            Conformance.Throws<ArgumentOutOfRangeException>(() => DesignerGeometry.Resize(b, 0, 0, (ResizeHandle)99));
        });
        yield return Test("display units convert without changing stored points", () =>
        { Conformance.Equal(72d, DesignerGeometry.PointsPerUnit(DesignerUnit.Inches)); Conformance.Equal(true, Math.Abs(25.4 * DesignerGeometry.PointsPerUnit(DesignerUnit.Millimeters) - 72) < 1e-10); Conformance.Equal("cm", DesignerGeometry.UnitLabel(DesignerUnit.Centimeters)); });
    }
}
