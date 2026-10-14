using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ReportScene.Controls.Uno;
using ReportScene.Designer;
using ReportScene.Model;
using ReportScene.Rendering.Skia;

namespace ReportScene.Designer.Uno;

public sealed partial class ReportDesignerControl
{
    private enum ColorPart { Fill, Text, Border }
    private bool colorDialogOpen;
    private static string ColorText(ReportItem item, ColorPart part) => part switch
    { ColorPart.Fill => item.Style.Fill, ColorPart.Text => item.Style.Foreground, _ => item.Style.BorderColor };
    private static DesignerColorValue EditorColor(string text)
    {
        if (DesignerColorValue.TryParse(text, out var value)) return value;
        // Imported aliases accepted by the existing renderer remain readable. Merely
        // opening/applying an untouched editor must not rewrite their stored spelling.
        var color = ReportRenderer.Color(text); return new(color.Alpha, color.Red, color.Green, color.Blue);
    }
    private async Task NativeColorAsync(ColorPart part)
    {
        if (disposed || colorDialogOpen) return;
        var selection = Session.Selection.Where(i => !i.Locked).ToArray();
        if (selection.Length == 0) return;
        var target = Operations.CaptureTarget();
        var original = EditorColor(ColorText(selection[0], part));
        bool mixed = selection.Any(i => EditorColor(ColorText(i, part)) != original);
        string caption = part switch { ColorPart.Fill => "Fill color", ColorPart.Text => "Text color", _ => "Border color" };
        var editor = new ReportColorEditor(original) { Width = Math.Clamp(ActualWidth - 140, 280, 580) };
        var registered = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal);
        void Register(string key, FrameworkElement control) { commands[key] = control; registered[key] = control; }
        void Publish()
        {
            if (disposed || !colorDialogOpen) return;
            foreach (var pair in editor.Elements()) Register(pair.Key, pair.Value);
            NotifyState();
        }
        colorDialogOpen = true;
        try
        {
            var error = new TextBlock { FontSize = 12, Foreground = ControlTheme.Brush("#A3372E"), TextWrapping = TextWrapping.Wrap };
            Register("color-dialog-error", error);
            var explanation = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = ControlTheme.Brush("#626262"),
                Text = mixed ? "Mixed colors. The original preview shows the first editable object. Choose a color to apply it to all unlocked selected objects."
                    : $"{selection.Length} editable object(s). Changes are applied together only when you choose Apply." };
            Register("color-selection-summary", explanation);
            var panel = new StackPanel { Spacing = 8, Width = editor.Width }; panel.Children.Add(explanation); panel.Children.Add(editor); panel.Children.Add(error);
            var viewport = new ScrollViewer { Content = panel, MaxHeight = Math.Clamp(ActualHeight - 220, 190, 650),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Register("color-dialog-scroll", viewport);
            var dialog = new ContentDialog { Title = caption, Content = viewport, XamlRoot = XamlRoot,
                PrimaryButtonText = "Apply", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
            dialog.Resources["ContentDialogMaxWidth"] = 680d;
            editor.StateChanged += Publish;
            dialog.Opened += (_, _) =>
            {
                var pending = new Stack<DependencyObject>(); pending.Push(dialog);
                while (pending.TryPop(out var node))
                {
                    if (node is Button button)
                    {
                        if (button.Name == "PrimaryButton") Register("color-dialog-apply", button);
                        if (button.Name == "CloseButton") Register("color-dialog-cancel", button);
                    }
                    for (int n = 0; n < VisualTreeHelper.GetChildrenCount(node); n++) pending.Push(VisualTreeHelper.GetChild(node, n));
                }
                Publish();
            };
            dialog.PrimaryButtonClick += (_, e) =>
            {
                try
                {
                    Operations.EnsureCurrent(target);
                    var next = editor.ReadValue();
                    if (!editor.HasUserChanges) return;
                    string text = next.ToHexString();
                    Session.UpdateSelection(caption, item => EditorColor(ColorText(item, part)) == next ? item : item with
                    {
                        Style = part switch
                        {
                            ColorPart.Fill => item.Style with { Fill = text },
                            ColorPart.Text => item.Style with { Foreground = text },
                            _ => item.Style with { BorderColor = text }
                        }
                    });
                }
                catch (Exception problem) { error.Text = problem.Message; e.Cancel = true; Publish(); }
            };
            await dialog.ShowAsync();
        }
        finally
        {
            editor.StateChanged -= Publish;
            foreach (var pair in registered)
                if (commands.TryGetValue(pair.Key, out var control) && ReferenceEquals(control, pair.Value)) commands.Remove(pair.Key);
            colorDialogOpen = false;
            if (!disposed) NotifyState();
        }
    }
}
