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

var directory = Path.Combine("artifacts", "samples"); Directory.CreateDirectory(directory);
foreach (var name in SampleReports.Names.Concat(["Matrix sales", "Subreport dashboard", "Subreport master-detail"]))
{
    var document = name switch
    {
        "Matrix sales" => MatrixSampleReports.Sales(), "Subreport dashboard" => SubreportSampleReports.Dashboard(),
        "Subreport master-detail" => SubreportSampleReports.MasterDetail(), _ => SampleReports.Create(name)
    };
    using var renderer = new ReportRenderer();
    var report = new ReportPreparer(renderer.Metrics).Prepare(document); var stem = name.ToLowerInvariant().Replace(' ', '-');
    File.WriteAllText(Path.Combine(directory, stem + ".report.json"), ReportJson.Serialize(document));
    foreach (var format in new[] { ExportFormat.Pdf, ExportFormat.Svg })
    {
        var file = new ReportExporter().Export(report, format);
        File.WriteAllBytes(Path.Combine(directory, stem + (file.Name.EndsWith(".zip", StringComparison.Ordinal) ? ".svg.zip" : format == ExportFormat.Pdf ? ".pdf" : ".svg")), file.Content);
    }
    File.WriteAllBytes(Path.Combine(directory, stem + ".png"), ReportExporter.Png(renderer, report.Pages[0], new(Dpi: 96)));
    if (name == "Matrix sales" || name.StartsWith("Subreport", StringComparison.Ordinal)) for (int page = 1; page < report.Pages.Count; page++)
        File.WriteAllBytes(Path.Combine(directory, stem + "-page-" + (page + 1) + ".png"), ReportExporter.Png(renderer, report.Pages[page], new(Dpi: 96)));
}
Console.WriteLine($"Wrote real report, PDF, SVG and PNG validation artifacts to {directory}.");
return 0;
