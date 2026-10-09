using System.Globalization;
using System.Security.Cryptography;
using ReportScene.Model;
using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace ReportScene.Rendering.Skia;

public sealed class ReportRenderException(string objectId, string message, Exception? inner = null) : Exception(message, inner)
{ public string ObjectId { get; } = objectId; }

/// <summary>One vector drawing implementation for the GPU designer, GPU preview, PDF, SVG and raster exports.</summary>
public sealed class ReportRenderer : IDisposable
{
    private readonly bool ownsMetrics;
    private readonly Dictionary<string, SKImage> images = new(StringComparer.Ordinal);
    private readonly Dictionary<(BarcodeKind Kind, string Text), BitMatrix> barcodes = new();
    private static readonly string[] Palette = ["#397BB5", "#E6A13A", "#58A48D", "#9A78B5", "#CE6F79", "#65AFC0", "#8E9C52", "#647888"];
    public SkiaTextMeasurer Metrics { get; }
    public ReportRenderer(SkiaTextMeasurer? metrics = null) { Metrics = metrics ?? new(); ownsMetrics = metrics is null; }
    public static SKRect Rect(RectD r) => SKRect.Create((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height);
    public static SKColor Color(string value) => SKColor.TryParse(value, out var c) ? c : throw new FormatException($"Invalid report color '{value}'.");
    public void DrawPage(SKCanvas canvas, PreparedPage page, RectD? viewport = null, bool paper = true)
    {
        canvas.Save();
        try
        {
            canvas.ClipRect(SKRect.Create((float)page.Width, (float)page.Height));
            if (paper) { using var white = new SKPaint { Color = SKColors.White }; canvas.DrawRect(SKRect.Create((float)page.Width, (float)page.Height), white); }
            foreach (var item in page.Items)
                if (viewport is null || item.Bounds.Intersects(viewport.Value)) DrawItem(canvas, item);
        }
        finally { canvas.Restore(); }
    }
    public void DrawItem(SKCanvas canvas, PreparedItem item, bool designer = false)
    {
        canvas.Save();
        try
        {
            if (item.Clip is { } clip) { if (clip.Width <= 0 || clip.Height <= 0) return; canvas.ClipRect(Rect(clip)); }
            var bounds = item.Bounds; var source = item.Source; var style = source.Style;
            if (source is not ShapeItem) Background(canvas, bounds, style);
            switch (source)
            {
                case TextItem: DrawText(canvas, item.Text, bounds, style); break;
                case ShapeItem shape: DrawShape(canvas, shape, bounds); break;
                case ImageItem image: DrawImage(canvas, image, bounds, designer); break;
                case CheckBoxItem: DrawCheck(canvas, bounds, item.Text.Equals("True", StringComparison.OrdinalIgnoreCase), style); break;
                case BarcodeItem barcode: DrawBarcode(canvas, barcode, item.Text, bounds); break;
                case ChartItem chart: DrawChart(canvas, chart, bounds, item.Points ?? [], designer); break;
                case TableItem table when designer: DrawTableDesign(canvas, table, bounds); break;
                case TableItem: throw new ReportRenderException(source.Id, "Tables must be expanded into prepared cells by the reporting engine.");
                default: throw new ReportRenderException(source.Id, $"No renderer is registered for {source.GetType().Name}.");
            }
        }
        catch (ReportRenderException) { throw; }
        catch (Exception e) when (e is ArgumentException or FormatException or InvalidOperationException)
        { throw new ReportRenderException(item.Source.Id, $"{item.Source.Name}: {e.Message}", e); }
        finally { canvas.Restore(); }
    }
    public void DrawText(SKCanvas canvas, string text, RectD bounds, ItemStyle style)
    {
        var lines = text.Split('\n');
        canvas.Save(); canvas.ClipRect(Rect(bounds));
        using var font = Metrics.CreateFont(style);
        using var paint = new SKPaint { Color = Color(style.Foreground), IsAntialias = true };
        double lineHeight = Metrics.LineHeight(style), available = bounds.Height - 2 * style.Padding;
        double offset = style.VerticalAlignment switch { VerticalAlign.Center => Math.Max(0, (available - lines.Length * lineHeight) / 2), VerticalAlign.Bottom => Math.Max(0, available - lines.Length * lineHeight), _ => 0 };
        var fontMetrics = font.Metrics;
        double topPadding = Math.Max(0, (lineHeight - (fontMetrics.Descent - fontMetrics.Ascent)) / 2);
        for (int n = 0; n < lines.Length; n++)
        {
            string line = lines[n]; double width = Metrics.MeasureWidth(line, style);
            double x = bounds.X + style.Padding + (style.Alignment switch { TextAlign.Center => (bounds.Width - style.Padding * 2 - width) / 2, TextAlign.Right => bounds.Width - style.Padding * 2 - width, _ => 0 });
            double y = bounds.Y + style.Padding + offset + n * lineHeight + topPadding - fontMetrics.Ascent;
            Metrics.Draw(canvas, line, (float)x, (float)y, style, paint);
            if (style.Underline) { paint.StrokeWidth = (float)Math.Max(.5, style.FontSize / 16); canvas.DrawLine((float)x, (float)(y + style.FontSize * .1), (float)(x + width), (float)(y + style.FontSize * .1), paint); }
        }
        canvas.Restore();
    }
    private static void Background(SKCanvas canvas, RectD bounds, ItemStyle style)
    {
        using var paint = new SKPaint { IsAntialias = true, Color = Color(style.Fill) };
        if (paint.Color.Alpha > 0) canvas.DrawRect(Rect(bounds), paint);
        if (style.BorderWidth > 0) { paint.Style = SKPaintStyle.Stroke; paint.Color = Color(style.BorderColor); paint.StrokeWidth = (float)style.BorderWidth; canvas.DrawRect(Rect(bounds), paint); }
    }
    private static void DrawShape(SKCanvas canvas, ShapeItem shape, RectD bounds)
    {
        using var paint = new SKPaint { IsAntialias = true, Color = Color(shape.Style.Fill) };
        void Draw()
        {
            switch (shape.Shape)
            {
                case ShapeKind.Rectangle: canvas.DrawRect(Rect(bounds), paint); break;
                case ShapeKind.RoundedRectangle: canvas.DrawRoundRect(Rect(bounds), (float)shape.Radius, (float)shape.Radius, paint); break;
                case ShapeKind.Ellipse: canvas.DrawOval(Rect(bounds), paint); break;
                case ShapeKind.Line: canvas.DrawLine((float)bounds.X, (float)bounds.Y, (float)bounds.Right, (float)bounds.Bottom, paint); break;
            }
        }
        if (shape.Shape != ShapeKind.Line && paint.Color.Alpha > 0) Draw();
        if (shape.Style.BorderWidth > 0 || shape.Shape == ShapeKind.Line)
        { paint.Style = SKPaintStyle.Stroke; paint.Color = Color(shape.Style.BorderColor); paint.StrokeWidth = (float)Math.Max(shape.Shape == ShapeKind.Line ? .5 : 0, shape.Style.BorderWidth); Draw(); }
    }
    private void DrawImage(SKCanvas canvas, ImageItem source, RectD bounds, bool designer)
    {
        if (source.Data.Length == 0)
        {
            if (designer)
            {
                Background(canvas, bounds, new() { Fill = "#F4F6F8", BorderWidth = .6, BorderColor = "#8997A5" });
                DrawText(canvas, "Picture\nDouble-click to load", bounds, new() { FontSize = 9, Foreground = "#69798B", Alignment = TextAlign.Center, VerticalAlignment = VerticalAlign.Center });
            }
            return;
        }
        string hash = Convert.ToHexString(SHA256.HashData(source.Data));
        if (!images.TryGetValue(hash, out var image))
        {
            using var data = SKData.CreateCopy(source.Data);
            using var codec = SKCodec.Create(data) ?? throw new FormatException("The embedded picture is not a supported image.");
            if (codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 32_000_000) throw new FormatException("Decoded image exceeds 32 megapixels.");
            image = SKImage.FromEncodedData(data) ?? throw new FormatException("Image decoding failed.");
            if (images.Count >= 24) { foreach (var old in images.Values) old.Dispose(); images.Clear(); }
            images.Add(hash, image);
        }
        if (source.PreserveAspect)
        {
            double scale = Math.Min(bounds.Width / image.Width, bounds.Height / image.Height);
            double w = image.Width * scale, h = image.Height * scale;
            bounds = new(bounds.X + (bounds.Width - w) / 2, bounds.Y + (bounds.Height - h) / 2, w, h);
        }
        canvas.DrawImage(image, Rect(bounds), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
    }
    private static void DrawCheck(SKCanvas canvas, RectD bounds, bool isChecked, ItemStyle style)
    {
        float side = (float)Math.Min(bounds.Width, bounds.Height);
        var box = SKRect.Create((float)bounds.X, (float)bounds.Y, side, side);
        using var paint = new SKPaint { Color = Color(style.Foreground), Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1, side / 14), IsAntialias = true };
        canvas.DrawRect(box, paint);
        if (!isChecked) return;
        using var path = new SKPath();
        path.MoveTo(box.Left + side * .2f, box.Top + side * .5f); path.LineTo(box.Left + side * .43f, box.Top + side * .75f); path.LineTo(box.Left + side * .82f, box.Top + side * .23f);
        canvas.DrawPath(path, paint);
    }
    public BitMatrix EncodeBarcode(BarcodeKind kind, string text)
    {
        if (string.IsNullOrEmpty(text)) throw new ArgumentException("A barcode cannot encode empty text.");
        if (text.Length > 4096) throw new ArgumentException("Barcode input exceeds 4096 characters.");
        if (barcodes.TryGetValue((kind, text), out var result)) return result;
        var format = kind switch { BarcodeKind.QrCode => BarcodeFormat.QR_CODE, BarcodeKind.Code128 => BarcodeFormat.CODE_128, BarcodeKind.Ean13 => BarcodeFormat.EAN_13, BarcodeKind.Code39 => BarcodeFormat.CODE_39, BarcodeKind.DataMatrix => BarcodeFormat.DATA_MATRIX, BarcodeKind.Pdf417 => BarcodeFormat.PDF_417, _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
        var hints = new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = kind == BarcodeKind.QrCode ? 4 : 10 };
        if (kind is BarcodeKind.QrCode or BarcodeKind.Pdf417) hints[EncodeHintType.CHARACTER_SET] = "UTF-8";
        result = new MultiFormatWriter().encode(text, format, 0, 0, hints);
        if (barcodes.Count >= 128) barcodes.Clear();
        barcodes[(kind, text)] = result; return result;
    }
    private void DrawBarcode(SKCanvas canvas, BarcodeItem barcode, string text, RectD bounds)
    {
        var matrix = EncodeBarcode(barcode.Symbology, text);
        double captionHeight = barcode.ShowText ? Math.Min(15, bounds.Height * .3) : 0;
        var codeBounds = bounds with { Height = bounds.Height - captionHeight };
        bool square = barcode.Symbology is BarcodeKind.QrCode or BarcodeKind.DataMatrix;
        double moduleX = codeBounds.Width / matrix.Width, moduleY = codeBounds.Height / matrix.Height;
        if (square) moduleX = moduleY = Math.Min(moduleX, moduleY);
        double originX = codeBounds.X + (codeBounds.Width - moduleX * matrix.Width) / 2;
        double originY = codeBounds.Y + (codeBounds.Height - moduleY * matrix.Height) / 2;
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = false };
        canvas.DrawRect(Rect(codeBounds), paint); paint.Color = SKColors.Black;
        for (int y = 0; y < matrix.Height; y++)
        {
            for (int x = 0; x < matrix.Width;)
            {
                if (!matrix[x, y]) { x++; continue; }
                int start = x++; while (x < matrix.Width && matrix[x, y]) x++;
                canvas.DrawRect(SKRect.Create((float)(originX + start * moduleX), (float)(originY + y * moduleY), (float)((x - start) * moduleX), (float)moduleY), paint);
            }
        }
        if (barcode.ShowText) DrawText(canvas, text, new(bounds.X, bounds.Bottom - captionHeight, bounds.Width, captionHeight), barcode.Style with { FontSize = Math.Min(9, captionHeight * .7), Alignment = TextAlign.Center, Padding = 0 });
    }
    private void DrawChart(SKCanvas canvas, ChartItem chart, RectD bounds, IReadOnlyList<ChartPoint> points, bool designer)
    {
        canvas.Save(); canvas.ClipRect(Rect(bounds));
        var titleStyle = chart.Style with { FontSize = Math.Max(11, chart.Style.FontSize), Bold = true, Padding = 0 };
        DrawText(canvas, chart.Title, new(bounds.X + 6, bounds.Y + 4, bounds.Width - 12, 23), titleStyle);
        if (points.Count == 0)
        { DrawText(canvas, designer ? $"{chart.Chart} chart\n{chart.DataSource} · {chart.CategoryColumn} / {chart.ValueExpression}\nPreview to evaluate data" : "No data", new(bounds.X, bounds.Y + 30, bounds.Width, bounds.Height - 30), new() { Foreground = "#768594", Alignment = TextAlign.Center, VerticalAlignment = VerticalAlign.Center }); canvas.Restore(); return; }
        using var paint = new SKPaint { IsAntialias = true };
        if (chart.Chart == ChartKind.Pie)
        {
            var positive = points.Where(p => p.Value > 0).ToArray(); decimal total = positive.Sum(p => p.Value);
            if (total <= 0) { DrawText(canvas, "Pie charts require positive values", new(bounds.X, bounds.Y + 35, bounds.Width, 30), new()); canvas.Restore(); return; }
            double diameter = Math.Min(bounds.Height - 40, bounds.Width * .65);
            var circle = SKRect.Create((float)(bounds.X + 10), (float)(bounds.Y + 32), (float)diameter, (float)diameter); float start = -90;
            for (int n = 0; n < positive.Length; n++)
            {
                float sweep = (float)(positive[n].Value / total * 360m); paint.Color = Color(Palette[n % Palette.Length]);
                canvas.DrawArc(circle, start, sweep, true, paint); start += sweep;
                if (n < 16) { var legend = new RectD(bounds.X + diameter + 24, bounds.Y + 40 + n * 17, Math.Max(20, bounds.Width - diameter - 30), 16); canvas.DrawRect((float)legend.X - 10, (float)legend.Y + 3, 6, 6, paint); DrawText(canvas, positive[n].Label, legend, new() { FontSize = 8, Padding = 0 }); }
            }
            canvas.Restore(); return;
        }
        double left = bounds.X + 49, right = bounds.Right - 12, top = bounds.Y + 35, bottom = bounds.Bottom - 30;
        if (right <= left || bottom <= top) { canvas.Restore(); return; }
        decimal min = Math.Min(0, points.Min(p => p.Value)), max = Math.Max(0, points.Max(p => p.Value));
        if (min == max) max = min + 1;
        double Y(decimal value) => bottom - (double)((value - min) / (max - min)) * (bottom - top);
        for (int tick = 0; tick <= 4; tick++)
        {
            decimal value = min + (max - min) * tick / 4;
            float y = (float)Y(value); paint.Color = Color("#DFE5EA"); paint.StrokeWidth = .5f;
            canvas.DrawLine((float)left, y, (float)right, y, paint);
            DrawText(canvas, value.ToString("0.##", CultureInfo.InvariantCulture), new(bounds.X, y - 6, 43, 15), new() { FontSize = 8, Alignment = TextAlign.Right, Foreground = "#647080", Padding = 0 });
        }
        double step = (right - left) / points.Count;
        using var path = new SKPath();
        for (int n = 0; n < points.Count; n++)
        {
            float x = (float)(left + step * (n + .5)), y = (float)Y(points[n].Value);
            paint.Color = Color(Palette[0]);
            if (chart.Chart == ChartKind.Bar)
            {
                float zero = (float)Y(0); canvas.DrawRect(SKRect.Create((float)(left + step * (n + .15)), Math.Min(y, zero), (float)(step * .7), Math.Max(.5f, Math.Abs(y - zero))), paint);
            }
            else { if (n == 0) path.MoveTo(x, y); else path.LineTo(x, y); canvas.DrawCircle(x, y, 2.4f, paint); }
            int stride = Math.Max(1, (int)Math.Ceiling(points.Count / Math.Max(1d, (right - left) / 56)));
            if (n % stride == 0) DrawText(canvas, points[n].Label, new(x - Math.Max(step, 55) / 2, bottom + 7, Math.Max(step, 55), 18), new() { FontSize = 8, Alignment = TextAlign.Center, Foreground = "#647080", Padding = 0 });
        }
        if (chart.Chart == ChartKind.Line) { paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = 2; canvas.DrawPath(path, paint); }
        canvas.Restore();
    }
    private void DrawTableDesign(SKCanvas canvas, TableItem table, RectD bounds)
    {
        double total = table.Columns.Sum(c => c.Width); if (total <= 0) return;
        double x = bounds.X;
        foreach (var column in table.Columns)
        {
            double width = column.Width / total * bounds.Width;
            for (int row = 0; row < Math.Max(2, Math.Min(10, (int)(bounds.Height / table.RowHeight))); row++)
            {
                var cell = new RectD(x, bounds.Y + row * table.RowHeight, width, table.RowHeight);
                var style = table.Style with { Fill = row == 0 ? "#E1EAF4" : "#FFFFFF", BorderWidth = .5, BorderColor = "#AEBCC8", Bold = row == 0 };
                Background(canvas, cell, style); DrawText(canvas, row == 0 ? column.Title : "[" + column.Expression + "]", cell, style);
            }
            x += width;
        }
    }
    public void Dispose()
    { foreach (var image in images.Values) image.Dispose(); images.Clear(); barcodes.Clear(); if (ownsMetrics) Metrics.Dispose(); }
}
