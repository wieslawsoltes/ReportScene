using System.Globalization;
using ReportScene.Designer;
using ReportScene.Engine;
using ReportScene.Model;
using ReportScene.Rendering.Skia;
using ReportScene.Samples;
using ReportScene.Serialization;
using SkiaSharp;

namespace ReportScene.Validation;

public static class MatrixRenderingTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action)
        { try { action(); return new("matrix rendering: " + name, true); } catch (Exception e) { return new("matrix rendering: " + name, false, e.ToString()); } }
        yield return Test("authoring representation renders through shared Skia", () =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(600, 200));
            using var renderer = new ReportRenderer(); surface.Canvas.Clear(SKColors.White);
            var matrix = (MatrixItem)MatrixReportingTests.Fixture().Pages[0].Bands[0].Items[0];
            renderer.DrawItem(surface.Canvas, new(matrix, matrix.Bounds), designer: true);
            using var image = surface.Snapshot(); using var pixels = SKBitmap.FromImage(image);
            Conformance.Equal(false, pixels.GetPixel(5, 5) == SKColors.White);
        });
        yield return Test("raw unprepared matrix fails explicitly", () =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(100, 100)); using var renderer = new ReportRenderer();
            var matrix = new MatrixItem(); Conformance.Throws<ReportRenderException>(() => renderer.DrawItem(surface.Canvas, new(matrix, matrix.Bounds)));
        });
        yield return Test("font-backed pagination preserves all source values", () =>
        {
            using var metrics = new SkiaTextMeasurer();
            var report = new ReportPreparer(metrics).Prepare(MatrixReportingTests.Fixture());
            Conformance.Equal(9, report.Pages.Count);
            Conformance.Equal(65, report.Pages.SelectMany(p => p.Items).Count(i => i.Source is TextItem && i.Source.Name == "Grid.Value"));
        });
        yield return Test("sample grand totals agree with independent source sums", () =>
        {
            var document = MatrixSampleReports.Sales(); using var metrics = new SkiaTextMeasurer();
            var report = new ReportPreparer(metrics).Prepare(document);
            decimal expectedRevenue = document.Data[0].Rows.Sum(r => r["Revenue"].AsNumber()), expectedUnits = document.Data[0].Rows.Sum(r => r["Units"].AsNumber());
            var texts = report.Pages.SelectMany(p => p.Items).Where(i => i.Source is TextItem && i.Source.Name == "SalesMatrix.Value").ToArray();
            Conformance.Equal(expectedRevenue.ToString("N2", CultureInfo.GetCultureInfo(document.Culture)), texts.Single(i => i.Source.Id.EndsWith(":r12:Value:c8", StringComparison.Ordinal)).Text);
            Conformance.Equal(expectedUnits.ToString("N0", CultureInfo.GetCultureInfo(document.Culture)), texts.Single(i => i.Source.Id.EndsWith(":r12:Value:c9", StringComparison.Ordinal)).Text);
            Conformance.Equal(true, report.Pages.Count > 1);
        });
        yield return Test("matrix editing and clipboard use transactional document history", () =>
        {
            var document = MatrixReportingTests.Fixture(2, 2); var matrix = (MatrixItem)document.Pages[0].Bands[0].Items[0];
            var session = new DesignerSession(document); session.Select(matrix.Id);
            session.UpdateSelection("Matrix rows", i => i is MatrixItem m ? m with { RowDimensions = [new() { Expression = "Sales.Column" }], RowHeight = 30 } : i);
            Conformance.Equal(30d, ((MatrixItem)session.Selection.Single()).RowHeight);
            session.Undo(); Conformance.Equal(24d, ((MatrixItem)session.Selection.Single()).RowHeight);
            session.Redo(); Conformance.Equal("Sales.Column", ((MatrixItem)session.Selection.Single()).RowDimensions[0].Expression);
            var copied = session.Copy(); session.Paste(copied);
            var clone = (MatrixItem)session.Selection.Single(); Conformance.Equal(false, clone.Id == matrix.Id); Conformance.Equal(30d, clone.RowHeight);
            Conformance.Equal("Sales.Column", ((MatrixItem)ReportJson.DeserializeItems(ReportJson.SerializeItems([clone])).Single()).RowDimensions[0].Expression);
        });
    }
}
