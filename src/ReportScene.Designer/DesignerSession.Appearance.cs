using ReportScene.Model;

namespace ReportScene.Designer;

public sealed partial class DesignerSession
{
    /// <summary>Sets the supported whole-object border width in points, in one undoable edit.</summary>
    public void SetSelectionBorderWidth(double width)
    {
        if (!double.IsFinite(width) || width < 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (HasActiveDrag) throw new InvalidOperationException("Complete or cancel the active drag before changing borders.");
        UpdateSelection("Border width", item => item with { Style = item.Style with { BorderWidth = width } });
    }
    /// <summary>
    /// Removes borders when all editable targets have them; otherwise adds missing
    /// borders at one point while retaining existing positive widths. Mixed targets
    /// are never individually inverted. Locked targets do not determine the action.
    /// </summary>
    public void ToggleSelectionBorder()
    {
        if (HasActiveDrag) throw new InvalidOperationException("Complete or cancel the active drag before changing borders.");
        var editable = Selection.Where(item => !item.Locked).ToArray();
        if (editable.Length == 0) return;
        bool remove = editable.All(item => item.Style.BorderWidth > 0);
        UpdateSelection("Toggle border", item => item with { Style = item.Style with
        { BorderWidth = remove ? 0 : item.Style.BorderWidth > 0 ? item.Style.BorderWidth : 1 } });
    }
    /// <summary>Sets supported text vertical alignment without changing any other style or content field.</summary>
    public void SetSelectionVerticalAlignment(VerticalAlign alignment)
    {
        if (!Enum.IsDefined(alignment)) throw new ArgumentOutOfRangeException(nameof(alignment));
        if (HasActiveDrag) throw new InvalidOperationException("Complete or cancel the active drag before changing alignment.");
        UpdateSelection("Vertical alignment", item => item with { Style = item.Style with { VerticalAlignment = alignment } });
    }
}
