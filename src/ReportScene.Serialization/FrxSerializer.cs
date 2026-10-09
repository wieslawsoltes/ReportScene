using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using ReportScene.Model;

namespace ReportScene.Serialization;

public sealed record FrxImportResult(ReportDocument Document, IReadOnlyList<Diagnostic> Diagnostics)
{ public bool Success => Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error); }

/// <summary>FRX coordinates use 96-DPI units; paper and margins use millimetres. Scripts and external connections are never executed.</summary>
public static class FrxSerializer
{
    private const double PxToPoint = .75, MmToPoint = 72 / 25.4;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly Dictionary<string, BandKind> BandNames = new(StringComparer.Ordinal)
    {
        ["ReportTitleBand"] = BandKind.ReportTitle, ["ReportSummaryBand"] = BandKind.ReportSummary,
        ["PageHeaderBand"] = BandKind.PageHeader, ["PageFooterBand"] = BandKind.PageFooter,
        ["ColumnHeaderBand"] = BandKind.ColumnHeader, ["ColumnFooterBand"] = BandKind.ColumnFooter,
        ["DataHeaderBand"] = BandKind.DataHeader, ["DataBand"] = BandKind.Data,
        ["DataFooterBand"] = BandKind.DataFooter, ["GroupHeaderBand"] = BandKind.GroupHeader,
        ["GroupFooterBand"] = BandKind.GroupFooter, ["ChildBand"] = BandKind.Child, ["OverlayBand"] = BandKind.Overlay
    };
    public static FrxImportResult Import(string xml)
    {
        if (xml.Length > ReportJson.MaximumCharacters) throw new FormatException("FRX input exceeds the report size limit.");
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = ReportJson.MaximumCharacters });
        var root = XDocument.Load(reader, LoadOptions.SetLineInfo).Root ?? throw new FormatException("Missing FRX root.");
        if (root.Name.LocalName != "Report") throw new FormatException("Expected an FRX Report root element.");
        var diagnostics = new List<Diagnostic>();
        void Warn(string message, XElement element, bool error = false) => diagnostics.Add(new("RSFRX001", message, error ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning, Attr(element, "Name", element.Name.LocalName)));
        var report = new ReportDocument { Name = Attr(root, "ReportInfo.Name", "Imported report"), Description = Attr(root, "ReportInfo.Description", ""), Pages = [] };
        foreach (var script in root.Descendants().Where(e => e.Name.LocalName == "ScriptText" && !string.IsNullOrWhiteSpace(e.Value)))
            Warn("C#/VB.NET report scripts are not imported or executed.", script, true);
        foreach (var e in root.Descendants())
        {
            if (e.Ancestors().Count() > 40) throw new FormatException("FRX nesting exceeds 40 levels.");
            foreach (var a in e.Attributes().Where(a => a.Name.LocalName.EndsWith("Event", StringComparison.Ordinal)))
                if (!string.IsNullOrWhiteSpace(a.Value)) Warn($"Event handler '{a.Name}' is not supported.", e, true);
        }
        foreach (var table in root.Descendants().Where(e => e.Name.LocalName == "TableDataSource"))
        {
            string name = Attr(table, "Alias", Attr(table, "Name", "Data"));
            if (report.Data.Any(t => t.Name == name)) continue;
            report.Data.Add(new() { Name = name, Columns = table.Elements().Where(e => e.Name.LocalName == "Column").Select(c => Attr(c, "Alias", Attr(c, "Name", "Column"))).ToList() });
            Warn($"Only the schema of '{name}' was imported. Bind local JSON/CSV data before preparing its rows; connection strings are not opened.", table);
        }
        foreach (var p in root.Descendants().Where(e => e.Name.LocalName == "Parameter"))
            report.Parameters.Add(new(Attr(p, "Name", "Parameter"), Value.From(Attr(p, "Expression", "")), Attr(p, "Description", "")));

        Band ReadBand(XElement element)
        {
            var kind = BandNames[element.Name.LocalName];
            var band = new Band
            {
                Name = Attr(element, "Name", element.Name.LocalName), Kind = kind, Height = Number(element, "Height", 30) * PxToPoint,
                DataSource = element.Attribute("DataSource")?.Value, Filter = element.Attribute("Filter")?.Value,
                VisibleExpression = element.Attribute("VisibleExpression")?.Value,
                CanSplit = Bool(element, "CanBreak", true), KeepWithNext = Bool(element, "KeepWithData", false), StartNewPage = Bool(element, "StartNewPage", false),
                RepeatHeader = Bool(element, "RepeatOnEveryPage", true)
            };
            if (band.DataSource is { } source && !report.Data.Any(t => t.Name.Equals(source, StringComparison.OrdinalIgnoreCase)))
            { report.Data.Add(new() { Name = source }); Warn($"Data source '{source}' has no imported rows.", element); }
            foreach (var sort in element.Elements().Where(e => e.Name.LocalName == "Sort").Elements())
                band.Sort.Add(new(Attr(sort, "Expression", ""), Bool(sort, "Descending", false)));
            foreach (var child in element.Elements())
            {
                if (BandNames.TryGetValue(child.Name.LocalName, out var childKind))
                {
                    if (childKind == BandKind.DataHeader) band = band with { Header = ReadBand(child) };
                    else if (childKind == BandKind.DataFooter) band = band with { Footer = ReadBand(child) };
                    else if (childKind is BandKind.Child or BandKind.Data) band.Children.Add(ReadBand(child));
                    else Warn($"Nested {child.Name.LocalName} is not supported at this position.", child, true);
                    continue;
                }
                if (child.Name.LocalName is "Sort" or "Hyperlink") continue;
                ReportItem? item = ReadItem(child);
                if (item is not null) band.Items.Add(item);
            }
            return band;
        }
        ReportItem? ReadItem(XElement element)
        {
            var bounds = new RectD(Number(element, "Left", 0) * PxToPoint, Number(element, "Top", 0) * PxToPoint, Math.Max(.1, Number(element, "Width", 100) * PxToPoint), Math.Max(.1, Number(element, "Height", 20) * PxToPoint));
            var style = ReadStyle(element);
            string name = Attr(element, "Name", element.Name.LocalName);
            ReportItem? item = element.Name.LocalName switch
            {
                "TextObject" => new TextItem { Text = Attr(element, "Text", "") },
                "LineObject" => new ShapeItem { Shape = ShapeKind.Line, Style = style with { BorderWidth = Math.Max(.75, style.BorderWidth) } },
                "ShapeObject" => new ShapeItem { Shape = Attr(element, "Shape", "Rectangle") switch { "Ellipse" => ShapeKind.Ellipse, "RoundRectangle" => ShapeKind.RoundedRectangle, _ => ShapeKind.Rectangle } },
                "CheckBoxObject" => new CheckBoxItem { Expression = Attr(element, "Expression", Bool(element, "Checked", false) ? "true" : "false") },
                "PictureObject" => new ImageItem { Data = DecodeImage(element), PreserveAspect = Attr(element, "SizeMode", "Zoom") != "StretchImage" },
                "BarcodeObject" => ReadBarcode(element),
                _ => null
            };
            if (item is null) { Warn($"Object '{element.Name.LocalName}' is not supported by this FRX importer. The import is refused rather than silently losing it.", element, true); return null; }
            if (element.Attribute("ImageLocation") is { Value.Length: > 0 }) Warn("External picture locations are not fetched; embed the image first.", element, true);
            if (element.Attribute("Fill") is { } fill && fill.Value != "Solid") Warn($"Fill '{fill.Value}' is not supported.", element, true);
            if (element.Attribute("Format") is { } fmt && fmt.Value is not ("General" or "Number" or "Currency" or "Percent")) Warn($"Text format '{fmt.Value}' is not imported.", element, true);
            if (item is TextItem text)
            {
                string? format = element.Attribute("Format")?.Value switch
                { "Number" => "N" + Attr(element, "Format.DecimalDigits", "2"), "Currency" => "C" + Attr(element, "Format.DecimalDigits", "2"), "Percent" => "P" + Attr(element, "Format.DecimalDigits", "2"), _ => null };
                item = text with { Format = format };
            }
            if (item is not ShapeItem { Shape: ShapeKind.Line }) item = item with { Style = style };
            return item with { Name = name, Bounds = bounds, CanGrow = Bool(element, "CanGrow", false), CanShrink = Bool(element, "CanShrink", false), VisibleExpression = element.Attribute("VisibleExpression")?.Value, Hyperlink = element.Attribute("Hyperlink.Value")?.Value, Bookmark = element.Attribute("Bookmark")?.Value };
        }
        BarcodeItem? ReadBarcode(XElement element)
        {
            string type = Attr(element, "Barcode", "Code128");
            BarcodeKind? kind = type switch { "Code128" => BarcodeKind.Code128, "QR Code" or "QR" => BarcodeKind.QrCode, "EAN13" or "EAN-13" => BarcodeKind.Ean13, "Code39" => BarcodeKind.Code39, "DataMatrix" => BarcodeKind.DataMatrix, "PDF417" => BarcodeKind.Pdf417, _ => null };
            if (kind is null) { Warn($"Barcode symbology '{type}' is not supported.", element, true); return null; }
            return new() { Text = Attr(element, "Text", "1234567890"), Symbology = kind.Value, ShowText = Bool(element, "ShowText", true) };
        }
        byte[] DecodeImage(XElement element)
        {
            string encoded = Attr(element, "Image", "");
            if (encoded.Length == 0) return [];
            try { return Convert.FromBase64String(encoded); }
            catch (FormatException) { Warn("Picture encoding is not a supported embedded base64 image.", element, true); return []; }
        }
        foreach (var element in root.Elements().Where(e => e.Name.LocalName is "ReportPage" or "DialogPage"))
        {
            if (element.Name.LocalName == "DialogPage") { Warn("Dialog pages are not imported.", element, true); continue; }
            double width = Number(element, "PaperWidth", 210) * MmToPoint, height = Number(element, "PaperHeight", 297) * MmToPoint;
            if (Bool(element, "Landscape", false) && width < height) (width, height) = (height, width);
            var page = new ReportPage { Name = Attr(element, "Name", "Page" + (report.Pages.Count + 1)), Width = width, Height = height, Margins = new(Number(element, "LeftMargin", 10) * MmToPoint, Number(element, "TopMargin", 10) * MmToPoint, Number(element, "RightMargin", 10) * MmToPoint, Number(element, "BottomMargin", 10) * MmToPoint) };
            foreach (var child in element.Elements())
            {
                if (!BandNames.ContainsKey(child.Name.LocalName)) { Warn($"Page child '{child.Name.LocalName}' is not supported.", child, true); continue; }
                if (child.Name.LocalName == "GroupHeaderBand")
                {
                    var dataElement = child.Elements().FirstOrDefault(e => e.Name.LocalName == "DataBand");
                    if (dataElement is null) { Warn("Nested or empty FRX groups require manual conversion.", child, true); continue; }
                    var headerClone = new XElement(child.Name, child.Attributes(), child.Elements().Where(e => !BandNames.ContainsKey(e.Name.LocalName)).Select(e => new XElement(e)));
                    var footerElement = child.Elements().FirstOrDefault(e => e.Name.LocalName == "GroupFooterBand");
                    var data = ReadBand(dataElement);
                    data.Groups.Add(new() { Expression = Attr(child, "Condition", ""), Header = ReadBand(headerClone), Footer = footerElement is null ? new() { Kind = BandKind.GroupFooter, Height = 0 } : ReadBand(footerElement) });
                    page.Bands.Add(data);
                }
                else page.Bands.Add(ReadBand(child));
            }
            report.Pages.Add(page);
        }
        if (report.Pages.Count == 0) diagnostics.Add(new("RSFRX002", "The FRX file contains no supported report pages."));
        diagnostics.AddRange(DocumentValidator.Validate(report));
        return new(report, diagnostics);
    }

    public static string Export(ReportDocument document)
    {
        var validation = DocumentValidator.Validate(document);
        if (validation.Count > 0) throw new FormatException(string.Join("\n", validation.Select(d => d.Message)));
        if (DocumentTree.AllItems(document).Any(i => i is TableItem or ChartItem)) throw new NotSupportedException("FRX export of ReportScene tables and charts is not implemented; use the lossless native format.");
        var root = new XElement("Report", new XAttribute("ScriptLanguage", "CSharp"), new XAttribute("ReportInfo.Name", document.Name), new XAttribute("ReportInfo.Description", document.Description));
        var dictionary = new XElement("Dictionary");
        foreach (var table in document.Data)
            dictionary.Add(new XElement("TableDataSource", new XAttribute("Name", table.Name), new XAttribute("ReferenceName", table.Name), new XAttribute("DataType", "System.Int32"), new XAttribute("Enabled", "true"), table.Columns.Select(c => new XElement("Column", new XAttribute("Name", c), new XAttribute("DataType", "System.String")))));
        foreach (var p in document.Parameters) dictionary.Add(new XElement("Parameter", new XAttribute("Name", p.Name), new XAttribute("DataType", "System.String"), new XAttribute("Expression", p.DefaultValue.AsString())));
        root.Add(dictionary);
        XElement WriteItem(ReportItem item)
        {
            string tag = item switch { TextItem => "TextObject", ShapeItem { Shape: ShapeKind.Line } => "LineObject", ShapeItem => "ShapeObject", ImageItem => "PictureObject", CheckBoxItem => "CheckBoxObject", BarcodeItem => "BarcodeObject", _ => throw new NotSupportedException() };
            var e = new XElement(tag, new XAttribute("Name", item.Name), new XAttribute("Left", N(item.Bounds.X / PxToPoint)), new XAttribute("Top", N(item.Bounds.Y / PxToPoint)), new XAttribute("Width", N(item.Bounds.Width / PxToPoint)), new XAttribute("Height", N(item.Bounds.Height / PxToPoint)));
            var s = item.Style;
            e.SetAttributeValue("Font", $"{s.FontFamily}, {N(s.FontSize)}pt" + (s.Bold || s.Italic || s.Underline ? ", style=" + string.Join(", ", new[] { s.Bold ? "Bold" : null, s.Italic ? "Italic" : null, s.Underline ? "Underline" : null }.Where(x => x is not null)) : ""));
            e.SetAttributeValue("TextFill.Color", s.Foreground); e.SetAttributeValue("Fill.Color", s.Fill); e.SetAttributeValue("HorzAlign", s.Alignment); e.SetAttributeValue("VertAlign", s.VerticalAlignment);
            if (s.BorderWidth > 0) { e.SetAttributeValue("Border.Lines", "All"); e.SetAttributeValue("Border.Width", N(s.BorderWidth / PxToPoint)); e.SetAttributeValue("Border.Color", s.BorderColor); }
            if (item.CanGrow) e.SetAttributeValue("CanGrow", true); if (item.CanShrink) e.SetAttributeValue("CanShrink", true);
            if (item.VisibleExpression is not null) e.SetAttributeValue("VisibleExpression", item.VisibleExpression);
            if (item.Hyperlink is not null) e.SetAttributeValue("Hyperlink.Value", item.Hyperlink);
            if (item.Bookmark is not null) e.SetAttributeValue("Bookmark", item.Bookmark);
            switch (item)
            {
                case TextItem t:
                    e.SetAttributeValue("Text", t.Text);
                    if (t.Format is { Length: > 0 } format)
                    {
                        if (format[0] is not ('N' or 'C' or 'P') || !int.TryParse(format[1..], out var digits)) throw new NotSupportedException($"FRX export does not support text format '{format}'.");
                        e.SetAttributeValue("Format", format[0] == 'N' ? "Number" : format[0] == 'C' ? "Currency" : "Percent"); e.SetAttributeValue("Format.DecimalDigits", digits);
                    }
                    break;
                case ShapeItem shape: e.SetAttributeValue("Shape", shape.Shape == ShapeKind.RoundedRectangle ? "RoundRectangle" : shape.Shape); break;
                case CheckBoxItem check: e.SetAttributeValue("Expression", check.Expression); break;
                case ImageItem image: e.SetAttributeValue("Image", Convert.ToBase64String(image.Data)); e.SetAttributeValue("SizeMode", image.PreserveAspect ? "Zoom" : "StretchImage"); break;
                case BarcodeItem barcode: e.SetAttributeValue("Text", barcode.Text); e.SetAttributeValue("Barcode", barcode.Symbology == BarcodeKind.QrCode ? "QR Code" : barcode.Symbology == BarcodeKind.Ean13 ? "EAN13" : barcode.Symbology == BarcodeKind.Pdf417 ? "PDF417" : barcode.Symbology); e.SetAttributeValue("ShowText", barcode.ShowText); break;
            }
            return e;
        }
        XElement WriteBand(Band band)
        {
            var e = new XElement(BandNames.First(kv => kv.Value == band.Kind).Key, new XAttribute("Name", band.Name), new XAttribute("Height", N(band.Height / PxToPoint)), new XAttribute("CanBreak", band.CanSplit));
            if (band.DataSource is not null) e.SetAttributeValue("DataSource", band.DataSource);
            if (band.Filter is not null) e.SetAttributeValue("Filter", band.Filter);
            if (band.VisibleExpression is not null) e.SetAttributeValue("VisibleExpression", band.VisibleExpression);
            if (band.StartNewPage) e.SetAttributeValue("StartNewPage", true);
            foreach (var item in band.Items) e.Add(WriteItem(item));
            if (band.Header is not null) e.Add(WriteBand(band.Header));
            foreach (var child in band.Children) e.Add(WriteBand(child));
            if (band.Footer is not null) e.Add(WriteBand(band.Footer));
            if (band.Sort.Count > 0) e.Add(new XElement("Sort", band.Sort.Select(s => new XElement("Sort", new XAttribute("Expression", s.Expression), new XAttribute("Descending", s.Descending)))));
            foreach (var group in band.Groups.AsEnumerable().Reverse())
            { var wrapper = WriteBand(group.Header); wrapper.SetAttributeValue("Condition", group.Expression); wrapper.Add(e); wrapper.Add(WriteBand(group.Footer)); e = wrapper; }
            return e;
        }
        foreach (var page in document.Pages)
        {
            if (page.Columns != 1) throw new NotSupportedException("Multi-column FRX export is not implemented; native JSON preserves it.");
            var e = new XElement("ReportPage", new XAttribute("Name", page.Name), new XAttribute("PaperWidth", N(page.Width / MmToPoint)), new XAttribute("PaperHeight", N(page.Height / MmToPoint)), new XAttribute("LeftMargin", N(page.Margins.Left / MmToPoint)), new XAttribute("TopMargin", N(page.Margins.Top / MmToPoint)), new XAttribute("RightMargin", N(page.Margins.Right / MmToPoint)), new XAttribute("BottomMargin", N(page.Margins.Bottom / MmToPoint)));
            foreach (var band in page.Bands) e.Add(WriteBand(band));
            root.Add(e);
        }
        return new XDocument(new XDeclaration("1.0", "utf-8", null), root).ToString();
    }
    private static ItemStyle ReadStyle(XElement e)
    {
        string font = Attr(e, "Font", "Arial, 10pt"); var pieces = font.Split(',');
        double size = pieces.Length > 1 && double.TryParse(pieces[1].Trim().Replace("pt", "", StringComparison.OrdinalIgnoreCase), NumberStyles.Float, Invariant, out var n) ? n : 10;
        return new()
        {
            FontFamily = pieces[0].Trim(), FontSize = size, Bold = font.Contains("Bold", StringComparison.OrdinalIgnoreCase), Italic = font.Contains("Italic", StringComparison.OrdinalIgnoreCase), Underline = font.Contains("Underline", StringComparison.OrdinalIgnoreCase),
            Foreground = Color(Attr(e, "TextFill.Color", "Black")), Fill = Color(Attr(e, "Fill.Color", "Transparent")), BorderColor = Color(Attr(e, "Border.Color", "Black")),
            BorderWidth = e.Attribute("Border.Lines") is not null || e.Name.LocalName == "LineObject" ? Number(e, "Border.Width", 1) * PxToPoint : 0,
            Alignment = Enum.TryParse<TextAlign>(Attr(e, "HorzAlign", "Left"), true, out var align) ? align : TextAlign.Left,
            VerticalAlignment = Enum.TryParse<VerticalAlign>(Attr(e, "VertAlign", "Top"), true, out var vertical) ? vertical : VerticalAlign.Top
        };
    }
    private static string Color(string value) => value.StartsWith('#') ? value : value.ToLowerInvariant() switch
    { "black" => "#000000", "white" => "#FFFFFF", "transparent" => "#00000000", "red" => "#FF0000", "blue" => "#0000FF", "green" => "#008000", "gray" or "grey" => "#808080", "silver" => "#C0C0C0", "lightgray" => "#D3D3D3", "yellow" => "#FFFF00", "navy" => "#000080", "orange" => "#FFA500", "maroon" => "#800000", _ => throw new FormatException($"FRX color '{value}' is not supported.") };
    private static string Attr(XElement element, string name, string fallback) => element.Attribute(name)?.Value ?? fallback;
    private static double Number(XElement element, string name, double fallback) => element.Attribute(name) is { } a ? double.Parse(a.Value, Invariant) : fallback;
    private static bool Bool(XElement element, string name, bool fallback) => element.Attribute(name) is { } a ? bool.Parse(a.Value) : fallback;
    private static string N(double number) => number.ToString("0.######", Invariant);
}
