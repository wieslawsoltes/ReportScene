using System.Text;
using ReportScene.Validation;
using ReportScene.Samples;
using ReportScene.Engine;
using ReportScene.Export;
using ReportScene.Serialization;
using ReportScene.Rendering.Skia;

var results = ConformanceSuite.Run();
foreach (var result in results) Console.WriteLine($"{(result.Passed ? "PASS" : "FAIL")} {result.Name}{(result.Error is null ? "" : "\n" + result.Error)}");
Console.WriteLine($"{results.Count(r => r.Passed)}/{results.Count} tests passed.");
if (results.Any(r => !r.Passed)) return 1;

var directory = Path.Combine("artifacts", "samples");
Directory.CreateDirectory(directory);
foreach (var name in SampleReports.Names)
{
    var document = SampleReports.Create(name);
    using var renderer = new ReportRenderer();
    var report = new ReportPreparer(renderer.Metrics).Prepare(document);
    var stem = name.ToLowerInvariant().Replace(' ', '-');
    File.WriteAllText(Path.Combine(directory, stem + ".report.json"), ReportJson.Serialize(document));
    foreach (var format in new[] { ExportFormat.Pdf, ExportFormat.Svg })
    {
        var file = new ReportExporter().Export(report, format);
        File.WriteAllBytes(Path.Combine(directory, stem + (file.Name.EndsWith(".zip", StringComparison.Ordinal) ? ".svg.zip" : format == ExportFormat.Pdf ? ".pdf" : ".svg")), file.Content);
    }
    File.WriteAllBytes(Path.Combine(directory, stem + ".png"), ReportExporter.Png(renderer, report.Pages[0], new(Dpi: 96)));
}
Console.WriteLine($"Wrote real report, PDF, SVG and PNG validation artifacts to {directory}.");
return 0;
