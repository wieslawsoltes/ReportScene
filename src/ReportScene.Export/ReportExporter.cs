using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using ReportScene.Model;
using ReportScene.Rendering.Skia;
using ReportScene.Serialization;
using ReportScene.Data;
using SkiaSharp;

namespace ReportScene.Export;

public enum ExportFormat { Pdf, Svg, Png, Html, Text, PreparedJson, Csv }
public sealed record ExportOptions(double Dpi = 144, int FirstPage = 1, int LastPage = int.MaxValue, int MaximumPixels = 64_000_000);
public sealed record ExportFile(string Name, string MediaType, byte[] Content);
public interface IReportExporter
{
    IReadOnlyList<ExportFormat> Formats { get; }
    ExportFile Export(PreparedReport report, ExportFormat format, ExportOptions? options = null, ReportDocument? document = null, CancellationToken cancellationToken = default);
}

public sealed class ReportExporter : IReportExporter
{
    public IReadOnlyList<ExportFormat> Formats { get; } = Enum.GetValues<ExportFormat>();
    public ExportFile Export(PreparedReport report, ExportFormat format, ExportOptions? options = null, ReportDocument? document = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report); options ??= new(); ValidateOptions(options);
        var pages = report.Pages.Where(p => p.Number >= options.FirstPage && p.Number <= options.LastPage).ToArray();
        if (pages.Length == 0 && format != ExportFormat.Csv) throw new ArgumentException("The selected page range is empty.");
        if (report.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) throw new InvalidOperationException("Reports containing errors cannot be exported.");
        cancellationToken.ThrowIfCancellationRequested();
        if (format != ExportFormat.Csv) foreach (var page in pages) ValidatePage(page);
        var safeName = string.Concat(report.Name.Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' ? c : '_')).Trim();
        if (safeName.Length == 0) safeName = "Report";
        using var renderer = new ReportRenderer();
        ExportFile TextFile(string extension, string mediaType, string text) => new(safeName + extension, mediaType, Encoding.UTF8.GetBytes(text));
        switch (format)
        {
            case ExportFormat.Pdf:
                using (var output = new MemoryStream())
                {
                    using (var pdf = SKDocument.CreatePdf(output, new SKDocumentPdfMetadata { Title = report.Name, Creator = "ReportScene", Producer = "ReportScene / Skia", RasterDpi = (float)options.Dpi }) ?? throw new NotSupportedException("The current Skia build does not provide a PDF backend."))
                    {
                        foreach (var page in pages) { cancellationToken.ThrowIfCancellationRequested(); var canvas = pdf.BeginPage((float)page.Width, (float)page.Height); renderer.DrawPage(canvas, page); AddLinks(canvas, page, cancellationToken); pdf.EndPage(); }
                        pdf.Close();
                    }
                    return new(safeName + ".pdf", "application/pdf", output.ToArray());
                }
            case ExportFormat.Svg:
                return PagesOrZip("svg", "image/svg+xml", p => Encoding.UTF8.GetBytes(Svg(renderer, p)));
            case ExportFormat.Png:
                return PagesOrZip("png", "image/png", p => Png(renderer, p, options));
            case ExportFormat.Html:
                var html = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>").Append(WebUtility.HtmlEncode(report.Name))
                    .Append("</title><style>body{margin:0;background:#e4e7eb;font-family:Arial,sans-serif}main{display:grid;justify-content:center;gap:20px;padding:24px}.page{background:white;position:relative;box-shadow:0 2px 8px #0003}.page svg{display:block;width:100%;height:100%}.link{position:absolute}@media print{body{background:white}main{display:block;padding:0}.page{box-shadow:none;break-after:page;page-break-after:always;margin:0}.page:last-child{break-after:auto;page-break-after:auto}@page{margin:0}}</style></head><body><main>");
                foreach (var page in pages)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    html.Append("<section class=\"page\" aria-label=\"Page ").Append(page.Number).Append("\" style=\"width:").Append(N(page.Width)).Append("pt;height:").Append(N(page.Height)).Append("pt\">").Append(Svg(renderer, page));
                    foreach (var (item, bounds) in VisibleItems(page, cancellationToken).Where(v => SafeLink(v.Item.Source.Hyperlink)))
                        html.Append("<a class=\"link\" rel=\"noopener noreferrer\" href=\"").Append(WebUtility.HtmlEncode(item.Source.Hyperlink)).Append("\" aria-label=\"").Append(WebUtility.HtmlEncode(item.Text)).Append("\" style=\"left:").Append(N(bounds.X)).Append("pt;top:").Append(N(bounds.Y)).Append("pt;width:").Append(N(bounds.Width)).Append("pt;height:").Append(N(bounds.Height)).Append("pt\"></a>");
                    html.Append("</section>");
                }
                html.Append("</main></body></html>"); return TextFile(".html", "text/html", html.ToString());
            case ExportFormat.Text:
                return TextFile(".txt", "text/plain", string.Join("\n\f\n", pages.Select(p => string.Join('\n', VisibleItems(p, cancellationToken).Where(v => v.Item.Source is TextItem).OrderBy(v => v.Item.Bounds.Y).ThenBy(v => v.Item.Bounds.X).Select(v => v.Item.Text)))));
            case ExportFormat.PreparedJson:
                return TextFile(".prepared.json", "application/json", JsonSerializer.Serialize(report with { Pages = pages }, ReportJsonContext.Default.PreparedReport));
            case ExportFormat.Csv:
                if (document is null || document.Data.Count == 0) throw new ArgumentException("CSV export requires bound data tables. It exports data, not page geometry.");
                if (document.Data.Count == 1) return new(safeName + ".csv", "text/csv", Encoding.UTF8.GetBytes(DataReaders.WriteCsv(document.Data[0])));
                return Zip(safeName + "-data.zip", document.Data.Select((table, index) => new ExportFile($"{index + 1:D3}-{SafeFileName(table.Name)}.csv", "text/csv", Encoding.UTF8.GetBytes(DataReaders.WriteCsv(table)))));
            default: throw new ArgumentOutOfRangeException(nameof(format));
        }
        ExportFile PagesOrZip(string extension, string mediaType, Func<PreparedPage, byte[]> export)
        {
            if (pages.Length == 1) return new(safeName + "." + extension, mediaType, export(pages[0]));
            return Zip(safeName + "-" + extension + ".zip", pages.Select(p => { cancellationToken.ThrowIfCancellationRequested(); return new ExportFile($"page-{p.Number:D4}.{extension}", mediaType, export(p)); }));
        }
    }
    public static byte[] Png(ReportRenderer renderer, PreparedPage page, ExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(renderer); ArgumentNullException.ThrowIfNull(options); ValidatePage(page); ValidateOptions(options);
        double scale = options.Dpi / 72;
        double w = Math.Ceiling(page.Width * scale), h = Math.Ceiling(page.Height * scale);
        if (!double.IsFinite(w) || !double.IsFinite(h) || w <= 0 || h <= 0 || w * h > options.MaximumPixels || w > int.MaxValue || h > int.MaxValue) throw new InvalidOperationException("The requested raster exceeds the configured pixel limit.");
        using var surface = SKSurface.Create(new SKImageInfo((int)w, (int)h, SKColorType.Rgba8888, SKAlphaType.Premul)) ?? throw new InvalidOperationException("Raster surface allocation failed.");
        surface.Canvas.Clear(SKColors.White); surface.Canvas.Scale((float)scale); renderer.DrawPage(surface.Canvas, page);
        using var image = surface.Snapshot(); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }
    public static string Svg(ReportRenderer renderer, PreparedPage page)
    {
        ArgumentNullException.ThrowIfNull(renderer); ValidatePage(page);
        using var stream = new MemoryStream();
        using (var canvas = SKSvgCanvas.Create(SKRect.Create((float)page.Width, (float)page.Height), stream) ?? throw new NotSupportedException("The current Skia build does not provide an SVG backend.")) renderer.DrawPage(canvas, page);
        var svg = Encoding.UTF8.GetString(stream.ToArray());
        int start = svg.IndexOf("<svg", StringComparison.Ordinal); return start >= 0 ? svg[start..] : throw new InvalidOperationException("SVG output is invalid.");
    }
    private static void ValidateOptions(ExportOptions options)
    {
        if (!double.IsFinite(options.Dpi) || options.Dpi < 36 || options.Dpi > 1200 || options.FirstPage < 1 || options.LastPage < options.FirstPage || options.MaximumPixels < 1) throw new ArgumentOutOfRangeException(nameof(options));
    }
    private static void ValidatePage(PreparedPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!double.IsFinite(page.Width) || !double.IsFinite(page.Height) || page.Width <= 0 || page.Height <= 0 || page.Width > float.MaxValue || page.Height > float.MaxValue)
            throw new ArgumentException("Prepared page dimensions must be positive and representable by the drawing backend.", nameof(page));
    }
    private static IEnumerable<(PreparedItem Item, RectD Bounds)> VisibleItems(PreparedPage page, CancellationToken cancellationToken)
    {
        foreach (var item in page.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (PreparedVisibility.VisibleBounds(page, item) is { } bounds) yield return (item, bounds);
        }
    }
    private static bool SafeLink(string? text) => Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "mailto";
    private static void AddLinks(SKCanvas canvas, PreparedPage page, CancellationToken cancellationToken)
    {
        foreach (var (item, bounds) in VisibleItems(page, cancellationToken).Where(v => SafeLink(v.Item.Source.Hyperlink)))
        { using var data = SKData.CreateCopy(Encoding.UTF8.GetBytes(item.Source.Hyperlink + "\0")); canvas.DrawAnnotation(ReportRenderer.Rect(bounds), "URL", data); }
    }
    private static ExportFile Zip(string name, IEnumerable<ExportFile> files)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var file in files) { var entry = zip.CreateEntry(file.Name, CompressionLevel.Fastest); entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero); using var stream = entry.Open(); stream.Write(file.Content); }
        return new(name, "application/zip", output.ToArray());
    }
    private static string SafeFileName(string name) => string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
    private static string N(double n) => n.ToString("0.###", CultureInfo.InvariantCulture);
}
