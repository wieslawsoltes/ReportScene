using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ReportScene.Controls.Uno;
using ReportScene.Model;

namespace ReportScene.Designer.Uno;

public sealed partial class ReportDesignerControl
{
    private void InsertSubreport()
    {
        if (previewMode) { SetPreview(false); BuildRibbon(); }
        var band = Session.SelectedBand ?? throw new InvalidOperationException("Select a band before inserting a subreport.");
        double width = Math.Min(240, Session.Page.ColumnWidth - 8);
        if (width < 24) throw new InvalidOperationException("The printable column is too narrow for a subreport.");
        bool onParent = band.Kind is BandKind.PageHeader or BandKind.PageFooter or BandKind.ColumnHeader or BandKind.ColumnFooter or BandKind.Overlay;
        double y = Math.Max(band.Height, band.Items.Select(i => i.Bounds.Bottom).DefaultIfEmpty(0).Max());
        string name = Session.UniquePageName("Subreport");
        var target = new ReportPage
        {
            Name = name, Width = width + 72, Bands = [new()
            {
                Name = name + "Title", Kind = BandKind.ReportTitle, Height = 40,
                Items = [new TextItem { Name = name + "Text", Text = "Subreport content", Bounds = new(0, 0, width, 30), Style = new() { FontSize = 14, Bold = true } }]
            }]
        };
        Session.AddSubreport(target, new(8, y, width, 24), onParent);
        DesignView.ScrollToSelection(); DesignView.Focus(FocusState.Programmatic);
    }
    private Task EditSelectedSubreportAsync()
        => Session.Selection.OfType<SubreportItem>().FirstOrDefault() is { } item ? EditSubreportAsync(item) : Task.CompletedTask;
    private void OpenSelectedSubreport()
    {
        if (Session.Selection.OfType<SubreportItem>().FirstOrDefault() is not { } item) return;
        Session.OpenSubreportPage(item.Id); SetPreview(false); BuildRibbon(); RefreshStructure(true);
    }
    private async Task EditSubreportAsync(SubreportItem item)
    {
        if (item.Locked) throw new InvalidOperationException("The selected subreport is locked.");
        Session.Select(item.Id); var target = Operations.CaptureTarget();
        var editor = new SubreportDefinitionEditor(item, Session.Document.Pages, Session.Page.Name);
        var error = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, BorderThickness = new(0), Foreground = ControlTheme.Brush("#AD362C"), Visibility = Visibility.Collapsed };
        var panel = new StackPanel { Spacing = 8, Width = Math.Clamp(ActualWidth - 140, 300, 760) };
        panel.Children.Add(editor); panel.Children.Add(error);
        var dialog = new ContentDialog
        {
            Title = "Subreport — " + item.Name, XamlRoot = XamlRoot, PrimaryButtonText = "Apply", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary,
            Content = new ScrollViewer { Content = panel, MaxHeight = Math.Clamp(ActualHeight - 240, 190, 610), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto }
        };
        dialog.Resources["ContentDialogMaxWidth"] = 900d;
        var registered = new HashSet<string>(StringComparer.Ordinal);
        void Register()
        {
            foreach (var key in registered) commands.Remove(key); registered.Clear();
            void Add(string key, FrameworkElement element) { commands[key] = element; registered.Add(key); }
            foreach (var control in editor.Editors) Add(control.Key, control.Value);
            Add("subreport-dialog-error", error);
            var pending = new Stack<DependencyObject>(); pending.Push(dialog);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (node is Button button)
                {
                    if (button.Name == "PrimaryButton") Add("subreport-dialog-apply", button);
                    if (button.Name == "CloseButton") Add("subreport-dialog-cancel", button);
                }
                for (int n = 0; n < VisualTreeHelper.GetChildrenCount(node); n++) pending.Push(VisualTreeHelper.GetChild(node, n));
            }
            NotifyState();
        }
        dialog.Opened += (_, _) => Register(); editor.EditorsChanged += Register;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                Operations.EnsureCurrent(target);
                var changed = editor.GetDefinition();
                Session.UpdateSelection("Edit subreport", source => source.Id == item.Id ? changed : source);
            }
            catch (Exception ex) { error.Text = ex.Message; error.Visibility = Visibility.Visible; args.Cancel = true; }
        };
        Register();
        try { await dialog.ShowAsync(); }
        finally { editor.EditorsChanged -= Register; foreach (var key in registered) commands.Remove(key); if (!disposed) NotifyState(); }
    }
    private void RefreshSubreportProperties(SubreportItem item)
    {
        properties.Category("Subreport");
        void Edit(string description, Func<SubreportItem, SubreportItem> transform)
            => Session.UpdateSelection(description, i => i is SubreportItem subreport ? transform(subreport) : i);
        properties.Choice("Target page", Session.Document.Pages.Where(p => p != Session.Page).Select(p => p.Name), item.PageName,
            name => Edit("Subreport target", s => s with { PageName = name }), "subreport-target");
        properties.Boolean("Print on parent", item.PrintOnParent, value => Edit("Subreport mode", s => s with { PrintOnParent = value }), "subreport-parent");
        properties.Boolean("Shift following", item.ShiftFollowing, value => Edit("Subreport shifting", s => s with { ShiftFollowing = value }), "subreport-shift");
        properties.ReadOnly("Parameter bindings", item.ParameterBindings.Count + " · double-click to edit");
    }
}
