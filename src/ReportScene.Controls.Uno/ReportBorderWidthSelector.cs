using System.Globalization;
using Microsoft.UI.Xaml.Controls;

namespace ReportScene.Controls.Uno;

/// <summary>
/// Whole-object border width in points. Presets have stable positions; one extra
/// display row preserves an exact imported/property-edited width. Display text
/// is never parsed back into the host's value. Null represents no common width.
/// </summary>
public sealed class ReportBorderWidthSelector : UserControl
{
    private static readonly double[] Presets = [0, .25, .5, .75, 1, 1.5, 2, 3, 4, 6];
    private bool synchronizing;
    private double? width;
    public double? BorderWidth => width;
    public ReportComboBox Editor { get; } = new() { Width = 88, Height = 25, MinHeight = 0,
        Padding = new(5, 0, 0, 0), FontSize = 11, CornerRadius = new(0) };
    public event Action<double>? WidthRequested;
    public ReportBorderWidthSelector()
    {
        Width = 88; Height = 25; Content = Editor;
        foreach (double value in Presets) Editor.Items.Add(Label(value));
        Editor.SelectionChanged += (_, _) =>
        {
            if (synchronizing || (uint)Editor.SelectedIndex >= (uint)Editor.Items.Count) return;
            double? requested = Editor.SelectedIndex < Presets.Length ? Presets[Editor.SelectedIndex] : width;
            if (requested is { } value && requested != width) WidthRequested?.Invoke(value);
        };
    }
    public void SetWidth(double? value, bool mixed = false)
    {
        if (value is { } number && (!double.IsFinite(number) || number < 0)) throw new ArgumentOutOfRangeException(nameof(value));
        if (value is not null && mixed) throw new ArgumentException("A common width cannot also be mixed.", nameof(mixed));
        synchronizing = true;
        try
        {
            width = value; Editor.PlaceholderText = mixed ? "Mixed" : "";
            int index = value is { } actual ? Array.IndexOf(Presets, actual) : -1;
            if (value is { } custom && index < 0)
            {
                string label = Label(custom);
                if (Editor.Items.Count == Presets.Length) Editor.Items.Add(label);
                else if (!Equals(Editor.Items[Presets.Length], label))
                {
                    Editor.SelectedIndex = -1; // Rebind the selected native item as well as the slot.
                    Editor.Items[Presets.Length] = label;
                }
                Editor.SelectedIndex = Presets.Length;
            }
            else
            {
                Editor.SelectedIndex = index;
                if (Editor.Items.Count > Presets.Length) Editor.Items.RemoveAt(Presets.Length);
            }
        }
        finally { synchronizing = false; }
    }
    private static string Label(double value) => value == 0 ? "None" :
        value.ToString(value >= 10000 ? "0.###E+0" : "0.###", CultureInfo.InvariantCulture) + " pt";
}
