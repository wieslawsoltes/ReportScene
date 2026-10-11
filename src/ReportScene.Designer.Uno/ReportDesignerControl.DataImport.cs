using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ReportScene.Controls.Uno;
using ReportScene.Data;
using ReportScene.Model;

namespace ReportScene.Designer.Uno;

public sealed partial class ReportDesignerControl
{
    private async Task ImportDataWithXmlAsync()
    {
        var file = await host.OpenFileAsync([".csv", ".json", ".xml"]); if (file is null) return;
        bool xml = file.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
        var name = new TextBox { Header = "Data table name", Text = Path.GetFileNameWithoutExtension(file.Name).Replace(' ', '_'), Width = 270 };
        var infer = new CheckBox { Content = "Infer numbers in untyped fields (may convert codes such as 001)", IsChecked = file.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) };
        var row = new TextBox { Header = "XML row element (blank: infer from first row)", Width = 310, Visibility = xml ? Visibility.Visible : Visibility.Collapsed };
        var error = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, BorderThickness = new(0), Foreground = ControlTheme.Brush("#AD362C"), Visibility = Visibility.Collapsed };
        var preview = new DataTablePreview();
        var panel = new StackPanel { Spacing = 8, Width = Math.Clamp(ActualWidth - 160, 300, 760) };
        panel.Children.Add(ControlTheme.Label(file.Name, 12, true)); panel.Children.Add(name); panel.Children.Add(infer); panel.Children.Add(row);
        if (xml) panel.Children.Add(new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Text = "XML: one root with repeated scalar rows. Child elements and Attribute_ prefixed attributes become columns. xsi:type and xsi:nil are supported. DTDs, nested objects and ambiguous column names are rejected." });
        DataTable Read() => DataFileReader.Read(file.Name, file.Content, RequireName(name.Text), infer.IsChecked == true,
            xmlOptions: new(RowElement: string.IsNullOrWhiteSpace(row.Text) ? null : row.Text.Trim()));
        var showPreview = ControlTheme.Button("data-import-preview", "Preview data", "table", () =>
        {
            try { preview.SetTable(Read()); error.Text = ""; error.Visibility = Visibility.Collapsed; }
            catch (Exception e) { error.Text = e.Message; error.Visibility = Visibility.Visible; }
            NotifyState();
        });
        panel.Children.Add(showPreview); panel.Children.Add(preview); panel.Children.Add(error);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Import report data", PrimaryButtonText = "Import", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary,
            Content = new ScrollViewer { Content = panel, MaxHeight = Math.Clamp(ActualHeight - 220, 200, 650), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto } };
        dialog.Resources["ContentDialogMaxWidth"] = 900d;
        var ids = new List<string>();
        void Register(string id, FrameworkElement element) { commands[id] = element; ids.Add(id); ControlTheme.Identify(element, id, id); }
        Register("data-import-name", name); Register("data-import-infer", infer); Register("data-import-row", row); Register("data-import-preview", showPreview); Register("data-import-error", error);
        dialog.Opened += (_, _) =>
        {
            var pending = new Stack<DependencyObject>(); pending.Push(dialog);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (node is Button button)
                {
                    if (button.Name == "PrimaryButton") Register("data-import-apply", button);
                    if (button.Name == "CloseButton") Register("data-import-cancel", button);
                }
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) pending.Push(VisualTreeHelper.GetChild(node, i));
            }
            NotifyState();
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                var table = Read(); Session.ReplaceData(table);
                SetStatus($"Imported {table.Rows.Count:N0} rows and {table.Columns.Count:N0} columns into {table.Name}");
            }
            catch (Exception e) { error.Text = e.Message; error.Visibility = Visibility.Visible; args.Cancel = true; }
        };
        try { await dialog.ShowAsync(); }
        finally { foreach (string id in ids) commands.Remove(id); NotifyState(); }
    }
}
