using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace ReportScene.Controls.Uno;

/// <summary>Reusable property editor with commit-on-focus-loss, validation feedback and typed editors.</summary>
public sealed class PropertySheet : StackPanel
{
    public event Action<Exception>? Failed;
    public Dictionary<string, FrameworkElement> Editors { get; } = new(StringComparer.Ordinal);
    public PropertySheet() { Spacing = 0; }
    public void Clear() { Children.Clear(); Editors.Clear(); }
    public void Category(string name)
    {
        Children.Add(new Border { Background = ControlTheme.Brush("#E6E8EB"), BorderBrush = ControlTheme.Brush("#CCD0D4"), BorderThickness = new(0, 1, 0, 1), Padding = new(7, 5, 4, 5), Child = ControlTheme.Label(name, 11, true) });
    }
    private void Row(string name, FrameworkElement editor, string id)
    {
        var grid = new Grid { MinHeight = 29, BorderBrush = ControlTheme.Brush("#E5E7E9"), BorderThickness = new(0, 0, 0, 1) };
        grid.ColumnDefinitions.Add(new() { Width = new(104) }); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var label = ControlTheme.Label(name, 11); label.Margin = new(7, 2, 4, 2); grid.Children.Add(label);
        editor.MinWidth = 0; editor.Margin = new(0); Grid.SetColumn(editor, 1); grid.Children.Add(editor); Children.Add(grid);
        ControlTheme.Identify(editor, "property-" + id, name); Editors[id] = editor;
    }
    private void Try(Action action) { try { action(); } catch (Exception e) { Failed?.Invoke(e); } }
    public TextBox Text(string name, string value, Action<string> commit, string? id = null, bool multiline = false)
    {
        string old = value;
        var input = new TextBox { Text = value, FontSize = 11, MinHeight = 27, Padding = new(5, 3, 3, 3), BorderThickness = new(0), CornerRadius = new(0), AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinWidth = 0, MaxLength = 1_000_000 };
        if (multiline) { input.Height = 78; ScrollViewer.SetVerticalScrollBarVisibility(input, ScrollBarVisibility.Auto); }
        void Commit() { if (old == input.Text) return; Try(() => { commit(input.Text); old = input.Text; }); }
        input.LostFocus += (_, _) => Commit(); input.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter && !multiline) { Commit(); e.Handled = true; } };
        Row(name, input, id ?? name); return input;
    }
    public TextBox Number(string name, double value, Action<double> commit, string? id = null)
        => Text(name, value.ToString("0.###", CultureInfo.InvariantCulture), s => { if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)) throw new FormatException($"{name} must be a finite number."); commit(number); }, id);
    public CheckBox Boolean(string name, bool value, Action<bool> commit, string? id = null)
    {
        var input = new CheckBox { IsChecked = value, MinHeight = 27, Padding = new(5, 0, 0, 0), MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Left };
        input.Checked += (_, _) => Try(() => commit(true)); input.Unchecked += (_, _) => Try(() => commit(false)); Row(name, input, id ?? name); return input;
    }
    public ComboBox Choice(string name, IEnumerable<string> choices, string value, Action<string> commit, string? id = null)
    {
        var input = new ComboBox { FontSize = 11, MinHeight = 27, Padding = new(5, 2, 22, 2), BorderThickness = new(0), CornerRadius = new(0), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var choice in choices) input.Items.Add(choice);
        input.SelectedItem = value;
        input.SelectionChanged += (_, _) => { if (input.SelectedItem is string item && item != value) { value = item; Try(() => commit(item)); } };
        Row(name, input, id ?? name); return input;
    }
    public void ReadOnly(string name, string value, string? id = null) => Row(name, new TextBlock { Text = value, FontSize = 11, Margin = new(5, 4, 3, 4), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }, id ?? name);
}
