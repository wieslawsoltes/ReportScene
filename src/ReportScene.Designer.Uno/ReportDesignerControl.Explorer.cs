using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReportScene.Controls.Uno;
using ReportScene.Model;

namespace ReportScene.Designer.Uno;

public sealed partial class ReportDesignerControl
{
    private DesignerOutline reportOutline = new([]), dataOutline = new([]);
    private string[] pageNames = [];
    private readonly Dictionary<string, int> objectIndices = new(StringComparer.Ordinal);
    private bool selectingObject;
    private readonly ComboBox objectSelector = new() { FontSize = 11, MinHeight = 0, Height = 24, Padding = new(5, 0, 22, 0), CornerRadius = new(0), HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Select an object" };
    private void RefreshStructure(bool force = false)
    {
        var report = DesignerOutline.Report(Session.Document); var data = DesignerOutline.Data(Session.Document);
        bool reportChanged = !reportOutline.HasSameEntries(report);
        if (reportChanged) reportOutline = report;
        if (!dataOutline.HasSameEntries(data)) dataOutline = data;
        if (reportChanged || force && objectSelector.Items.Count == 0)
        {
            selectingObject = true;
            try
            {
                objectIndices.Clear();
                var labels = new string[reportOutline.Entries.Count];
                for (int n = 0; n < labels.Length; n++)
                {
                    var entry = reportOutline.Entries[n]; objectIndices[entry.Key] = n;
                    labels[n] = entry.Label + (entry.Detail is null ? "" : "  ·  " + entry.Detail.Replace("Item", ""));
                }
                objectSelector.ItemsSource = labels;
            }
            finally { selectingObject = false; }
        }
        var names = Session.Document.Pages.Select(p => p.Name).ToArray();
        if (!pageNames.SequenceEqual(names))
        {
            pageNames = names;
            foreach (var id in commands.Keys.Where(k => k.StartsWith("page-tab-", StringComparison.Ordinal)).ToArray()) commands.Remove(id);
            pageTabs.Children.Clear();
            for (int n = 0; n < names.Length; n++)
            {
                int index = n;
                var button = Command("page-tab-" + n, names[n], "report", () =>
                {
                    Session.SetPage(index); SetPreview(false); BuildRibbon(); SyncObjectSelection();
                });
                button.Height = 24; button.Padding = new(10, 1, 10, 1); pageTabs.Children.Add(button);
            }
            var add = Command("page-tab-add", "+", "", Session.AddPage); add.Height = 24; pageTabs.Children.Add(add);
        }
        RebuildTree(); SyncObjectSelection();
    }
    private void RebuildTree()
    {
        tree.SetOutline(activeTree == "Data" ? dataOutline : reportOutline);
        UpdateTabSelection(); SyncObjectSelection();
    }
    private void SyncObjectSelection()
    {
        if (Session.HasActiveDrag) return;
        string? key = Session.SelectedIds.Count == 1 ? "item:" + Session.SelectedIds.First() : Session.SelectedIds.Count > 1 ? null : Session.BandId is { } band ? "band:" + band : "page:" + Session.PageIndex;
        selectingObject = true;
        try { objectSelector.SelectedIndex = key is not null && objectIndices.TryGetValue(key, out int index) ? index : -1; }
        finally { selectingObject = false; }
        objectSelector.PlaceholderText = Session.SelectedIds.Count > 1 ? Session.SelectedIds.Count + " selected objects" : "Select an object";
        if (activeTree == "Report") tree.SelectKey(key);
        for (int n = 0; n < pageNames.Length; n++) if (commands.TryGetValue("page-tab-" + n, out var element) && element is Button button)
        { button.Background = ControlTheme.Brush(n == Session.PageIndex ? "#FFFFFF" : "#E9E9E9"); button.BorderThickness = new(0, n == Session.PageIndex ? 2 : 0, 0, 0); button.BorderBrush = ControlTheme.Brush("#2777AD"); }
    }
    private void InvokeOutline(DesignerOutlineEntry entry)
    {
        if (entry.PageIndex >= 0)
        {
            if (Session.PageIndex != entry.PageIndex) Session.SetPage(entry.PageIndex);
            if (previewMode) { SetPreview(false); BuildRibbon(); }
            if (entry.ObjectId is { } item) { Session.Select(item); DesignView.ScrollToSelection(); }
            else if (entry.BandId is { } band) Session.SelectBand(band);
            else Session.SetPage(entry.PageIndex);
            return;
        }
        if (entry.Expression is { } expression)
        {
            if (previewMode) { SetPreview(false); BuildRibbon(); }
            Session.Add(new TextItem { Name = entry.Label.Replace(" ", ""), Text = expression, Bounds = new(8, 6, 150, 22) }); DesignView.ScrollToSelection();
        }
        else if (entry.Kind == OutlineKind.Table) Run(EditDataAsync);
        else if (entry.Key == "parameters") Run(ParametersAsync);
    }
    private void UpdateTabSelection()
    {
        foreach (var name in new[] { "Home", "Report", "Layout", "View", "Preview" })
            if (commands.TryGetValue("tab-" + name.ToLowerInvariant(), out var element) && element is Button button)
            {
                bool active = previewMode ? name == "Preview" : name == activeRibbon;
                button.Background = ControlTheme.Brush(active ? "#FAFAFA" : "#F0F0F0");
                button.BorderBrush = ControlTheme.Brush(active ? "#C8C8C8" : "#00FFFFFF"); button.BorderThickness = new(active ? 1 : 0, active ? 1 : 0, active ? 1 : 0, 0);
            }
        foreach (var name in new[] { "Data", "Report" })
            if (commands.TryGetValue(name == "Data" ? "tree-data" : "tree-report", out var element) && element is Button button)
            { button.Background = ControlTheme.Brush(activeTree == name ? "#FFFFFF" : "#E9E9E9"); button.BorderThickness = new(0, activeTree == name ? 2 : 0, 0, 0); button.BorderBrush = ControlTheme.Brush("#2777AD"); }
    }
}
