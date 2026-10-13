using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using ReportScene.Controls.Uno;
using ReportScene.Model;

namespace ReportScene.Designer.Uno;

public sealed record ElementBounds(double X, double Y, double Width, double Height);
public sealed record ObjectSnapshot(string Id, string Name, string Kind, RectD Bounds, string? Text, bool Selected, ElementBounds? Screen)
{
    public bool Locked { get; init; }
}
public sealed record DesignerSnapshot(string Document, bool Dirty, bool Preview, int TemplatePages, int PreparedPages, int PreviewPage, int ObjectCount, int SelectedCount, bool CanUndo, bool CanRedo, int HistoryCount, double Zoom, long RenderCount, string Status, IReadOnlyList<Diagnostic> Diagnostics, Dictionary<string, ElementBounds> Commands, Dictionary<string, ElementBounds> Properties, IReadOnlyList<ObjectSnapshot> Objects, Dictionary<string, string> Inputs, long SnapshotSequence)
{
    public Guid DocumentId { get; init; }
    public long ContentRevision { get; init; }
    public long PreviewIndexBuilds { get; init; }
    public int PreviewCandidatesExamined { get; init; }
    public PreviewViewportSnapshot PreviewViewport { get; init; }
    public bool PreviewOutlineVisible { get; init; }
    public bool PreviewOutlineEffectiveVisible { get; init; }
    public double PreviewOutlineWidth { get; init; }
    public int PreviewBookmarkCount { get; init; }
    public long PreviewOutlineBuilds { get; init; }
    public long PreviewOutlineNodes { get; init; }
    public int PreviewOutlineProjectedCount { get; init; }
    public string PreviewOutlineFilter { get; init; } = "";
    public string? PreviewSelectedBookmark { get; init; }
    public RectD? PreviewHighlight { get; init; }
    public bool PreviewRevealPending { get; init; }
    public int SearchQueryScans { get; init; }
    public int SearchMatches { get; init; }
    public bool RecoveryPending { get; init; }
    public long DesignLayoutBuilds { get; init; }
    public RectD DesignPaintViewport { get; init; }
    public int DesignDrawnObjects { get; init; }
    public int ReplacementScanCount { get; init; }
    public long OutlineCreatedNodes { get; init; }
    public long OutlineUpdates { get; init; }
    public long OutlineSnapshotBuilds { get; init; }
    public string OutlineContextKey { get; init; } = "";
    public int OutlineContextCount { get; init; }
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
    public bool FullPaper { get; init; }
    public double DesignPaperHeight { get; init; }
    public double SourcePaperHeight { get; init; }
    public long TextLayoutCacheHits { get; init; }
    public long TextLayoutCacheMisses { get; init; }
    public int TextLayoutCacheCharacters { get; init; }
    public bool PreferencesLoaded { get; init; }
    public bool PreferencesPending { get; init; }
    public long PreferencesGeneration { get; init; }
    public long PreferencesWriteCount { get; init; }
    public string PreferencesError { get; init; } = "";
    public double PreferredSidebarWidth { get; init; }
    public double PreferredTreeHeight { get; init; }
    public bool EffectiveTreeShown { get; init; }
    public bool EffectivePropertiesShown { get; init; }
    public bool PaneDragActive { get; init; }
    public double WorkspaceWidth { get; init; }
    public double WorkspaceHeight { get; init; }
    public double GridSpacing { get; init; }
    public bool SnapEnabled { get; init; }
    public bool GridVisible { get; init; }
    public bool RulersVisible { get; init; }
    public double DesignZoom { get; init; }
    public double PreviewZoom { get; init; }
}
[JsonSerializable(typeof(DesignerSnapshot))]
public partial class DesignerSnapshotJsonContext : System.Text.Json.Serialization.JsonSerializerContext;

public sealed partial class ReportDesignerControl
{
    private long snapshotSequence;
    /// <summary>Read-only diagnostic data. Test drivers deliver real input, never invoke application commands.</summary>
    public DesignerSnapshot Snapshot()
    {
        bool InVisibleTree(FrameworkElement element)
        {
            if (!element.IsLoaded) return false;
            for (DependencyObject? p = element; p is not null; p = VisualTreeHelper.GetParent(p))
                if (p is UIElement ui && ui.Visibility != Visibility.Visible) return false;
            return true;
        }
        ElementBounds? Bounds(FrameworkElement element)
        {
            if (!InVisibleTree(element) || element.ActualWidth <= 0 || element.ActualHeight <= 0) return null;
            try { var p = element.TransformToVisual(null).TransformPoint(new(0, 0)); return new(p.X, p.Y, element.ActualWidth, element.ActualHeight); }
            catch (InvalidOperationException) { return null; }
        }
        static ElementBounds Intersect(ElementBounds a, ElementBounds b)
        {
            double x = Math.Max(a.X, b.X), y = Math.Max(a.Y, b.Y);
            return new(x, y, Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - x), Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - y));
        }
        ElementBounds? PopupItemBounds(FrameworkElement option, out ElementBounds? popupBounds)
        {
            popupBounds = null;
            if (Bounds(option) is not { } visible) return null;
            for (DependencyObject? p = VisualTreeHelper.GetParent(option); p is not null; p = VisualTreeHelper.GetParent(p))
            {
                if (p is Popup) break;
                if (p is FrameworkElement parent && (p is ScrollViewer or ScrollContentPresenter || parent.Name == "PopupBorder") && Bounds(parent) is { } clip)
                {
                    visible = Intersect(visible, clip);
                    if (parent.Name == "PopupBorder") popupBounds = clip;
                    else popupBounds ??= clip;
                }
            }
            if (option.XamlRoot is { } root) visible = Intersect(visible, new(0, 0, root.Size.Width, root.Size.Height));
            return visible.Width > 4 && visible.Height > 4 ? visible : null;
        }
        var commandBounds = new Dictionary<string, ElementBounds>(StringComparer.Ordinal);
        var inputs = new Dictionary<string, string>(StringComparer.Ordinal);
        void Input(string id, FrameworkElement element)
        {
            if (element is Control control) inputs[id + "-enabled"] = control.IsEnabled.ToString();
            switch (element)
            {
                case TextBox text: inputs[id] = text.Text; break;
                case TextBlock label: inputs[id] = label.Text; break;
                case TreeViewItem row: inputs[id] = row.Content?.ToString() ?? ""; break;
                case ToggleButton toggle: inputs[id] = toggle.IsChecked?.ToString() ?? "Mixed"; break;
                case Slider slider: inputs[id] = slider.Value.ToString(CultureInfo.InvariantCulture); break;
                case ComboBox choice:
                    inputs[id] = choice.SelectedIndex.ToString(CultureInfo.InvariantCulture); inputs[id + "-value"] = choice.SelectedItem?.ToString() ?? "";
                    if (!choice.IsDropDownOpen) break;
                    for (int n = 0; n < choice.Items.Count; n++)
                    {
                        if (choice.ContainerFromIndex(n) is not FrameworkElement option) continue;
                        var optionBounds = PopupItemBounds(option, out var popupBounds);
                        if (popupBounds is not null) commandBounds[id + "-popup"] = popupBounds;
                        if (optionBounds is not null) commandBounds[id + "-option-" + n] = optionBounds;
                    }
                    break;
            }
        }
        foreach (var pair in commands.Concat(tree.Elements()).Concat(PreviewView.OutlineElements()))
        {
            if (Bounds(pair.Value) is { } bounds)
            { commandBounds[pair.Key] = bounds; Input(pair.Key, pair.Value); }
            // An empty validation TextBlock may legitimately measure zero width.
            // Its native Text still exists; report it without inventing a clickable
            // rectangle. Unloaded or collapsed controls remain excluded.
            else if (pair.Value is TextBlock label && InVisibleTree(label)) Input(pair.Key, label);
        }
        if (Bounds(DesignView.CanvasElement) is { } designPaper) commandBounds["design-paper"] = designPaper;
        if (Bounds(DesignView.ViewportElement) is { } designViewport) commandBounds["design-viewport"] = designViewport;
        var propertyBounds = new Dictionary<string, ElementBounds>(StringComparer.Ordinal);
        foreach (var pair in properties.Editors)
            if (Bounds(pair.Value) is { } bounds) { propertyBounds[pair.Key] = bounds; Input("property:" + pair.Key, pair.Value); }
        var origin = Bounds(DesignView.CanvasElement);
        var objects = DesignView.Layout.Objects.Select(entry => new ObjectSnapshot(entry.Item.Id, entry.Item.Name, entry.Item.GetType().Name, entry.Item.Bounds, entry.Item is TextItem text ? text.Text : null, Session.SelectedIds.Contains(entry.Item.Id), origin is null ? null : new(origin.X + entry.Bounds.X * DesignView.PointScale, origin.Y + entry.Bounds.Y * DesignView.PointScale, entry.Bounds.Width * DesignView.PointScale, entry.Bounds.Height * DesignView.PointScale)) { Locked = entry.Item.Locked }).ToArray();
        return new(Session.Document.Name, Session.IsDirty, previewMode, Session.Document.Pages.Count, PreviewView.Report?.Pages.Count ?? 0, PreviewView.PageIndex + 1, DocumentTree.AllItems(Session.Document).Count(), Session.SelectedIds.Count, Session.CanUndo, Session.CanRedo, Session.HistoryCount, previewMode ? PreviewView.Zoom : DesignView.Zoom, previewMode ? PreviewView.RenderCount : DesignView.RenderCount, status.Text, diagnostics.ToArray(), commandBounds, propertyBounds, objects, inputs, ++snapshotSequence)
        {
            DocumentId = Session.DocumentId, ContentRevision = Session.Revision, PreviewIndexBuilds = PreviewView.IndexBuildCount,
            PreviewCandidatesExamined = PreviewView.LastCandidatesExamined, PreviewViewport = PreviewView.ViewportSnapshot,
            PreviewOutlineVisible = PreviewView.OutlineVisible, PreviewOutlineEffectiveVisible = PreviewView.OutlineEffectivelyVisible,
            PreviewOutlineWidth = PreviewView.OutlineWidth, PreviewBookmarkCount = PreviewView.OutlineSnapshot?.Count ?? 0,
            PreviewOutlineBuilds = PreviewView.OutlineBuildCount, PreviewOutlineNodes = PreviewView.OutlineNodeCount,
            PreviewOutlineFilter = PreviewView.OutlineFilter, PreviewOutlineProjectedCount = PreviewView.OutlineProjectedCount,
            PreviewSelectedBookmark = PreviewView.SelectedBookmarkKey, PreviewHighlight = PreviewView.HighlightBounds, PreviewRevealPending = PreviewView.RevealPending,
            SearchQueryScans = searchSession?.QueryScanCount ?? 0, SearchMatches = searchSession?.Matches.Count ?? 0, RecoveryPending = recovery.HasPending,
            DesignLayoutBuilds = DesignView.LayoutBuildCount, DesignPaintViewport = DesignView.LastPaintViewport, DesignDrawnObjects = DesignView.LastDrawnObjectCount,
            ReplacementScanCount = Session.LastReplacementScanCount,
            OutlineCreatedNodes = tree.CreatedNodeCount, OutlineUpdates = tree.UpdateCount, OutlineSnapshotBuilds = outlineSnapshotBuilds,
            OutlineContextCount = tree.ContextCount, OutlineContextKey = tree.ContextKey, OutlineProjectedCount = tree.ProjectedCount,
            OutlineVisibleRows = tree.VisibleRowCount, OutlineFilter = tree.FilterText, ActiveTree = activeTree,
            PropertyFilter = propertyFilter.Text, PropertyProjection = properties.VisiblePropertyIds.ToArray(), PropertiesAlphabetical = properties.Alphabetical,
            PropertyValidation = properties.ValidationMessage, DisplayUnit = displayUnit.ToString(), SidebarWidth = sidebarWidth,
            TreePaneHeight = treeHeight, TreeShown = showTree, PropertiesShown = showProperties, ActiveDrag = Session.HasActiveDrag,
            FullPaper = DesignView.ShowFullPage, DesignPaperHeight = DesignView.Layout.Paper.Height, SourcePaperHeight = Session.Page.Height,
            TextLayoutCacheHits = DesignView.TextLayouts.Hits, TextLayoutCacheMisses = DesignView.TextLayouts.Misses,
            TextLayoutCacheCharacters = DesignView.TextLayouts.RetainedCharacters,
            PreferencesLoaded = preferencesLoaded, PreferencesPending = preferences.HasPending,
            PreferencesGeneration = preferences.Generation, PreferencesWriteCount = preferences.WriteCount, PreferencesError = preferenceError,
            PreferredSidebarWidth = preferredSidebarWidth, PreferredTreeHeight = preferredTreeHeight,
            EffectiveTreeShown = visiblePaneLayout.ShowTree, EffectivePropertiesShown = visiblePaneLayout.ShowProperties,
            PaneDragActive = sideSplitter.IsDragging || treeSplitter.IsDragging,
            WorkspaceWidth = workspace.ActualWidth, WorkspaceHeight = workspace.ActualHeight,
            GridSpacing = Session.GridSize, SnapEnabled = Session.SnapToGrid,
            GridVisible = DesignView.ShowGrid, RulersVisible = DesignView.ShowRulers,
            DesignZoom = DesignView.Zoom, PreviewZoom = PreviewView.Zoom
        };
    }
}
