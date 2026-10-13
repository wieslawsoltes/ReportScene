using ReportScene.Model;

namespace ReportScene.Designer;

/// <summary>Only unlocked targets are editable; equal styles are counted but create no history.</summary>
public readonly record struct DesignerFormattingResult(int EditableObjects, int ChangedObjects);

public sealed partial class DesignerSession
{
    /// <summary>
    /// Copies appearance only, resolving all explicit targets before one undoable edit.
    /// IDs, content, expressions, geometry, data bindings and container children are retained.
    /// Lookup and replacement are linear scans, not one full scan per selected object.
    /// </summary>
    public DesignerFormattingResult ApplyFormatting(ItemStyle style, IReadOnlyCollection<string> objectIds)
    {
        ArgumentNullException.ThrowIfNull(style); ArgumentNullException.ThrowIfNull(objectIds);
        if (HasActiveDrag) throw new InvalidOperationException("Complete or cancel the active drag before applying formatting.");
        ValidateFormatting(style);
        var requested = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in objectIds)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Formatting targets must have nonempty identifiers.", nameof(objectIds));
            requested.Add(id);
        }
        if (requested.Count == 0) return default;
        var found = new HashSet<string>(StringComparer.Ordinal);
        var before = new List<ReportItem>(); int editable = 0;
        foreach (var item in DocumentTree.AllItems(Document))
        {
            if (!requested.Contains(item.Id)) continue;
            if (!found.Add(item.Id)) throw new InvalidOperationException("Formatting targets have duplicate identifiers.");
            if (item.Locked) continue;
            editable++;
            if (item.Style != style) before.Add(item);
        }
        if (found.Count != requested.Count) throw new InvalidOperationException("One or more formatting targets no longer exist.");
        if (editable == 0) return default;
        if (before.Count == 0) { SelectMany(requested); return new(editable, 0); }
        var original = before.ToArray(); var changed = original.Select(item => item with { Style = style }).ToArray();
        // Own the target list: later changes to a caller's collection cannot alter redo selection.
        Execute("Format painter", () => ReplaceItems(changed), () => ReplaceItems(original), requested.ToArray());
        return new(editable, changed.Length);
    }
    internal static void ValidateFormatting(ItemStyle style)
    {
        static bool Positive(double value) => double.IsFinite(value) && value > 0;
        static bool Nonnegative(double value) => double.IsFinite(value) && value >= 0;
        if (!Positive(style.FontSize) || !Positive(style.LineHeight) || !Nonnegative(style.Padding) || !Nonnegative(style.BorderWidth)
            || string.IsNullOrWhiteSpace(style.FontFamily) || !Enum.IsDefined(style.Alignment) || !Enum.IsDefined(style.VerticalAlignment))
            throw new ArgumentException("Formatting needs a font, finite positive text metrics and valid alignment.", nameof(style));
        // Do not normalize: valid ARGB spelling and transparent RGB channels are copied exactly.
        if (!DesignerColorValue.TryParse(style.Foreground, out _) || !DesignerColorValue.TryParse(style.Fill, out _)
            || !DesignerColorValue.TryParse(style.BorderColor, out _))
            throw new ArgumentException("Formatting colors must be #RRGGBB or #AARRGGBB.", nameof(style));
    }
}
