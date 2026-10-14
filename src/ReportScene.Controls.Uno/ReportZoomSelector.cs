using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ReportScene.Controls.Uno;

/// <summary>
/// Native preset selector which can also display an exact externally fitted scale.
/// At most one additional display row is retained. Formatting never feeds a rounded
/// value back into the host; the host owns zoom changes through ZoomRequested.
/// </summary>
public sealed class ReportZoomSelector : UserControl
{
    private static readonly double[] Presets = [.25, .5, .75, .85, 1, 1.25, 1.5, 2, 3, 4];
    private bool synchronizing;
    private double zoom = double.NaN;
    public ReportComboBox Editor { get; } = new() { Width = 88, MinHeight = 0, Height = 25,
        Padding = new(5, 0, 0, 0), FontSize = 11, CornerRadius = new(0) };
    public double Zoom => zoom;
    public event Action<double>? ZoomRequested;
    public ReportZoomSelector()
    {
        Width = 88; Height = 25; Content = Editor;
        foreach (double preset in Presets) Editor.Items.Add(Label(preset));
        SetZoom(1);
        Editor.SelectionChanged += (_, _) =>
        {
            if (synchronizing || (uint)Editor.SelectedIndex >= (uint)Editor.Items.Count) return;
            double requested = Editor.SelectedIndex < Presets.Length ? Presets[Editor.SelectedIndex] : zoom;
            if (requested != zoom) ZoomRequested?.Invoke(requested);
        };
    }
    public void SetZoom(double value)
    {
        if (!double.IsFinite(value) || value <= 0 || value > 4) throw new ArgumentOutOfRangeException(nameof(value));
        int index = Array.IndexOf(Presets, value);
        int expectedIndex = index < 0 ? Presets.Length : index;
        if (value == zoom && Editor.SelectedIndex == expectedIndex
            && Equals(Editor.SelectedItem, Label(value))) return;
        synchronizing = true;
        try
        {
            zoom = value;
            if (index < 0)
            {
                string label = Label(value);
                if (Editor.Items.Count == Presets.Length) Editor.Items.Add(label);
                else if (!Equals(Editor.Items[Presets.Length], label))
                {
                    // The native selector can retain SelectedItem when a selected
                    // Items slot is replaced at the same SelectedIndex. Deselect
                    // before replacement so both the item and its painted text
                    // are rebound. The synchronization guard suppresses feedback.
                    Editor.SelectedIndex = -1;
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
    private static string Label(double value) => (value * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";
}
