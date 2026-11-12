using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ReportScene.Model;
using ReportScene.Rendering.Skia;
using SkiaSharp;

var style = new ItemStyle { FontSize = 10, Padding = 0 };
string[] texts = Enumerable.Range(0, 128).Select(n => $"Invoice AV office café a\u0301 item {n:D3}").ToArray();
string[] probes = Enumerable.Range(0, 8192).Select(n => $"Invoice AV office café a\u0301 unique {n:D5}").ToArray();
using var metrics = new SkiaTextMeasurer();
using var renderer = new ReportRenderer(metrics);
using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
using var bitmap = ReportImageRaster.Create(1000, 520);
using var canvas = new SKCanvas(bitmap);
int Draw(bool layout, int frames)
{
    for (int frame = 0; frame < frames; frame++)
    {
        canvas.Clear(SKColors.White);
        for (int i = 0; i < texts.Length; i++)
        {
            float x = 4 + i % 4 * 245, y = 14 + i / 4 * 16;
            if (layout) renderer.DrawText(canvas, texts[i], new RectD(x, y - 12, 240, 16), style);
            else metrics.Draw(canvas, texts[i], x, y, style, paint);
        }
    }
    canvas.Flush(); return frames * texts.Length;
}
double MeasureCold()
{
    using var cold = new SkiaTextMeasurer(); double sum = 0;
    foreach (string text in probes) sum += cold.MeasureWidth(text, style);
    return sum;
}
// Measure fresh-owner widths before drawing: the historical left-aligned
// renderer measured every line, while the candidate deliberately skips that
// work. Running layout first would prewarm only the historical width code.
var widths = Measure(MeasureCold);
var draws = Measure(() => Draw(false, 64));
string drawPixels = Convert.ToHexString(SHA256.HashData(bitmap.GetPixelSpan()));
var layout = Measure(() => Draw(true, 64));
string layoutPixels = Convert.ToHexString(SHA256.HashData(bitmap.GetPixelSpan()));
var output = new { sourceRevision = args.Length > 1 ? args[1] : "unspecified", timestampUtc = DateTime.UtcNow,
    framework = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(), drawsPerMeasurement = 8192,
    uniqueRuns = 128, widthProbesPerMeasurement = probes.Length,
    workloadOrder = new[] { "widths", "draw", "layout" }, warmupCount = 1, measuredSamples = 5,
    tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "runtime-default",
    rendererAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(SkiaTextMeasurer).Assembly.Location))),
    harnessSourceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Program.cs")))),
    draw = draws.Median, drawSamples = draws.Samples, drawPixels,
    layout = layout.Median, layoutSamples = layout.Samples, layoutPixels,
    widths = widths.Median, widthSamples = widths.Samples,
    note = "The identical public-API harness was separately built against baseline 22939aa and candidate assemblies. Median of five process-local repetitions after one warmup; all times include real CPU drawing/width calls. Warm draw/layout caches; cold unique width owner. Widths run first so the old left-aligned renderer cannot give only the baseline width code an additional warmup. Native surface SHA256 and exact width sums are differential correctness checks. No GPU timing claim." };
string json = JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true });
if (args.Length > 0) File.WriteAllText(args[0], json);
Console.WriteLine(json);

static MeasurementSet Measure(Func<double> action)
{
    _ = action(); var samples = new List<Measurement>();
    for (int i = 0; i < 5; i++)
    {
        long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
        double checksum = action();
        samples.Add(new(Stopwatch.GetElapsedTime(start).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - bytes, checksum));
    }
    return new(samples.OrderBy(sample => sample.Milliseconds).ElementAt(2), samples);
}
sealed record Measurement(double Milliseconds, long AllocatedBytes, double Checksum);
sealed record MeasurementSet(Measurement Median, List<Measurement> Samples);
