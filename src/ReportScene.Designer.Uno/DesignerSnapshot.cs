using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using ReportScene.Model;

namespace ReportScene.Designer.Uno;

public sealed record ElementBounds(double X, double Y, double Width, double Height);
public sealed record ObjectSnapshot(string Id, string Name, string Kind, RectD Bounds, string? Text, bool Selected, ElementBounds? Screen)
{
    public bool Locked { get; init; }
}
public sealed record DesignerSnapshot(string Document, bool Dirty, bool Preview, int TemplatePages, int PreparedPages, int PreviewPage, int ObjectCount, int SelectedCount, bool CanUndo, bool CanRedo, int HistoryCount, double Zoom, long RenderCount, string Status, IReadOnlyList<Diagnostic> Diagnostics, Dictionary<string, ElementBounds> Commands, Dictionary<string, ElementBounds> Properties, IReadOnlyList<ObjectSnapshot> Objects, Dictionary<string, string> Inputs, long SnapshotSequence)
{
    public long ContentRevision { get; init; }
    public long PreviewIndexBuilds { get; init; }
    public int PreviewCandidatesExamined { get; init; }
    public int SearchQueryScans { get; init; }
    public int SearchMatches { get; init; }
    public bool RecoveryPending { get; init; }
    public long DesignLayoutBuilds { get; init; }
    public int ReplacementScanCount { get; init; }
    public long OutlineCreatedNodes { get; init; }
    public long OutlineUpdates { get; init; }
    public int OutlineProjectedCount { get; init; }
    public int OutlineVisibleRows { get; init; }
    public string OutlineFilter { get; init; } = "";
    public string ActiveTree { get; init; } = "";
    public string PropertyFilter { get; init; } = "";
    public string[] PropertyProjection { get; init; } = [];
    public bool PropertiesAlphabetical { get; init; }
    public string PropertyValidation { get; init; } = "";
    public string DisplayUnit { get; init; } = "";
    public double SidebarWidth { get; init; }
    public double TreePaneHeight { get; init; }
    public bool TreeShown { get; init; }
    public bool PropertiesShown { get; init; }
    public bool ActiveDrag { get; init; }
}
[JsonSerializable(typeof(DesignerSnapshot))]
public partial class DesignerSnapshotJsonContext : System.Text.Json.Serialization.JsonSerializerContext;

public sealed partial class ReportDesignerControl
{
    private long snapshotSequence;
    /// <summary>Read-only diagnostic data. Test drivers deliver real input, never invoke application commands.</summary>
    public DesignerSnapshot Snapshot()
    {
        bool Visible(FrameworkElement element)
        {
            if (!element.IsLoaded || element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
            for (DependencyObject? p = element; p is not null; p = VisualTreeHelper.GetParent(p)) if (p is UIElement ui && ui.Visibility != Visibility.Visible) return false;
            return true;
        }
        ElementBounds? Bounds(FrameworkElement element)
        {
            if (!Visible(element)) return null;
            try { var p = element.TransformToVisual(null).TransformPoint(new(0, 0)); return new(p.X, p.Y, element.ActualWidth, element.ActualHeight); }
            catch (InvalidOperationException) { return null; }
        }
        var commandBounds = new Dictionary<string, ElementBounds>(StringComparer.Ordinal);
        var inputs = new Dictionary<string, string>(StringComparer.Ordinal);
        void Input(string id, FrameworkElement element)
        {
            switch (element)
            {
                case TextBox text: inputs[id] = text.Text; break;
                case ToggleButton toggle: inputs[id] = toggle.IsChecked?.ToString() ?? "Mixed"; break;
                case Slider slider: inputs[id] = slider.Value.ToString(CultureInfo.InvariantCulture); break;
                case ComboBox choice:
                    inputs[id] = choice.SelectedIndex.ToString(CultureInfo.InvariantCulture); inputs[id + "-value"] = choice.SelectedItem?.ToString() ?? "";
                    if (!choice.IsDropDownOpen) break;
                    for (int n = 0; n < choice.Items.Count; n++)
                        if (choice.ContainerFromIndex(n) is FrameworkElement option && Bounds(option) is { } optionBounds) commandBounds[id + "-option-" + n] = optionBounds;
                    break;
            }
        }
        foreach (var pair in commands.Concat(tree.Elements()))
        {
            if (Bounds(pair.Value) is not { } bounds) continue;
            commandBounds[pair.Key] = bounds; Input(pair.Key, pair.Value);
        }
        var propertyBounds = new Dictionary<string, ElementBounds>(StringComparer.Ordinal);
        foreach (var pair in properties.Editors)
            if (Bounds(pair.Value) is { } bounds) { propertyBounds[pair.Key] = bounds; Input("property:" + pair.Key, pair.Value); }
        var origin = Bounds(DesignView.CanvasElement);
        var objects = DesignView.Layout.Objects.Select(entry => new ObjectSnapshot(entry.Item.Id, entry.Item.Name, entry.Item.GetType().Name, entry.Item.Bounds, entry.Item is TextItem text ? text.Text : null, Session.SelectedIds.Contains(entry.Item.Id), origin is null ? null : new(origin.X + entry.Bounds.X * DesignView.PointScale, origin.Y + entry.Bounds.Y * DesignView.PointScale, entry.Bounds.Width * DesignView.PointScale, entry.Bounds.Height * DesignView.PointScale)) { Locked = entry.Item.Locked }).ToArray();
        return new(Session.Document.Name, Session.IsDirty, previewMode, Session.Document.Pages.Count, PreviewView.Report?.Pages.Count ?? 0, PreviewView.PageIndex + 1, DocumentTree.AllItems(Session.Document).Count(), Session.SelectedIds.Count, Session.CanUndo, Session.CanRedo, Session.HistoryCount, previewMode ? PreviewView.Zoom : DesignView.Zoom, previewMode ? PreviewView.RenderCount : DesignView.RenderCount, status.Text, diagnostics.ToArray(), commandBounds, propertyBounds, objects, inputs, ++snapshotSequence)
        {
            ContentRevision = Session.Revision, PreviewIndexBuilds = PreviewView.IndexBuildCount,
            PreviewCandidatesExamined = PreviewView.LastCandidatesExamined, SearchQueryScans = searchSession?.QueryScanCount ?? 0,
            SearchMatches = searchSession?.Matches.Count ?? 0, RecoveryPending = recovery.HasPending,
            DesignLayoutBuilds = DesignView.LayoutBuildCount, ReplacementScanCount = Session.LastReplacementScanCount,
            OutlineCreatedNodes = tree.CreatedNodeCount, OutlineUpdates = tree.UpdateCount, OutlineProjectedCount = tree.ProjectedCount,
            OutlineVisibleRows = tree.VisibleRowCount, OutlineFilter = tree.FilterText, ActiveTree = activeTree,
            PropertyFilter = propertyFilter.Text, PropertyProjection = properties.VisiblePropertyIds.ToArray(), PropertiesAlphabetical = properties.Alphabetical,
            PropertyValidation = properties.ValidationMessage, DisplayUnit = displayUnit.ToString(), SidebarWidth = sidebarWidth,
            TreePaneHeight = treeHeight, TreeShown = showTree, PropertiesShown = showProperties, ActiveDrag = Session.HasActiveDrag
        };
    }
}
