using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReportScene.Model;

namespace ReportScene.Controls.Uno;

public sealed partial class ReportPreviewView
{
    private readonly Grid previewRoot = new();
    private readonly ReportPreviewOutlineView outlineView = new();
    private readonly PaneSplitter outlineSplitter = new() { Minimum = 160, Maximum = 640, Width = 4 };
    private PreparedOutlineSnapshot? outlineSnapshot;
    private RectD? pendingReveal;
    private bool outlineVisible, sizingOutline, revealQueued, disposed;
    private double outlineWidth = 220, dragOutlineWidth;
    public bool OutlineVisible
    {
        get => outlineVisible;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (value) EnsureOutline();
            if (outlineVisible == value) return;
            outlineVisible = value; ApplyOutlineLayout(); ViewChanged?.Invoke();
        }
    }
    public double OutlineWidth
    {
        get => outlineWidth;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!double.IsFinite(value) || value < 160 || value > 640) throw new ArgumentOutOfRangeException(nameof(value));
            if (outlineWidth == value) return;
            outlineWidth = value; ApplyOutlineLayout(); ViewChanged?.Invoke();
        }
    }
    public bool OutlineEffectivelyVisible => outlineView.Visibility == Visibility.Visible;
    public PreparedOutlineSnapshot? OutlineSnapshot => outlineSnapshot;
    public long OutlineBuildCount { get; private set; }
    public long OutlineNodeCount => outlineView.NodeCreationCount;
    public string OutlineFilter => outlineView.FilterText;
    public int OutlineProjectedCount => outlineView.ProjectedCount;
    public string? SelectedBookmarkKey { get; private set; }
    public RectD? HighlightBounds => surface.Highlight;
    public bool RevealPending => pendingReveal is not null;
    public IEnumerable<KeyValuePair<string, FrameworkElement>> OutlineElements()
    {
        yield return new("preview-outline-splitter", outlineSplitter);
        foreach (var element in outlineView.Elements()) yield return element;
    }
    private void InitializeOutline()
    {
        previewRoot.ColumnDefinitions.Add(new() { Width = new(0) });
        previewRoot.ColumnDefinitions.Add(new() { Width = new(0) });
        previewRoot.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        previewRoot.Children.Add(outlineView); Grid.SetColumn(outlineSplitter, 1); previewRoot.Children.Add(outlineSplitter);
        Grid.SetColumn(scroll, 2); previewRoot.Children.Add(scroll); Content = previewRoot;
        ControlTheme.Identify(outlineSplitter, "preview-outline-splitter", "Resize document outline; arrows resize and double-click resets width");
        outlineSplitter.ValueChanged += width => { if (!sizingOutline) OutlineWidth = width; };
        outlineSplitter.InteractionStarted += () => dragOutlineWidth = outlineWidth;
        outlineSplitter.InteractionCompleted += cancelled => { if (cancelled) OutlineWidth = dragOutlineWidth; };
        outlineSplitter.ResetRequested += () => OutlineWidth = 220;
        outlineView.CloseRequested += () => OutlineVisible = false;
        outlineView.BookmarkRequested += bookmark =>
        {
            try { NavigateBookmark(bookmark); } catch (Exception error) { Failed?.Invoke(error); }
        };
        outlineView.ViewChanged += () => ViewChanged?.Invoke();
        previewRoot.SizeChanged += (_, _) => ApplyOutlineLayout();
        surface.Loaded += (_, _) => QueueReveal();
        // A deferred target is revisited after actual layout, not an idle timer or
        // repeated application mutation. Once revealed, this hook does no work.
        scroll.LayoutUpdated += (_, _) => { if (pendingReveal is not null) QueueReveal(); };
        ApplyOutlineLayout();
    }
    private void ApplyOutlineLayout()
    {
        if (disposed || previewRoot.ColumnDefinitions.Count == 0) return;
        bool visible = outlineVisible && (previewRoot.ActualWidth <= 0 || previewRoot.ActualWidth >= 424);
        double available = previewRoot.ActualWidth <= 0 ? 640 : Math.Max(160, Math.Min(640, previewRoot.ActualWidth - 264));
        double width = Math.Min(outlineWidth, available);
        sizingOutline = true;
        try
        {
            previewRoot.ColumnDefinitions[0].Width = new(visible ? width : 0);
            previewRoot.ColumnDefinitions[1].Width = new(visible ? 4 : 0);
            outlineView.Visibility = outlineSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            outlineSplitter.Maximum = available; outlineSplitter.Value = width;
        }
        finally { sizingOutline = false; }
        surface.Invalidate(); QueueReveal();
    }
    private void EnsureOutline()
    {
        if (report is null || outlineSnapshot is not null) return;
        var snapshot = new PreparedOutlineSnapshot(report);
        outlineView.SetSnapshot(snapshot); outlineSnapshot = snapshot; OutlineBuildCount++;
    }
    private void ResetOutline()
    {
        pendingReveal = null; SelectedBookmarkKey = null; outlineSnapshot = null; outlineView.SetSnapshot(null);
    }
    /// <summary>Accept only a bookmark instance from the currently bound metadata snapshot; old report targets are rejected.</summary>
    public bool NavigateBookmark(PreparedBookmark bookmark)
    {
        ArgumentNullException.ThrowIfNull(bookmark); ObjectDisposedException.ThrowIf(disposed, this);
        if (!ReferenceEquals(outlineSnapshot?.Find(bookmark.Key), bookmark) || report is null
            || bookmark.PageIndex < 0 || bookmark.PageIndex >= report.Pages.Count) return false;
        ShowTarget(bookmark.PageIndex, bookmark.Bounds);
        SelectedBookmarkKey = bookmark.Key; outlineView.SelectBookmark(bookmark); ViewChanged?.Invoke(); return true;
    }
    private void ShowTarget(int index, RectD bounds)
    {
        if (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) || !double.IsFinite(bounds.Right) || !double.IsFinite(bounds.Bottom)
            || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) || bounds.Width < 0 || bounds.Height < 0)
            throw new ArgumentException("Preview target bounds must be finite with nonnegative size.", nameof(bounds));
        var page = report!.Pages[index]; var clipped = PreparedVisibility.Intersect(bounds, new(0, 0, page.Width, page.Height));
        if (clipped.Width <= 0 || clipped.Height <= 0) return;
        PageIndex = index; surface.Highlight = clipped; pendingReveal = clipped;
        surface.Invalidate(); QueueReveal();
    }
    private void QueueReveal()
    {
        if (disposed || pendingReveal is null || revealQueued) return;
        revealQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            revealQueued = false;
            if (disposed || report is null || pendingReveal is not { } target) return;
            if (!surface.IsLoaded || !scroll.IsLoaded || scroll.ViewportWidth <= 0 || scroll.ViewportHeight <= 0
                || scroll.ExtentWidth <= 0 || scroll.ExtentHeight <= 0
                || Math.Abs(surface.ActualWidth - surface.Width) > 1 || Math.Abs(surface.ActualHeight - surface.Height) > 1) return;
            try
            {
                var origin = surface.TransformToVisual(scroll).TransformPoint(new(0, 0));
                double scale = surface.PointScale;
                var bounds = new RectD(origin.X + target.X * scale, origin.Y + target.Y * scale, target.Width * scale, target.Height * scale);
                var offsets = ReportPreviewReveal.Calculate(bounds, scroll.ViewportWidth, scroll.ViewportHeight,
                    scroll.HorizontalOffset, scroll.VerticalOffset, scroll.ExtentWidth, scroll.ExtentHeight);
                pendingReveal = null;
                scroll.ChangeView(offsets.HorizontalOffset, offsets.VerticalOffset, null, true);
                surface.Invalidate();
            }
            catch (Exception error) { pendingReveal = null; Failed?.Invoke(error); }
        })) revealQueued = false;
    }
    public new void Dispose()
    {
        if (disposed) return; disposed = true; pendingReveal = null; report = null; outlineSnapshot = null;
        outlineView.SetSnapshot(null); surface.ResetPage(); base.Dispose();
    }
}
