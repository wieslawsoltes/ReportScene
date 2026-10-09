using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ReportScene.Model;
using Windows.Foundation;

namespace ReportScene.Designer.Uno;

public sealed record ElementBounds(double X, double Y, double Width, double Height);
public sealed record ObjectSnapshot(string Id, string Name, string Kind, RectD Bounds, string? Text, bool Selected, ElementBounds? Screen);
public sealed record DesignerSnapshot(string Document, bool Dirty, bool Preview, int TemplatePages, int PreparedPages, int PreviewPage, int ObjectCount, int SelectedCount, bool CanUndo, bool CanRedo, int HistoryCount, double Zoom, long RenderCount, string Status, IReadOnlyList<Diagnostic> Diagnostics, Dictionary<string, ElementBounds> Commands, Dictionary<string, ElementBounds> Properties, IReadOnlyList<ObjectSnapshot> Objects);
[JsonSerializable(typeof(DesignerSnapshot))]
public partial class DesignerSnapshotJsonContext : System.Text.Json.Serialization.JsonSerializerContext;

public sealed partial class ReportDesignerControl
{
    /// <summary>Read-only test/diagnostic data. Browser tests deliver real pointer and keyboard input to the Uno controls.</summary>
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
        foreach (var pair in commands) if (Bounds(pair.Value) is { } bounds) commandBounds[pair.Key] = bounds;
        var propertyBounds = new Dictionary<string, ElementBounds>(StringComparer.Ordinal);
        foreach (var pair in properties.Editors) if (Bounds(pair.Value) is { } bounds) propertyBounds[pair.Key] = bounds;
        var origin = Bounds(DesignView.CanvasElement);
        var objects = DesignView.Layout.Objects.Select(entry => new ObjectSnapshot(entry.Item.Id, entry.Item.Name, entry.Item.GetType().Name, entry.Item.Bounds, entry.Item is TextItem text ? text.Text : null, Session.SelectedIds.Contains(entry.Item.Id), origin is null ? null : new(origin.X + entry.Bounds.X * DesignView.PointScale, origin.Y + entry.Bounds.Y * DesignView.PointScale, entry.Bounds.Width * DesignView.PointScale, entry.Bounds.Height * DesignView.PointScale))).ToArray();
        return new(Session.Document.Name, Session.IsDirty, previewMode, Session.Document.Pages.Count, PreviewView.Report?.Pages.Count ?? 0, PreviewView.PageIndex + 1, DocumentTree.AllItems(Session.Document).Count(), Session.SelectedIds.Count, Session.CanUndo, Session.CanRedo, Session.HistoryCount, previewMode ? PreviewView.Zoom : DesignView.Zoom, previewMode ? PreviewView.RenderCount : DesignView.RenderCount, status.Text, diagnostics.ToArray(), commandBounds, propertyBounds, objects);
    }
}
