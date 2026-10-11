using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace ReportScene.Controls.Uno;

/// <summary>Reusable compact property grid with categories, filtering, sorting, validation and generation-safe typed editors.</summary>
public sealed class PropertySheet : StackPanel
{
    private sealed record Entry(string Name, string Id, string Category, Grid Row, FrameworkElement Editor);
    private readonly List<Entry> entries = [];
    private readonly Dictionary<string, Button> categories = new(StringComparer.Ordinal);
    private readonly HashSet<string> collapsed = new(StringComparer.Ordinal);
    private string category = "", filter = "";
    private bool alphabetical, arranging;
    private int generation, deferred;
    public event Action<Exception>? Failed;
    public event Action<string, string>? DescriptionChanged;
    public event Action? ViewChanged;
    public Action? CommitGuard { get; set; }
    public string ValidationMessage { get; private set; } = "";
    public Dictionary<string, FrameworkElement> Editors { get; } = new(StringComparer.Ordinal);
    public string FilterText { get => filter; set { filter = value ?? ""; ArrangeRows(); } }
    public bool Alphabetical { get => alphabetical; set { alphabetical = value; ArrangeRows(); } }
    public IReadOnlyList<string> VisiblePropertyIds => entries.Where(e => e.Row.Parent == this).Select(e => e.Id).ToArray();
    public PropertySheet() { Spacing = 0; }
    public IDisposable DeferRefresh() { deferred++; return new RefreshScope(this); }
    private sealed class RefreshScope(PropertySheet owner) : IDisposable
    {
        private PropertySheet? sheet = owner;
        public void Dispose() { if (sheet is not { } current) return; sheet = null; if (--current.deferred == 0) current.ArrangeRows(); }
    }
    public void Clear()
    {
        generation++; arranging = true;
        try { Children.Clear(); entries.Clear(); Editors.Clear(); categories.Clear(); category = ""; ValidationMessage = ""; }
        finally { arranging = false; }
    }
    public void Category(string name)
    {
        category = name;
        if (!categories.ContainsKey(name))
        {
            var button = new Button { Content = name, MinHeight = 0, Height = 22, Padding = new(5, 0, 2, 0), CornerRadius = new(0), BorderThickness = new(0, 1, 0, 1), BorderBrush = ControlTheme.Brush("#D8D8D8"), Background = ControlTheme.Brush("#EEEEEE"), FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch };
            ControlTheme.Identify(button, "property-category-" + name, "Expand or collapse " + name);
            button.Click += (_, _) => { if (!collapsed.Add(name)) collapsed.Remove(name); ArrangeRows(); };
            categories.Add(name, button);
        }
        ArrangeRows();
    }
    public void ExpandAll() { collapsed.Clear(); ArrangeRows(); }
    public void CollapseAll() { collapsed.UnionWith(categories.Keys); ArrangeRows(); }
    private void ArrangeRows()
    {
        if (deferred != 0) return;
        string query = filter.Trim();
        bool Match(Entry e) => query.Length == 0 || e.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || e.Id.Contains(query, StringComparison.OrdinalIgnoreCase) || e.Category.Contains(query, StringComparison.OrdinalIgnoreCase);
        var matching = entries.Where(Match);
        var desired = new List<UIElement>();
        if (alphabetical) desired.AddRange(matching.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).Select(e => e.Row));
        else
        {
            foreach (var group in matching.GroupBy(e => e.Category))
            {
                if (categories.TryGetValue(group.Key, out var header)) { header.Content = (collapsed.Contains(group.Key) && query.Length == 0 ? "+  " : "−  ") + group.Key; desired.Add(header); }
                if (query.Length > 0 || !collapsed.Contains(group.Key)) desired.AddRange(group.Select(e => e.Row));
            }
        }
        if (!Children.SequenceEqual(desired))
        {
            arranging = true;
            try { Children.Clear(); foreach (var child in desired) Children.Add(child); }
            finally { arranging = false; }
        }
        ViewChanged?.Invoke();
    }
    private void Row(string name, FrameworkElement editor, string id)
    {
        if (Editors.ContainsKey(id)) throw new ArgumentException("Duplicate property identifier: " + id);
        var grid = new Grid { MinHeight = 22, BorderBrush = ControlTheme.Brush("#E2E2E2"), BorderThickness = new(0, 0, 0, 1) };
        grid.ColumnDefinitions.Add(new() { Width = new(.44, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = new(.56, GridUnitType.Star) });
        var label = ControlTheme.Label(name, 11); label.Margin = new(8, 0, 3, 0); label.TextTrimming = TextTrimming.CharacterEllipsis;
        grid.Children.Add(new Border { Child = label, BorderBrush = ControlTheme.Brush("#E5E5E5"), BorderThickness = new(0, 0, 1, 0) });
        editor.MinWidth = 0; Grid.SetColumn(editor, 1); grid.Children.Add(editor);
        ControlTheme.Identify(editor, "property-" + id, name); Editors[id] = editor; entries.Add(new(name, id, category, grid, editor));
        editor.GotFocus += (_, _) => DescriptionChanged?.Invoke(name, Description(id, name));
        ArrangeRows();
    }
    private static string Description(string id, string name) => id switch
    {
        "name" or "band-name" => "The name identifies this object in the report tree and expressions.",
        "left" or "top" => "Position relative to the containing band, in the selected display units.",
        "width" or "height" or "band-height" => "Size in the selected display units. Report files always store points (1/72 inch).",
        "text" => "Literal text and bracketed expressions. Enter inserts a line; Escape restores the previous value.",
        "font-size" => "Font size in points, independent of the geometry display units.",
        "band-filter" or "visible" => "An expression evaluated against the current data and parameter scope.",
        "hyperlink" => "The link used in supported prepared-report exports. Executable URL schemes are not allowed.",
        _ => name + ". Changes commit on Enter or when the editor loses focus. Escape restores the previous value."
    };
    private bool TryCommit(int epoch, Action? guard, FrameworkElement editor, Action action)
    {
        if (epoch != generation || arranging) return false;
        try { guard?.Invoke(); action(); ValidationMessage = ""; ToolTipService.SetToolTip(editor, null); return true; }
        catch (Exception e) { ValidationMessage = e.Message; ToolTipService.SetToolTip(editor, e.Message); DescriptionChanged?.Invoke("Invalid value", e.Message); Failed?.Invoke(e); return false; }
    }
    public TextBox Text(string name, string value, Action<string> commit, string? id = null, bool multiline = false)
    {
        string old = value; int epoch = generation; var guard = CommitGuard;
        var input = new TextBox { Text = value, FontSize = 11, MinHeight = 0, Padding = new(4, 1, 3, 1), BorderThickness = new(0), CornerRadius = new(0), AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinWidth = 0, Height = multiline ? 46 : 22, MaxLength = 1_000_000 };
        if (multiline) ScrollViewer.SetVerticalScrollBarVisibility(input, ScrollBarVisibility.Auto);
        void Commit() { if (old == input.Text) return; TryCommit(epoch, guard, input, () => { commit(input.Text); old = input.Text; }); }
        input.LostFocus += (_, _) => Commit();
        input.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape) { input.Text = old; ValidationMessage = ""; ToolTipService.SetToolTip(input, null); e.Handled = true; }
            else if (e.Key == VirtualKey.Enter && !multiline) { Commit(); e.Handled = true; }
        };
        Row(name, input, id ?? name); return input;
    }
    public TextBox Number(string name, double value, Action<double> commit, string? id = null)
        => Text(name, value.ToString("0.###", CultureInfo.InvariantCulture), s => { if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)) throw new FormatException($"{name} must be a finite number."); commit(number); }, id);
    public CheckBox Boolean(string name, bool value, Action<bool> commit, string? id = null)
    {
        int epoch = generation; var guard = CommitGuard; bool suppress = false;
        var input = new CheckBox { IsChecked = value, MinHeight = 0, Height = 22, Padding = new(4, 0, 0, 0), MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Left };
        void Commit(bool next)
        {
            if (suppress || next == value) return;
            if (TryCommit(epoch, guard, input, () => commit(next))) value = next;
            else { suppress = true; try { input.IsChecked = value; } finally { suppress = false; } }
        }
        input.Checked += (_, _) => Commit(true); input.Unchecked += (_, _) => Commit(false); Row(name, input, id ?? name); return input;
    }
    public ComboBox Choice(string name, IEnumerable<string> choices, string value, Action<string> commit, string? id = null)
    {
        int epoch = generation; var guard = CommitGuard; bool suppress = false;
        var input = new ReportComboBox { FontSize = 11, MinHeight = 0, Height = 22, Padding = new(4, 0, 20, 0), BorderThickness = new(0), CornerRadius = new(0), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var choice in choices) input.Items.Add(choice);
        input.SelectedItem = value;
        input.SelectionChanged += (_, _) =>
        {
            if (suppress || input.SelectedItem is not string item || item == value) return;
            if (TryCommit(epoch, guard, input, () => commit(item))) value = item;
            else { suppress = true; try { input.SelectedItem = value; } finally { suppress = false; } }
        };
        Row(name, input, id ?? name); return input;
    }
    public void ReadOnly(string name, string value, string? id = null) => Row(name, new TextBlock { Text = value, FontSize = 11, Margin = new(4, 1, 3, 1), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }, id ?? name);
}
