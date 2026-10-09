using ReportScene.Model;

namespace ReportScene.Samples;

public static class SampleReports
{
    public static IReadOnlyList<string> Names { get; } = ["Invoice", "Grouped sales", "Master-detail", "Mailing labels", "Tables and charts"];
    public static ReportDocument Create(string name) => name.ToLowerInvariant() switch
    { "grouped sales" => GroupedSales(), "master-detail" => MasterDetail(), "mailing labels" => Labels(), "tables and charts" => TablesAndCharts(), _ => Invoice() };
    private static TextItem Text(string name, string text, double x, double y, double width, double height, double size = 10, bool bold = false, TextAlign align = TextAlign.Left, string? format = null, string color = "#242424")
        => new() { Name = name, Text = text, Bounds = new(x, y, width, height), Format = format, Style = new() { FontSize = size, Bold = bold, Alignment = align, Foreground = color } };
    private static Band Footer() => new() { Name = "PageFooter", Kind = BandKind.PageFooter, Height = 24, Items = [Text("FooterNote", "Created with ReportScene", 0, 6, 300, 16, 8, color: "#747474"), Text("PageNumber", "Page [PageN] of [TotalPages]", 370, 6, 153, 16, 8, align: TextAlign.Right)] };
    public static ReportDocument Invoice()
    {
        var lines = new DataTable { Name = "Items", Columns = ["Description", "Quantity", "Price"] };
        string[] names = ["Report design and template development", "Data integration services", "Reusable reporting components", "Desktop application deployment", "Browser preview configuration", "Quality assurance and validation", "Developer documentation", "Training and onboarding"];
        for (int n = 0; n < names.Length; n++) lines.Rows.Add(new() { ["Description"] = names[n], ["Quantity"] = n % 3 + 1, ["Price"] = 75m + n * 12.5m });
        var data = new Band
        {
            Name = "InvoiceItems", Kind = BandKind.Data, DataSource = "Items", Height = 32, CanSplit = false, AlternateFill = "#F2F5F8",
            Header = new() { Kind = BandKind.DataHeader, Name = "ColumnHeadings", Height = 30, Items = [new ShapeItem { Name = "HeaderFill", Bounds = new(0, 0, 523, 28), Style = new() { Fill = "#244F75" } }, Text("DescriptionHeader", "DESCRIPTION", 8, 6, 290, 20, 9, true, color: "#FFFFFF"), Text("QuantityHeader", "QTY", 305, 6, 40, 20, 9, true, TextAlign.Right, color: "#FFFFFF"), Text("RateHeader", "RATE", 350, 6, 76, 20, 9, true, TextAlign.Right, color: "#FFFFFF"), Text("AmountHeader", "AMOUNT", 432, 6, 83, 20, 9, true, TextAlign.Right, color: "#FFFFFF")] },
            Items = [Text("ItemDescription", "[Items.Description]", 8, 7, 290, 20), Text("ItemQuantity", "[Items.Quantity]", 305, 7, 40, 20, align: TextAlign.Right), Text("ItemPrice", "[Items.Price]", 350, 7, 76, 20, align: TextAlign.Right, format: "C2"), Text("ItemAmount", "[Items.Quantity * Items.Price]", 432, 7, 83, 20, align: TextAlign.Right, format: "C2")]
        };
        return new()
        {
            Name = "Professional invoice", Data = [lines], Parameters = [new("Customer", "Northwind Trading Ltd.", "Customer name", true), new("InvoiceNumber", "INV-2026-001", "Invoice number"), new("DueDate", "30 October 2026", "Payment due")],
            Pages = [new() { Bands = [
                new() { Name = "InvoiceTitle", Kind = BandKind.ReportTitle, Height = 172, Items = [
                    Text("Company", "CONTOSO", 0, 0, 310, 42, 28, true, color: "#244F75"), Text("CompanySubtitle", "PROFESSIONAL SERVICES", 2, 43, 280, 20, 9, color: "#6D7781"),
                    Text("InvoiceLabel", "INVOICE", 330, 4, 193, 40, 27, true, TextAlign.Right, color: "#244F75"), Text("InvoiceNumber", "[Parameters.InvoiceNumber]", 330, 45, 193, 20, 11, align: TextAlign.Right),
                    new ShapeItem { Name = "TitleRule", Shape = ShapeKind.Line, Bounds = new(0, 80, 523, .75), Style = new() { BorderColor = "#CDD7E0", BorderWidth = .75 } },
                    Text("BillToCaption", "BILL TO", 0, 99, 280, 18, 9, true, color: "#6D7781"), Text("Customer", "[Parameters.Customer]", 0, 119, 310, 22, 12, true),
                    Text("DueCaption", "PAYMENT DUE", 340, 99, 183, 18, 9, true, TextAlign.Right, color: "#6D7781"), Text("DueDate", "[Parameters.DueDate]", 340, 119, 183, 22, 11, align: TextAlign.Right)
                ] }, data,
                new() { Name = "InvoiceSummary", Kind = BandKind.ReportSummary, Height = 154, Items = [
                    new ShapeItem { Name = "TotalBackground", Bounds = new(290, 18, 233, 46), Style = new() { Fill = "#E8EEF4" } }, Text("TotalCaption", "TOTAL DUE", 300, 32, 98, 25, 11, true), Text("GrandTotal", "[Sum(Items.Quantity * Items.Price)]", 400, 29, 115, 30, 17, true, TextAlign.Right, "C2", "#244F75"),
                    Text("Terms", "Thank you for your business.\nPlease quote your invoice number when making payment.", 0, 87, 410, 43, 10, color: "#63707B"),
                    new BarcodeItem { Name = "InvoiceQr", Symbology = BarcodeKind.QrCode, Text = "[Parameters.InvoiceNumber]", Bounds = new(443, 80, 72, 72), ShowText = false }
                ] }, Footer()
            ] }]
        };
    }
    public static ReportDocument GroupedSales(int count = 90)
    {
        var data = new DataTable { Name = "Sales", Columns = ["Order", "Region", "Product", "Amount"] };
        string[] regions = ["Central", "East", "West"], products = ["Developer license", "Design services", "Support subscription", "Training package"];
        for (int n = 0; n < count; n++) data.Rows.Add(new() { ["Order"] = "SO-" + (n + 1).ToString("D4"), ["Region"] = regions[n % 3], ["Product"] = products[n % 4], ["Amount"] = 75m + n * 17.25m });
        return new() { Name = "Sales by region", Data = [data], Pages = [new() { Bands = [
            new() { Kind = BandKind.ReportTitle, Name = "ReportTitle", Height = 64, Items = [Text("Title", "SALES BY REGION", 0, 0, 523, 36, 24, true, color: "#244F75"), Text("Subtitle", "Grouped data · subtotals · repeating headers", 0, 38, 523, 20, 10, color: "#63707B")] },
            new() { Kind = BandKind.Data, Name = "SalesRows", DataSource = "Sales", Height = 25, CanSplit = false, AlternateFill = "#F2F5F8", Header = new() { Name = "SalesHeadings", Kind = BandKind.DataHeader, Height = 26, Items = [Text("OrderHeader", "ORDER", 0, 4, 100, 20, 9, true), Text("ProductHeader", "PRODUCT", 115, 4, 260, 20, 9, true), Text("AmountHeader", "AMOUNT", 400, 4, 123, 20, 9, true, TextAlign.Right)] },
                Groups = [new() { Expression = "Region", Header = new() { Name = "RegionHeader", Kind = BandKind.GroupHeader, Height = 34, Items = [Text("RegionName", "[Sales.Region]", 0, 7, 523, 24, 15, true, color: "#244F75") with { Bookmark = "[Sales.Region]" }] }, Footer = new() { Name = "RegionFooter", Kind = BandKind.GroupFooter, Height = 36, Items = [Text("SubtotalLabel", "[Sales.Region] subtotal", 115, 4, 260, 24, 11, true), Text("Subtotal", "[Sum(Sales.Amount)]", 400, 4, 123, 24, 11, true, TextAlign.Right, "C2")] } }],
                Items = [Text("Order", "[Sales.Order]", 0, 3, 100, 20), Text("Product", "[Sales.Product]", 115, 3, 260, 20), Text("Amount", "[Sales.Amount]", 400, 3, 123, 20, align: TextAlign.Right, format: "C2")]
            }, new() { Name = "GrandSummary", Kind = BandKind.ReportSummary, Height = 40, Items = [Text("GrandTotal", "Grand total: [Sum(Sales.Amount)]", 200, 8, 323, 30, 15, true, TextAlign.Right, "C2")] }, Footer()
        ] }] };
    }
    public static ReportDocument MasterDetail()
    {
        var customers = new DataTable { Name = "Customers", Columns = ["Id", "Name"], Rows = [new() { ["Id"] = 1, ["Name"] = "Northwind Trading" }, new() { ["Id"] = 2, ["Name"] = "Alpine Ski House" }, new() { ["Id"] = 3, ["Name"] = "Adventure Works" }] };
        var orders = new DataTable { Name = "Orders", Columns = ["CustomerId", "Order", "Amount"] };
        for (int n = 0; n < 18; n++) orders.Rows.Add(new() { ["CustomerId"] = n % 3 + 1, ["Order"] = "ORD-" + (n + 1).ToString("D3"), ["Amount"] = 100m + n * 25 });
        return new() { Name = "Customer orders", Data = [customers, orders], Relations = [new("Customers", "Id", "Orders", "CustomerId")], Pages = [new() { Bands = [
            new() { Name = "Title", Kind = BandKind.ReportTitle, Height = 64, Items = [Text("Title", "CUSTOMER ORDERS", 0, 0, 523, 40, 24, true)] },
            new() { Name = "Customers", Kind = BandKind.Data, DataSource = "Customers", Height = 42, KeepWithNext = true, Items = [Text("Customer", "[Customers.Name]", 0, 8, 523, 30, 16, true, color: "#244F75")], Children = [new() { Name = "Orders", Kind = BandKind.Data, DataSource = "Orders", Height = 25, CanSplit = false, AlternateFill = "#F2F5F8", Items = [Text("Order", "[Orders.Order]", 18, 2, 150, 21), Text("Amount", "[Orders.Amount]", 350, 2, 170, 21, align: TextAlign.Right, format: "C2")], Footer = new() { Kind = BandKind.DataFooter, Name = "OrderTotal", Height = 34, Items = [Text("Total", "Customer total: [Sum(Orders.Amount)]", 250, 2, 270, 25, 11, true, TextAlign.Right, "C2")] } }] }, Footer()
        ] }] };
    }
    public static ReportDocument Labels()
    {
        var data = new DataTable { Name = "Labels", Columns = ["Name", "Address", "Code"] };
        for (int n = 1; n <= 36; n++) data.Rows.Add(new() { ["Name"] = "Customer " + n.ToString("D2"), ["Address"] = n + " Market Street", ["Code"] = "RS-" + n.ToString("D5") });
        return new() { Name = "Three-column mailing labels", Data = [data], Pages = [new() { Margins = new(22, 22, 22, 22), Columns = 3, ColumnGap = 10, Bands = [new() { Name = "Labels", Kind = BandKind.Data, DataSource = "Labels", Height = 96, CanSplit = false, Items = [Text("Name", "[Labels.Name]", 4, 2, 160, 23, 12, true), Text("Address", "[Labels.Address]", 4, 27, 160, 20, 9), new BarcodeItem { Name = "Barcode", Text = "[Labels.Code]", Bounds = new(4, 52, 158, 34), ShowText = true }] }] }] };
    }
    public static ReportDocument TablesAndCharts()
    {
        var data = new DataTable { Name = "Revenue", Columns = ["Month", "Revenue", "Target"] };
        string[] months = ["January", "February", "March", "April", "May", "June"];
        for (int n = 0; n < months.Length; n++) data.Rows.Add(new() { ["Month"] = months[n], ["Revenue"] = 21000m + n * 3600 + (n % 2) * 2800, ["Target"] = 24000m + n * 2500 });
        return new() { Name = "Revenue overview", Data = [data], Pages = [new() { Bands = [
            new() { Name = "Title", Kind = BandKind.ReportTitle, Height = 64, Items = [Text("Title", "REVENUE OVERVIEW", 0, 0, 523, 40, 24, true, color: "#244F75")] },
            new() { Name = "Dashboard", Kind = BandKind.Child, Height = 560, CanSplit = true, Items = [
                new ChartItem { Name = "RevenueChart", Title = "Monthly revenue", DataSource = "Revenue", CategoryColumn = "Month", ValueExpression = "Revenue", Bounds = new(0, 0, 523, 240), Chart = ChartKind.Bar },
                new TableItem { Name = "RevenueTable", DataSource = "Revenue", Bounds = new(0, 265, 523, 180), RowHeight = 29, Columns = [new("Month", "Month", 220), new("Revenue", "Revenue", 150, "C2"), new("Target", "Target", 150, "C2")], Style = new() { FontSize = 11, Padding = 5 } },
                Text("Summary", "Total revenue: [Sum(Revenue.Revenue)]", 230, 500, 293, 35, 16, true, TextAlign.Right, "C2")
            ] }, Footer()
        ] }] };
    }
}
