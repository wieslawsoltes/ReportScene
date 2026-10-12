using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ReportScene.Controls.Uno;

/// <summary>A native ComboBox whose popup rows match a compact report-authoring toolbar.</summary>
public sealed class ReportComboBox : ComboBox
{
    public ReportComboBox()
    {
        DefaultStyleKey = typeof(ComboBox);
        // Uno limits the managed popup to nine closed-control heights. Keeping the
        // default 36px rows inside a 24–25px toolbar control hides the last entries of
        // an otherwise short list. Compact rows retain native scrolling/keyboard input.
        // Source: unoplatform/uno, ComboBox.custom.cs, DropDownLayouter.Measure.
        var style = new Style(typeof(ComboBoxItem));
        style.Setters.Add(new Setter(MinHeightProperty, 22d));
        style.Setters.Add(new Setter(HeightProperty, 22d));
        style.Setters.Add(new Setter(PaddingProperty, new Thickness(6, 1, 6, 1)));
        style.Setters.Add(new Setter(FontSizeProperty, 11d));
        ItemContainerStyle = style;
    }
}
