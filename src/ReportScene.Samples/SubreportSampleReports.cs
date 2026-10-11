using ReportScene.Model;

namespace ReportScene.Samples;

/// <summary>Executable examples of independent parallel flows and caller-scoped child reports.</summary>
public static class SubreportSampleReports
{
    private static TextItem Text(string name, string text, RectD bounds, double size = 10, bool bold = false, string? format = null) => new()
    { Name = name, Text = text, Bounds = bounds, Format = format, Style = new() { FontSize = size, Bold = bold, Padding = 3, Foreground = "#263B50" } };
    private static Band Header(string title) => new()
    {
        Name = "ReportHeader", Kind = BandKind.PageHeader, Height = 66, Items =
        [Text("Title", title, new(0, 0, 720, 36), 24, true), Text("Subtitle", "Independent subreports · shared parent pages · native .NET reporting", new(0, 40, 720, 22), 10)]
    };
    private static Band Footer() => new()
    {
        Name = "ReportFooter", Kind = BandKind.PageFooter, Height = 24,
        Items = [Text("Footer", "ReportScene · Page [PageN] of [TotalPages]", new(0, 1, 720, 22), 8)]
    };
    public static ReportDocument Dashboard()
    {
        var orders = new DataTable { Name = "Orders", Columns = ["Number", "Customer", "Amount"] };
        var tasks = new DataTable { Name = "Tasks", Columns = ["Title", "Owner", "Status"] };
        for (int n = 1; n <= 28; n++) orders.Rows.Add(new() { ["Number"] = "SO-" + n.ToString("D3"), ["Customer"] = new[] { "Northwind", "Alpine", "Contoso" }[n % 3], ["Amount"] = 175m + n * 37.5m });
        for (int n = 1; n <= 17; n++) tasks.Rows.Add(new() { ["Title"] = "Delivery review " + n, ["Owner"] = new[] { "Alex", "Morgan", "Sam" }[n % 3], ["Status"] = n % 3 == 0 ? "Pending" : "Ready" });
        return new()
        {
            Name = "Subreport dashboard", Data = [orders, tasks], Pages =
            [new()
            {
                Name = "Dashboard", Width = 841.89, Height = 595.276, Margins = new(36, 28, 36, 28), Bands =
                [Header("OPERATIONS OVERVIEW"), new() { Name = "ParallelReports", Kind = BandKind.ReportTitle, Height = 26, Items =
                    [new SubreportItem { Name = "OrdersSubreport", PageName = "OrdersDetail", Bounds = new(0, 0, 372, 26) },
                     new SubreportItem { Name = "TasksSubreport", PageName = "TasksDetail", Bounds = new(396, 0, 372, 26) }] },
                 new() { Name = "Closing", Kind = BandKind.ReportSummary, Height = 42, Items = [Text("Closing", "End of operations overview — both subreports complete.", new(0, 10, 720, 24), 11, true)] }, Footer()]
            }, new()
            {
                Name = "OrdersDetail", Width = 444, Bands =
                [new() { Name = "Orders", Kind = BandKind.Data, DataSource = "Orders", Height = 28, CanSplit = false, AlternateFill = "#F0F5FA",
                    Header = new() { Name = "OrdersHeading", Kind = BandKind.DataHeader, Height = 35, Items = [Text("OrdersHeading", "SALES ORDERS", new(0, 0, 365, 32), 15, true)] },
                    Items = [Text("OrderNumber", "[Orders.Number]", new(0, 2, 85, 22)), Text("Customer", "[Orders.Customer]", new(90, 2, 145, 22)), Text("Amount", "[Orders.Amount]", new(245, 2, 120, 22), format: "N2")],
                    Footer = new() { Name = "OrderTotal", Kind = BandKind.DataFooter, Height = 36, Items = [Text("OrderTotal", "Order total: [Sum(Orders.Amount)]", new(0, 6, 365, 25), 12, true, "N2")] } }]
            }, new()
            {
                Name = "TasksDetail", Width = 444, Bands =
                [new() { Name = "Tasks", Kind = BandKind.Data, DataSource = "Tasks", Height = 42, CanSplit = false, AlternateFill = "#F4F6F8",
                    Header = new() { Name = "TasksHeading", Kind = BandKind.DataHeader, Height = 35, Items = [Text("TasksHeading", "DELIVERY CHECKLIST", new(0, 0, 365, 32), 15, true)] },
                    Items = [Text("TaskTitle", "[Tasks.Title]", new(0, 0, 220, 22), 10, true), Text("TaskOwner", "[Tasks.Owner] · [Tasks.Status]", new(0, 20, 300, 20), 9)] }]
            }]
        };
    }
    public static ReportDocument MasterDetail()
    {
        var accounts = new DataTable { Name = "Accounts", Columns = ["Id", "Name"], Rows = [new() { ["Id"] = 1, ["Name"] = "Northwind" }, new() { ["Id"] = 2, ["Name"] = "Contoso" }, new() { ["Id"] = 3, ["Name"] = "Alpine" }] };
        var orders = new DataTable { Name = "Orders", Columns = ["AccountId", "Reference", "Amount"] };
        for (int n = 1; n <= 24; n++) orders.Rows.Add(new() { ["AccountId"] = n % 3 + 1, ["Reference"] = "ORD-" + n.ToString("D3"), ["Amount"] = 40m + n * 12.5m });
        return new()
        {
            Name = "Subreport master-detail", Data = [accounts, orders], Relations = [new("Accounts", "Id", "Orders", "AccountId")],
            Parameters = [new("CurrencyLabel", "USD")], Pages =
            [new()
            {
                Name = "AccountsReport", Bands =
                [new() { Name = "Header", Kind = BandKind.PageHeader, Height = 54, Items = [Text("Title", "ACCOUNT STATEMENTS", new(0, 0, 520, 40), 23, true)] },
                 new() { Name = "Account", Kind = BandKind.Data, DataSource = "Accounts", Height = 90, CanSplit = true,
                    Items = [Text("AccountName", "[Accounts.Name]", new(0, 0, 515, 30), 17, true),
                        new SubreportItem { Name = "Statement", PageName = "OrderStatement", PrintOnParent = true, Bounds = new(0, 34, 515, 24), ParameterBindings = [new("Heading", "Accounts.Name + ' orders'")] },
                        Text("AccountEnd", "End of [Accounts.Name] statement", new(0, 65, 515, 22), 9)] },
                 new() { Name = "Footer", Kind = BandKind.PageFooter, Height = 24, Items = [Text("PageNumber", "Page [PageN] of [TotalPages]", new(0, 0, 515, 22), 8)] }]
            }, new()
            {
                Name = "OrderStatement", Bands =
                [new() { Name = "OrderRows", Kind = BandKind.Data, DataSource = "Orders", Height = 26, CanSplit = false,
                    Header = new() { Name = "Heading", Kind = BandKind.DataHeader, Height = 24, Items = [Text("OrderHeading", "[Parameters.Heading]", new(0, 0, 510, 22), 10, true)] },
                    Items = [Text("Reference", "[Orders.Reference]", new(8, 0, 180, 23)), Text("Charge", "[Orders.Amount] [Parameters.CurrencyLabel]", new(260, 0, 245, 23), format: "N2")],
                    Footer = new() { Name = "Subtotal", Kind = BandKind.DataFooter, Height = 30, Items = [Text("Subtotal", "Subtotal: [Sum(Orders.Amount)]", new(260, 1, 245, 27), 12, true, "N2")] } }]
            }]
        };
    }
}
