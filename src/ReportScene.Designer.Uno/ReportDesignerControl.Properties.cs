using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReportScene.Model;
using ReportScene.Rendering.Skia;

namespace ReportScene.Designer.Uno;

public sealed partial class ReportDesignerControl
{
    private void Measurement(string name, double points, Action<double> commit, string id)
    {
        double scale = DesignerGeometry.PointsPerUnit(displayUnit);
        properties.Number(name, points / scale, value => commit(value * scale), id);
    }
    private void RefreshProperties()
    {
        properties.Clear();
        var items = Session.Selection.ToArray();
        if (items.Length == 0)
        {
            var band = Session.SelectedBand;
            if (band is null) { RefreshPageProperties(); return; }
            objectName.Text = band.Name + " · " + band.Kind;
            void EditBand(Func<Band, Band> change) { var current = Session.SelectedBand ?? throw new InvalidOperationException("No band selected."); Session.UpdateBand(change(current)); }
            properties.Category("Band");
            properties.Text("Name", band.Name, text => EditBand(b => b with { Name = RequireName(text) }), "band-name");
            properties.ReadOnly("Kind", band.Kind.ToString(), "band-kind");
            Measurement("Height (" + DesignerGeometry.UnitLabel(displayUnit) + ")", band.Height, n => EditBand(b => b with { Height = Positive(n, "Height", true) }), "band-height");
            if (band.Kind == BandKind.Data)
            {
                properties.Category("Data");
                properties.Choice("Data source", new[] { "" }.Concat(Session.Document.Data.Select(t => t.Name)), band.DataSource ?? "", name => EditBand(b => b with { DataSource = EmptyNull(name) }), "band-source");
                properties.Text("Filter", band.Filter ?? "", text => EditBand(b => b with { Filter = EmptyNull(text) }), "band-filter");
                properties.Text("Sort", string.Join("; ", band.Sort.Select(s => s.Expression + (s.Descending ? " DESC" : " ASC"))), text => EditBand(b => b with { Sort = ParseSort(text) }), "band-sort");
                properties.Text("Parent key", band.ParentKey ?? "", text => EditBand(b => b with { ParentKey = EmptyNull(text) }), "band-parent-key");
                properties.Text("Child key", band.ChildKey ?? "", text => EditBand(b => b with { ChildKey = EmptyNull(text) }), "band-child-key");
                properties.ReadOnly("Groups", band.Groups.Count.ToString());
            }
            properties.Category("Behavior");
            properties.Boolean("Can split", band.CanSplit, value => EditBand(b => b with { CanSplit = value }), "band-can-split");
            properties.Boolean("Keep with next", band.KeepWithNext, value => EditBand(b => b with { KeepWithNext = value }), "band-keep-next");
            properties.Boolean("New page", band.StartNewPage, value => EditBand(b => b with { StartNewPage = value }), "band-new-page");
            properties.Boolean("Repeat header", band.RepeatHeader, value => EditBand(b => b with { RepeatHeader = value }), "band-repeat-header");
            properties.Text("Visible when", band.VisibleExpression ?? "", value => EditBand(b => b with { VisibleExpression = EmptyNull(value) }), "band-visible");
            properties.Text("Alternate fill", band.AlternateFill ?? "", value => EditBand(b => b with { AlternateFill = value.Length == 0 ? null : ValidColor(value) }), "band-alternate-fill");
            return;
        }
        var first = items.FirstOrDefault(i => !i.Locked) ?? items[0];
        objectName.Text = items.Length == 1 ? first.Name + " · " + first.GetType().Name.Replace("Item", "") : items.Length + " selected objects";
        void Edit(string name, Func<ReportItem, ReportItem> change)
        {
            if (!Session.Selection.Any(i => !i.Locked)) throw new InvalidOperationException("The selected objects are locked. Unlock them before editing their properties.");
            Session.UpdateSelection(name, change);
        }
        properties.Category("Object");
        if (items.Length == 1) properties.Text("Name", first.Name, text => Edit("Object name", i => i with { Name = RequireName(text) }), "name");
        else properties.ReadOnly("Selection", items.Length + " objects; displayed values are from the first editable object", "selection-info");
        if (first is TextItem textItem)
        {
            properties.Text("Text", textItem.Text, text => Edit("Text", i => i is TextItem t ? t with { Text = text } : i), "text", true);
            properties.Text("Format", textItem.Format ?? "", text => Edit("Text format", i => i is TextItem t ? t with { Format = EmptyNull(text) } : i), "format");
        }
        if (first is BarcodeItem barcode)
        {
            properties.Text("Data", barcode.Text, text => Edit("Barcode data", i => i is BarcodeItem b ? b with { Text = text } : i), "barcode-data");
            properties.Choice("Symbology", Enum.GetNames<BarcodeKind>(), barcode.Symbology.ToString(), value => Edit("Barcode symbology", i => i is BarcodeItem b ? b with { Symbology = Enum.Parse<BarcodeKind>(value) } : i), "barcode-kind");
            properties.Boolean("Show text", barcode.ShowText, value => Edit("Barcode caption", i => i is BarcodeItem b ? b with { ShowText = value } : i), "barcode-caption");
        }
        if (first is CheckBoxItem checkbox) properties.Text("Expression", checkbox.Expression, text => Edit("Check box expression", i => i is CheckBoxItem c ? c with { Expression = text } : i), "checkbox-expression");
        if (first is ShapeItem shape)
        {
            properties.Choice("Shape", Enum.GetNames<ShapeKind>(), shape.Shape.ToString(), value => Edit("Shape", i => i is ShapeItem s ? s with { Shape = Enum.Parse<ShapeKind>(value) } : i), "shape-kind");
            Measurement("Radius", shape.Radius, value => Edit("Corner radius", i => i is ShapeItem s ? s with { Radius = Positive(value, "Radius", true) } : i), "shape-radius");
        }
        if (first is ChartItem chart)
        {
            properties.Text("Title", chart.Title, text => Edit("Chart title", i => i is ChartItem c ? c with { Title = text } : i), "chart-title");
            properties.Choice("Data source", Session.Document.Data.Select(t => t.Name), chart.DataSource, text => Edit("Chart data source", i => i is ChartItem c ? c with { DataSource = text } : i), "chart-source");
            properties.Text("Category", chart.CategoryColumn, text => Edit("Chart category", i => i is ChartItem c ? c with { CategoryColumn = text } : i), "chart-category");
            properties.Text("Value", chart.ValueExpression, text => Edit("Chart value", i => i is ChartItem c ? c with { ValueExpression = text } : i), "chart-value");
            properties.Choice("Chart type", Enum.GetNames<ChartKind>(), chart.Chart.ToString(), text => Edit("Chart type", i => i is ChartItem c ? c with { Chart = Enum.Parse<ChartKind>(text) } : i), "chart-type");
        }
        if (first is TableItem table)
        {
            properties.Choice("Data source", Session.Document.Data.Select(t => t.Name), table.DataSource, text => Edit("Table data source", i => i is TableItem t ? t with { DataSource = text } : i), "table-source");
            Measurement("Row height", table.RowHeight, value => Edit("Table row height", i => i is TableItem t ? t with { RowHeight = Positive(value, "Row height") } : i), "table-row-height");
            properties.Boolean("Show header", table.ShowHeader, value => Edit("Table header", i => i is TableItem t ? t with { ShowHeader = value } : i), "table-header");
            properties.ReadOnly("Columns", table.Columns.Count + " · double-click to edit");
        }
        if (first is ImageItem image)
        {
            properties.ReadOnly("Image", image.Data.Length == 0 ? "Double-click to load" : (image.Data.Length / 1024d).ToString("0.0") + " KB");
            properties.Boolean("Keep aspect", image.PreserveAspect, value => Edit("Image fit", i => i is ImageItem p ? p with { PreserveAspect = value } : i), "image-aspect");
        }
        properties.Category("Position · " + DesignerGeometry.UnitLabel(displayUnit));
        Measurement("Left", first.Bounds.X, n => Edit("Left", i => i with { Bounds = i.Bounds with { X = Positive(n, "Left", true) } }), "left");
        Measurement("Top", first.Bounds.Y, n => Edit("Top", i => i with { Bounds = i.Bounds with { Y = Positive(n, "Top", true) } }), "top");
        Measurement("Width", first.Bounds.Width, n => Edit("Width", i => i with { Bounds = i.Bounds with { Width = Positive(n, "Width") } }), "width");
        Measurement("Height", first.Bounds.Height, n => Edit("Height", i => i with { Bounds = i.Bounds with { Height = Positive(n, "Height") } }), "height");
        properties.Category("Appearance");
        properties.Text("Font", first.Style.FontFamily, text => Edit("Font family", i => i with { Style = i.Style with { FontFamily = RequireName(text) } }), "font");
        properties.Number("Font size (pt)", first.Style.FontSize, n => Edit("Font size", i => i with { Style = i.Style with { FontSize = Positive(n, "Font size") } }), "font-size");
        properties.Boolean("Bold", first.Style.Bold, value => Edit("Bold", i => i with { Style = i.Style with { Bold = value } }), "bold");
        properties.Boolean("Italic", first.Style.Italic, value => Edit("Italic", i => i with { Style = i.Style with { Italic = value } }), "italic");
        properties.Boolean("Underline", first.Style.Underline, value => Edit("Underline", i => i with { Style = i.Style with { Underline = value } }), "underline");
        properties.Text("Text color", first.Style.Foreground, value => Edit("Text color", i => i with { Style = i.Style with { Foreground = ValidColor(value) } }), "foreground");
        properties.Text("Fill", first.Style.Fill, value => Edit("Fill", i => i with { Style = i.Style with { Fill = ValidColor(value) } }), "fill");
        properties.Text("Border color", first.Style.BorderColor, value => Edit("Border color", i => i with { Style = i.Style with { BorderColor = ValidColor(value) } }), "border-color");
        properties.Number("Border width (pt)", first.Style.BorderWidth, n => Edit("Border width", i => i with { Style = i.Style with { BorderWidth = Positive(n, "Border width", true) } }), "border-width");
        properties.Choice("Alignment", Enum.GetNames<TextAlign>(), first.Style.Alignment.ToString(), value => Edit("Alignment", i => i with { Style = i.Style with { Alignment = Enum.Parse<TextAlign>(value) } }), "alignment");
        properties.Choice("Vertical", Enum.GetNames<VerticalAlign>(), first.Style.VerticalAlignment.ToString(), value => Edit("Vertical alignment", i => i with { Style = i.Style with { VerticalAlignment = Enum.Parse<VerticalAlign>(value) } }), "vertical-alignment");
        Measurement("Padding", first.Style.Padding, n => Edit("Padding", i => i with { Style = i.Style with { Padding = Positive(n, "Padding", true) } }), "padding");
        properties.Category("Behavior");
        properties.Boolean("Can grow", first.CanGrow, value => Edit("Can grow", i => i with { CanGrow = value }), "can-grow");
        properties.Boolean("Can shrink", first.CanShrink, value => Edit("Can shrink", i => i with { CanShrink = value }), "can-shrink");
        properties.Text("Visible when", first.VisibleExpression ?? "", value => Edit("Visibility expression", i => i with { VisibleExpression = EmptyNull(value) }), "visible");
        properties.Text("Hyperlink", first.Hyperlink ?? "", value => Edit("Hyperlink", i => i with { Hyperlink = EmptyNull(value) }), "hyperlink");
        properties.Text("Bookmark", first.Bookmark ?? "", value => Edit("Bookmark", i => i with { Bookmark = EmptyNull(value) }), "bookmark");
        properties.Boolean("Locked", items.All(i => i.Locked), Session.SetSelectionLocked, "locked");
    }
    private static List<Model.SortKey> ParseSort(string text) => text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(s =>
    {
        bool descending = s.EndsWith(" DESC", StringComparison.OrdinalIgnoreCase), ascending = s.EndsWith(" ASC", StringComparison.OrdinalIgnoreCase);
        return new Model.SortKey(descending ? s[..^5].Trim() : ascending ? s[..^4].Trim() : s, descending);
    }).ToList();
    private static string RequireName(string text) => string.IsNullOrWhiteSpace(text) ? throw new FormatException("A name cannot be empty.") : text.Trim();
    private static string? EmptyNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static double Positive(double n, string name, bool zero = false) => double.IsFinite(n) && (zero ? n >= 0 : n > 0) && n <= 1_000_000 ? n : throw new ArgumentException($"{name} must be {(zero ? "nonnegative" : "positive")} and at most one million.");
    private static string ValidColor(string text)
    {
        text = text.Trim(); if (!text.StartsWith('#')) text = "#" + text;
        if (text.Length is not (7 or 9)) throw new FormatException("Use #RRGGBB or #AARRGGBB for a color.");
        _ = ReportRenderer.Color(text); return text.ToUpperInvariant();
    }
}
