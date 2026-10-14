using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using ReportScene.Designer;
using Windows.System;
using Windows.UI;

namespace ReportScene.Controls.Uno;

/// <summary>Native palette/spectrum and byte-exact RGBA drafts. Only a host's explicit ReadValue/commit changes a report.</summary>
public sealed class ReportColorEditor : UserControl
{
    private static readonly string[] Palette = [
        "#000000", "#404040", "#808080", "#C0C0C0", "#E8EEF4", "#FFFFFF", "#244F75", "#397BB5",
        "#800000", "#C43E3E", "#FF0000", "#FF8080", "#804000", "#D5A23A", "#FF8000", "#FFC080",
        "#808000", "#C0C000", "#FFFF00", "#FFFF80", "#004000", "#2F7D55", "#00A000", "#80FF80",
        "#004040", "#008080", "#00FFFF", "#80FFFF", "#000080", "#0000FF", "#8080FF", "#C0C0FF",
        "#400080", "#8000FF", "#C080FF", "#E0C0FF", "#800080", "#FF00FF", "#FF80FF", "#FFC0FF"
    ];
    private static readonly DesignerColorValue[] PaletteValues = Palette.Select(DesignerColorValue.Parse).ToArray();
    private readonly SolidColorBrush selectedBorder = ControlTheme.Brush("#005A9E"), normalBorder = ControlTheme.Brush("#909090");
    private readonly Dictionary<string, FrameworkElement> elements = new(StringComparer.Ordinal);
    private readonly List<Button> swatches = [];
    private readonly ColorPicker picker = new()
    {
        Width = 280, IsAlphaEnabled = true, IsAlphaSliderVisible = true, IsAlphaTextInputVisible = false,
        IsColorPreviewVisible = false, IsColorChannelTextInputVisible = false, IsHexInputVisible = false,
        IsColorSliderVisible = true, IsColorSpectrumVisible = true, IsMoreButtonVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center
    };
    private readonly TextBox hex = new() { Header = "#RRGGBB / #AARRGGBB", FontSize = 11, Height = 50, MinHeight = 0, MaxLength = 32, Padding = new(5, 2, 5, 2) };
    private readonly TextBox red, green, blue, alpha;
    private readonly TextBlock error = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, MinHeight = 30, Foreground = ControlTheme.Brush("#A3372E") };
    private readonly TextBlock valueLabel = ControlTheme.Label("", 11);
    private readonly SolidColorBrush currentBrush = new();
    private readonly Grid layout = new();
    private readonly StackPanel details = new() { Spacing = 8, Width = 260, HorizontalAlignment = HorizontalAlignment.Center };
    private FrameworkElement? spectrum;
    private DesignerColorValue value;
    private int synchronizing, selectedSwatch = -1;
    private string observedHex = "", observedRed = "", observedGreen = "", observedBlue = "", observedAlpha = "";
    public DesignerColorValue OriginalValue { get; }
    public DesignerColorValue Value => value;
    public bool HasUserChanges { get; private set; }
    public bool IsValid => TryRead(out _, out _);
    public string ValidationMessage => error.Text;
    public long ValueChangeCount { get; private set; }
    public long PaletteUpdateCount { get; private set; }
    public event Action? StateChanged;

    public ReportColorEditor(DesignerColorValue initial)
    {
        OriginalValue = value = initial; Width = 580; MinWidth = 280;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        layout.ColumnDefinitions.Add(new() { Width = new(280) }); layout.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.Children.Add(picker); Grid.SetColumn(details, 1); layout.Children.Add(details); Content = layout;
        elements["color-picker"] = ControlTheme.Identify(picker, "color-picker", "Color spectrum, brightness and opacity");
        details.Children.Add(ControlTheme.Label("Standard colors", 11, true));
        var palette = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        for (int n = 0; n < 8; n++) palette.ColumnDefinitions.Add(new() { Width = new(32) });
        for (int n = 0; n < 5; n++) palette.RowDefinitions.Add(new() { Height = new(29) });
        for (int n = 0; n < Palette.Length; n++)
        {
            int index = n; string text = Palette[n];
            var button = new Button { Width = 27, Height = 25, MinWidth = 0, MinHeight = 0, Padding = new(0), CornerRadius = new(0),
                Background = ControlTheme.Brush(text), BorderBrush = normalBorder, BorderThickness = new(1), HorizontalAlignment = HorizontalAlignment.Left };
            button.Click += (_, _) => UserValue(PaletteValues[index]);
            button.KeyDown += (_, e) =>
            {
                int next = e.Key switch { VirtualKey.Left => index - 1, VirtualKey.Right => index + 1, VirtualKey.Up => index - 8,
                    VirtualKey.Down => index + 8, VirtualKey.Home => 0, VirtualKey.End => swatches.Count - 1, _ => -1000 };
                if (next == -1000) return;
                swatches[Math.Clamp(next, 0, swatches.Count - 1)].Focus(FocusState.Keyboard); e.Handled = true;
            };
            swatches.Add(button); elements["color-palette-" + n] = ControlTheme.Identify(button, "color-palette-" + n, "Color " + text);
            ToolTipService.SetToolTip(button, text); Grid.SetColumn(button, n % 8); Grid.SetRow(button, n / 8); palette.Children.Add(button);
        }
        details.Children.Add(palette);
        var transparent = ControlTheme.Button("color-transparent", "No color (transparent)", "", () => UserValue(new(0, 0, 0, 0)));
        transparent.HorizontalAlignment = HorizontalAlignment.Stretch; elements["color-transparent"] = transparent; details.Children.Add(transparent);
        var previews = new Grid(); previews.ColumnDefinitions.Add(new()); previews.ColumnDefinitions.Add(new());
        var original = Preview("Original", new SolidColorBrush(Native(initial)), "color-original-preview");
        var current = Preview("New", currentBrush, "color-current-preview");
        previews.Children.Add(original); Grid.SetColumn(current, 1); previews.Children.Add(current); details.Children.Add(previews);
        elements["color-hex"] = ControlTheme.Identify(hex, "color-hex", "Hexadecimal report color, alpha first when present"); details.Children.Add(hex);
        var channels = new Grid(); for (int n = 0; n < 4; n++) channels.ColumnDefinitions.Add(new());
        TextBox Channel(string name, int column)
        {
            var input = new TextBox { Header = name, FontSize = 11, Height = 48, MinHeight = 0, MinWidth = 0, MaxLength = 5, Padding = new(4, 1, 3, 1), Margin = new(0, 0, 3, 0) };
            string id = "color-" + name.ToLowerInvariant(); elements[id] = ControlTheme.Identify(input, id, name + " from 0 to 255");
            Grid.SetColumn(input, column); channels.Children.Add(input); return input;
        }
        red = Channel("Red", 0); green = Channel("Green", 1); blue = Channel("Blue", 2); alpha = Channel("Alpha", 3);
        details.Children.Add(channels); elements["color-value"] = ControlTheme.Identify(valueLabel, "color-value", "Current valid color"); details.Children.Add(valueLabel);
        elements["color-validation"] = ControlTheme.Identify(error, "color-validation", "Color input validation"); details.Children.Add(error);
        var reset = ControlTheme.Button("color-restore", "Restore original", "undo", () => UserValue(OriginalValue)); elements["color-restore"] = reset; details.Children.Add(reset);
        foreach (var input in new[] { hex, red, green, blue, alpha })
        {
            input.TextChanged += (_, _) => Edited(input);
            input.KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) { SetValue(value); e.Handled = true; } };
        }
        picker.ColorChanged += (_, e) =>
        {
            var next = FromNative(e.NewColor);
            if (synchronizing == 0 && next == FromNative(picker.Color) && next != value) UserValue(next);
        };
        picker.PreviousColor = Native(initial);
        picker.Loaded += (_, _) => StateChanged?.Invoke(); picker.SizeChanged += (_, _) => StateChanged?.Invoke();
        SizeChanged += (_, e) =>
        {
            bool wide = e.NewSize.Width >= 550;
            layout.ColumnDefinitions[0].Width = wide ? new(280) : new(1, GridUnitType.Star);
            layout.ColumnDefinitions[1].Width = wide ? new(1, GridUnitType.Star) : new(0);
            Grid.SetColumn(details, wide ? 1 : 0); Grid.SetRow(details, wide ? 0 : 1);
            details.Margin = wide ? new(12, 0, 0, 0) : new(0, 12, 0, 0);
        };
        SetValue(initial);
    }
    private void UserValue(DesignerColorValue next) { HasUserChanges = true; SetValue(next); }
    public void RestoreOriginal() => SetValue(OriginalValue);
    public void SetValue(DesignerColorValue next) => ApplyValue(next, false, false);
    private void ApplyValue(DesignerColorValue next, bool keepHex, bool keepChannels)
    {
        synchronizing++;
        try
        {
            if (value != next) ValueChangeCount++;
            value = next;
            if (!keepHex) hex.Text = next.ToHexString();
            if (!keepChannels)
            {
                red.Text = next.R.ToString(CultureInfo.InvariantCulture); green.Text = next.G.ToString(CultureInfo.InvariantCulture);
                blue.Text = next.B.ToString(CultureInfo.InvariantCulture); alpha.Text = next.A.ToString(CultureInfo.InvariantCulture);
            }
            if (FromNative(picker.Color) != next) picker.Color = Native(next);
            currentBrush.Color = Native(next); valueLabel.Text = next.ToHexString(); error.Text = "";
            int index = Array.IndexOf(PaletteValues, next);
            if (index != selectedSwatch)
            {
                if (selectedSwatch >= 0) { swatches[selectedSwatch].BorderThickness = new(1); swatches[selectedSwatch].BorderBrush = normalBorder; PaletteUpdateCount++; }
                if (index >= 0) { swatches[index].BorderThickness = new(3); swatches[index].BorderBrush = selectedBorder; PaletteUpdateCount++; }
                selectedSwatch = index;
            }
            RememberText();
        }
        finally { synchronizing--; }
        StateChanged?.Invoke();
    }
    private void RememberText()
    { observedHex = hex.Text; observedRed = red.Text; observedGreen = green.Text; observedBlue = blue.Text; observedAlpha = alpha.Text; }
    private void Edited(TextBox input)
    {
        // Check the event source, not the combined fields: a delayed hex event
        // must not interpret another field's newer draft as a fresh hex edit.
        string previous = ReferenceEquals(input, hex) ? observedHex : ReferenceEquals(input, red) ? observedRed
            : ReferenceEquals(input, green) ? observedGreen : ReferenceEquals(input, blue) ? observedBlue : observedAlpha;
        if (synchronizing != 0 || input.Text == previous) return;
        bool fromHex = ReferenceEquals(input, hex);
        RememberText();
        // Invalid text is only a draft, not an accepted color choice. Otherwise
        // Escape followed by Apply could flatten a mixed selection unintentionally.
        // Preserve the active draft and caret while typing eight-digit ARGB values.
        if (fromHex && DesignerColorValue.TryParse(hex.Text, out var parsed))
        { HasUserChanges = true; ApplyValue(parsed, true, false); return; }
        if (!fromHex && TryChannels(out var channels))
        { HasUserChanges = true; ApplyValue(channels, false, true); return; }
        _ = TryRead(out _, out string message); error.Text = message; StateChanged?.Invoke();
    }
    private bool TryChannels(out DesignerColorValue color)
    {
        color = default;
        if (!DesignerColorValue.TryReadChannel(alpha.Text, out byte a) || !DesignerColorValue.TryReadChannel(red.Text, out byte r)
            || !DesignerColorValue.TryReadChannel(green.Text, out byte g) || !DesignerColorValue.TryReadChannel(blue.Text, out byte b)) return false;
        color = new(a, r, g, b); return true;
    }
    private bool TryRead(out DesignerColorValue color, out string message)
    {
        color = default; message = "";
        if (!DesignerColorValue.TryParse(hex.Text, out var parsed)) { message = "Use #RRGGBB or #AARRGGBB for a color."; return false; }
        if (!TryChannels(out var channels)) { message = "Red, green, blue and alpha must be whole numbers from 0 to 255."; return false; }
        if (parsed != channels) { message = "Finish editing the color fields before applying."; return false; }
        color = parsed; return true;
    }
    public DesignerColorValue ReadValue() => TryRead(out var color, out string message) ? color : throw new FormatException(message);
    public IEnumerable<KeyValuePair<string, FrameworkElement>> Elements()
    {
        foreach (var element in elements) yield return element;
        if (picker.IsLoaded && (spectrum is null || !spectrum.IsLoaded)) spectrum = FindSpectrum(picker);
        if (spectrum is not null) yield return new("color-spectrum", spectrum);
    }
    private static FrameworkElement? FindSpectrum(DependencyObject root)
    {
        var pending = new Stack<DependencyObject>(); pending.Push(root); int examined = 0;
        while (pending.TryPop(out var item) && examined++ < 4096)
        {
            if (item is ColorSpectrum spectrum) return spectrum;
            for (int n = 0; n < VisualTreeHelper.GetChildrenCount(item); n++) pending.Push(VisualTreeHelper.GetChild(item, n));
        }
        return null;
    }
    private StackPanel Preview(string title, SolidColorBrush brush, string id)
    {
        var panel = new StackPanel { Spacing = 3 }; panel.Children.Add(ControlTheme.Label(title, 11));
        var board = new Grid { Width = 112, Height = 48, HorizontalAlignment = HorizontalAlignment.Left };
        for (int n = 0; n < 7; n++) board.ColumnDefinitions.Add(new());
        for (int n = 0; n < 3; n++) board.RowDefinitions.Add(new());
        var white = ControlTheme.Brush("#FFFFFF"); var gray = ControlTheme.Brush("#C0C0C0");
        for (int y = 0; y < 3; y++) for (int x = 0; x < 7; x++)
        { var cell = new Border { Background = (x + y) % 2 == 0 ? white : gray }; Grid.SetColumn(cell, x); Grid.SetRow(cell, y); board.Children.Add(cell); }
        var color = new Border { Background = brush, BorderBrush = normalBorder, BorderThickness = new(1) };
        Grid.SetColumnSpan(color, 7); Grid.SetRowSpan(color, 3); board.Children.Add(color);
        elements[id] = ControlTheme.Identify(board, id, title + " color preview"); panel.Children.Add(board); return panel;
    }
    private static Color Native(DesignerColorValue color) => Color.FromArgb(color.A, color.R, color.G, color.B);
    private static DesignerColorValue FromNative(Color color) => new(color.A, color.R, color.G, color.B);
}
