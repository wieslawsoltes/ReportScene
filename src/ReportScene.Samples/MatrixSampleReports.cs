using ReportScene.Model;

namespace ReportScene.Samples;

public static class MatrixSampleReports
{
    public static ReportDocument Sales()
    {
        var data = new DataTable { Name = "Sales", Columns = ["Region", "Product", "Quarter", "Revenue", "Units"] };
        string[] regions = ["Central", "East", "West"], products = ["Designer", "Runtime", "Support"];
        for (int r = 0; r < regions.Length; r++) for (int p = 0; p < products.Length; p++) for (int q = 1; q <= 4; q++)
        {
            data.Rows.Add(new() { ["Region"] = regions[r], ["Product"] = products[p], ["Quarter"] = "Q" + q,
                ["Revenue"] = 1250m + r * 375m + p * 225m + q * 187.5m, ["Units"] = 8 + r * 2 + p + q * 3 });
        }
        var matrix = new MatrixItem
        {
            Name = "SalesMatrix", DataSource = "Sales", Bounds = new(0, 0, 769, 184), RowHeaderWidth = 180, CellWidth = 84,
            RowDimensions = [new() { Expression = "Sales.Region", Title = "Region", Subtotal = true }, new() { Expression = "Sales.Product", Title = "Product" }],
            ColumnDimensions = [new() { Expression = "Sales.Quarter", Title = "Quarter" }],
            Measures = [new() { Expression = "Sales.Revenue", Title = "Revenue", Format = "N2" }, new() { Expression = "Sales.Units", Title = "Units", Format = "N0" }],
            RowHeight = 27, Style = new() { FontSize = 10, Padding = 4, Foreground = "#263B50", BorderColor = "#BECBD7" }
        };
        return new()
        {
            Name = "Matrix sales", Data = [data], Description = "Reusable cross-tab: Region / Product by Quarter, two source-backed measures, subtotals and grand totals.",
            Pages = [new()
            {
                Width = 841.89, Height = 595.276, Margins = new(36, 30, 36, 30), Bands =
                [new() { Kind = BandKind.PageHeader, Name = "MatrixTitle", Height = 68, Items =
                    [new TextItem { Name = "Title", Text = "SALES PERFORMANCE", Bounds = new(0, 0, 760, 34), Style = new() { FontSize = 25, Bold = true, Foreground = "#244F75" } },
                     new TextItem { Name = "Subtitle", Text = "Region / Product × Quarter · Revenue and Units · Source-backed totals", Bounds = new(0, 39, 760, 20), Style = new() { FontSize = 10, Foreground = "#5D7184" } }] },
                 new() { Kind = BandKind.ReportTitle, Name = "MatrixBand", Height = 184, Items = [matrix] },
                 new() { Kind = BandKind.PageFooter, Name = "MatrixFooter", Height = 25, Items =
                    [new TextItem { Name = "Footer", Text = "ReportScene · Matrix sample", Bounds = new(0, 5, 470, 18), Style = new() { FontSize = 8, Foreground = "#5D7184" } },
                     new TextItem { Name = "PageNumber", Text = "Page [PageN] of [TotalPages]", Bounds = new(550, 5, 219, 18), Style = new() { FontSize = 8, Alignment = TextAlign.Right } }] }]
            }]
        };
    }
}
