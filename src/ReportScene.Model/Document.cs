using System.Text.Json.Serialization;

namespace ReportScene.Model;

/// <summary>All report geometry is in points (1/72 inch), independent of device DPI.</summary>
public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    [JsonIgnore] public double Right => X + Width;
    [JsonIgnore] public double Bottom => Y + Height;
    public bool Contains(double x, double y) => x >= X && y >= Y && x <= Right && y <= Bottom;
    public bool Intersects(RectD b) => X <= b.Right && Right >= b.X && Y <= b.Bottom && Bottom >= b.Y;
    public RectD Offset(double dx, double dy) => this with { X = X + dx, Y = Y + dy };
}
public readonly record struct Margins(double Left, double Top, double Right, double Bottom);
public enum TextAlign { Left, Center, Right }
public enum VerticalAlign { Top, Center, Bottom }
public enum BandKind { ReportTitle, ReportSummary, PageHeader, PageFooter, ColumnHeader, ColumnFooter, DataHeader, Data, DataFooter, GroupHeader, GroupFooter, Child, Overlay }
public enum ShapeKind { Rectangle, RoundedRectangle, Ellipse, Line }
public enum ChartKind { Bar, Line, Pie }
public enum BarcodeKind { Code128, QrCode, Ean13, Code39, DataMatrix, Pdf417 }
public enum DiagnosticSeverity { Info, Warning, Error }
public sealed record Diagnostic(string Code, string Message, DiagnosticSeverity Severity = DiagnosticSeverity.Error, string? ObjectId = null);

public sealed record ItemStyle
{
    public string FontFamily { get; init; } = "Arial";
    public double FontSize { get; init; } = 10;
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public string Foreground { get; init; } = "#202020";
    public string Fill { get; init; } = "#00000000";
    public string BorderColor { get; init; } = "#808080";
    public double BorderWidth { get; init; }
    public double Padding { get; init; } = 2;
    public double LineHeight { get; init; } = 1.25;
    public TextAlign Alignment { get; init; }
    public VerticalAlign VerticalAlignment { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TextItem), "text")]
[JsonDerivedType(typeof(ShapeItem), "shape")]
[JsonDerivedType(typeof(ImageItem), "image")]
[JsonDerivedType(typeof(CheckBoxItem), "checkbox")]
[JsonDerivedType(typeof(BarcodeItem), "barcode")]
[JsonDerivedType(typeof(ChartItem), "chart")]
[JsonDerivedType(typeof(TableItem), "table")]
[JsonDerivedType(typeof(MatrixItem), "matrix")]
[JsonDerivedType(typeof(SubreportItem), "subreport")]
public abstract record ReportItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Object";
    public RectD Bounds { get; init; } = new(0, 0, 120, 22);
    public ItemStyle Style { get; init; } = new();
    public string? VisibleExpression { get; init; }
    public string? Hyperlink { get; init; }
    public string? Bookmark { get; init; }
    public bool CanGrow { get; init; }
    public bool CanShrink { get; init; }
    public bool Locked { get; init; }
}
public sealed record TextItem : ReportItem
{
    public string Text { get; init; } = "Text";
    public string? Format { get; init; }
}
public sealed record ShapeItem : ReportItem
{
    public ShapeKind Shape { get; init; }
    public double Radius { get; init; } = 6;
}
public sealed record ImageItem : ReportItem
{
    public byte[] Data { get; init; } = [];
    public bool PreserveAspect { get; init; } = true;
}
public sealed record CheckBoxItem : ReportItem { public string Expression { get; init; } = "true"; }
public sealed record BarcodeItem : ReportItem
{
    public string Text { get; init; } = "1234567890";
    public BarcodeKind Symbology { get; init; } = BarcodeKind.Code128;
    public bool ShowText { get; init; } = true;
}
public sealed record ChartItem : ReportItem
{
    public string DataSource { get; init; } = "Data";
    public string CategoryColumn { get; init; } = "Name";
    public string ValueExpression { get; init; } = "Value";
    public ChartKind Chart { get; init; }
    public string Title { get; init; } = "Chart";
}
public sealed record TableColumn(string Title, string Expression, double Width = 100, string? Format = null);
public sealed record TableItem : ReportItem
{
    public string DataSource { get; init; } = "Data";
    public List<TableColumn> Columns { get; init; } = [];
    public double RowHeight { get; init; } = 22;
    public bool ShowHeader { get; init; } = true;
}

public sealed record SortKey(string Expression, bool Descending = false);
public sealed record GroupDefinition
{
    public string Expression { get; init; } = "Category";
    public Band Header { get; init; } = new() { Kind = BandKind.GroupHeader, Name = "GroupHeader" };
    public Band Footer { get; init; } = new() { Kind = BandKind.GroupFooter, Name = "GroupFooter" };
    public bool KeepHeaderWithFirstRow { get; init; } = true;
}
public sealed record Band
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Data1";
    public BandKind Kind { get; init; } = BandKind.Data;
    public double Height { get; init; } = 30;
    public string? DataSource { get; init; }
    public string? Filter { get; init; }
    public string? VisibleExpression { get; init; }
    public List<SortKey> Sort { get; init; } = [];
    public List<GroupDefinition> Groups { get; init; } = [];
    public Band? Header { get; init; }
    public Band? Footer { get; init; }
    public List<Band> Children { get; init; } = [];
    public List<ReportItem> Items { get; init; } = [];
    public bool CanSplit { get; init; } = true;
    public bool KeepWithNext { get; init; }
    public bool StartNewPage { get; init; }
    public bool RepeatHeader { get; init; } = true;
    public string? AlternateFill { get; init; }
    public string? ParentKey { get; init; }
    public string? ChildKey { get; init; }
}
public sealed record ReportPage
{
    public string Name { get; init; } = "Page1";
    public double Width { get; init; } = 595.276;
    public double Height { get; init; } = 841.89;
    public Margins Margins { get; init; } = new(36, 36, 36, 36);
    public int Columns { get; init; } = 1;
    public double ColumnGap { get; init; } = 12;
    public List<Band> Bands { get; init; } = [];
    [JsonIgnore] public double ContentWidth => Width - Margins.Left - Margins.Right;
    [JsonIgnore] public double ColumnWidth => (ContentWidth - (Columns - 1) * ColumnGap) / Columns;
}
public sealed record ReportDocument
{
    public int FormatVersion { get; init; } = 1;
    public string Name { get; init; } = "Untitled report";
    public string Description { get; init; } = "";
    public string Culture { get; init; } = "en-US";
    public List<ReportPage> Pages { get; init; } = [new()];
    public List<DataTable> Data { get; init; } = [];
    public List<DataRelation> Relations { get; init; } = [];
    public List<ReportParameter> Parameters { get; init; } = [];
    public Dictionary<string, ItemStyle> Styles { get; init; } = new();
    public Dictionary<string, string> Metadata { get; init; } = new();
}

public static class DocumentTree
{
    public static IEnumerable<Band> AllBands(ReportDocument document) => document.Pages.SelectMany(p => p.Bands).SelectMany(Descendants);
    public static IEnumerable<Band> Descendants(Band band)
    {
        yield return band;
        if (band.Header is { } h) foreach (var b in Descendants(h)) yield return b;
        foreach (var g in band.Groups)
        {
            foreach (var b in Descendants(g.Header)) yield return b;
            foreach (var b in Descendants(g.Footer)) yield return b;
        }
        foreach (var c in band.Children) foreach (var b in Descendants(c)) yield return b;
        if (band.Footer is { } f) foreach (var b in Descendants(f)) yield return b;
    }
    public static IEnumerable<ReportItem> AllItems(ReportDocument document) => AllBands(document).SelectMany(b => b.Items);
}
