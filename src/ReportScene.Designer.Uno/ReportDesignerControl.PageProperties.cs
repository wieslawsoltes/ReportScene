using ReportScene.Model;

namespace ReportScene.Designer.Uno;

public sealed partial class ReportDesignerControl
{
    private void RefreshPageProperties()
    {
        var page = Session.Page;
        objectName.Text = page.Name + " · ReportPage";
        void Edit(System.Func<ReportPage, ReportPage> change) => Session.UpdatePageProperties(change(Session.Page));
        properties.Category("Design");
        properties.Text("Name", page.Name, value => Edit(p => p with { Name = RequireName(value) }), "page-name");
        properties.ReadOnly("Units", DesignerGeometry.UnitLabel(displayUnit) + " (stored as points)", "page-units");
        properties.Category("Paper");
        properties.Boolean("Landscape", page.Width > page.Height, value => Edit(p => value == (p.Width > p.Height) ? p : p with { Width = p.Height, Height = p.Width }), "page-landscape");
        Measurement("Paper width", page.Width, value => Edit(p => p with { Width = Positive(value, "Paper width") }), "page-width");
        Measurement("Paper height", page.Height, value => Edit(p => p with { Height = Positive(value, "Paper height") }), "page-height");
        properties.Category("Margins");
        Measurement("Left", page.Margins.Left, value => Edit(p => p with { Margins = p.Margins with { Left = Positive(value, "Left margin", true) } }), "page-margin-left");
        Measurement("Top", page.Margins.Top, value => Edit(p => p with { Margins = p.Margins with { Top = Positive(value, "Top margin", true) } }), "page-margin-top");
        Measurement("Right", page.Margins.Right, value => Edit(p => p with { Margins = p.Margins with { Right = Positive(value, "Right margin", true) } }), "page-margin-right");
        Measurement("Bottom", page.Margins.Bottom, value => Edit(p => p with { Margins = p.Margins with { Bottom = Positive(value, "Bottom margin", true) } }), "page-margin-bottom");
        properties.Category("Columns");
        properties.Number("Count", page.Columns, value =>
        {
            if (value < 1 || value > 64 || value != System.Math.Truncate(value)) throw new System.ArgumentException("Column count must be an integer between 1 and 64.");
            Edit(p => p with { Columns = (int)value });
        }, "page-columns");
        Measurement("Gap", page.ColumnGap, value => Edit(p => p with { ColumnGap = Positive(value, "Column gap", true) }), "page-column-gap");
        properties.ReadOnly("Printable width", (page.ColumnWidth / DesignerGeometry.PointsPerUnit(displayUnit)).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), "page-printable-width");
    }
}
