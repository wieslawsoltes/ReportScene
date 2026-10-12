using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ReportScene.Model;
using ReportScene.Rendering.Skia;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;
using Windows.System;

namespace ReportScene.Controls.Uno;

/// <summary>Read-only paint/viewport evidence. A render callback is not a presentation acknowledgement.</summary>
public readonly record struct PreviewPaintSnapshot(double AreaWidth, double AreaHeight, double PointScale, RectD Clip, RectD Paper,
    double ScaleX, double ScaleY, double TranslateX, double TranslateY);
public readonly record struct PreviewViewportSnapshot(double Width, double Height, double ExtentWidth, double ExtentHeight,
    double HorizontalOffset, double VerticalOffset, string FocusOwner, PreviewPaintSnapshot Paint);

/// <summary>Only the current page and its immutable spatial index are retained by the preview surface.</summary>
public sealed class ReportPreviewView : UserControl
{
    private readonly PreviewCanvas surface;
    private readonly ScrollViewer scroll;
    private PreparedReport? report;
    private int pageIndex;
    private double zoom = 1;
    public PreparedReport? Report
    {
        get => report;
        set { report = value; pageIndex = 0; surface.ResetPage(); Refresh(); scroll.ChangeView(0, 0, null, true); }
    }
    public int PageIndex
    {
        get => pageIndex;
        set
        {
            int next = new ReportPagePosition(report?.Pages.Count ?? 0, value).Index;
            if (next == pageIndex) return;
            pageIndex = next; surface.Highlight = null; Refresh(); scroll.ChangeView(0, 0, null, true);
        }
    }
    public ReportPagePosition Position => new(report?.Pages.Count ?? 0, pageIndex);
    public double Zoom
    {
        get => zoom;
        set { if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value)); double next = Math.Clamp(value, ReportPreviewFit.MinimumZoom, ReportPreviewFit.MaximumZoom); if (next == zoom) return; zoom = next; Refresh(); }
    }
    public long RenderCount => surface.RenderCount;
    public long IndexBuildCount => surface.IndexBuildCount;
    public int LastCandidatesExamined => surface.LastCandidatesExamined;
    public FrameworkElement CanvasElement => surface;
    public FrameworkElement ViewportElement => scroll;
    public PreviewViewportSnapshot ViewportSnapshot => new(scroll.ViewportWidth, scroll.ViewportHeight, scroll.ExtentWidth, scroll.ExtentHeight,
        scroll.HorizontalOffset, scroll.VerticalOffset, XamlRoot is null ? "" : FocusManager.GetFocusedElement(XamlRoot)?.GetType().Name ?? "", surface.LastPaint);
    public event Action? ViewChanged;
    public event Action<Exception>? Failed;
    public ReportPreviewView(ReportRenderer renderer)
    {
        IsTabStop = true;
        surface = new(renderer) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new(16) };
        surface.Failed += e => Failed?.Invoke(e);
        scroll = new() { Content = surface, IsTabStop = true, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled, Background = ControlTheme.Brush("#AAB2BA") };
        surface.PointerPressed += (_, e) =>
        {
            var pointer = e.GetCurrentPoint(surface);
            if (pointer.Properties.IsLeftButtonPressed || pointer.PointerDevice.PointerDeviceType != Windows.Devices.Input.PointerDeviceType.Mouse) scroll.Focus(FocusState.Pointer);
        };
        // A new point scale is assigned before the asynchronous layout pass. Repaint
        // again with the final element/viewport clip, including scrolling and resize.
        surface.SizeChanged += (_, _) => surface.Invalidate();
        scroll.SizeChanged += (_, _) => surface.Invalidate();
        scroll.ViewChanged += (_, _) => surface.Invalidate();
        Content = scroll; ControlTheme.Identify(this, "report-preview-view", "Prepared report preview; Page Up/Down changes pages, Ctrl+Home/End jumps to first/last");
        // Tunnelling runs before the ScrollViewer's class handler consumes paging keys.
        // It is scoped to this preview, never the sibling page-entry/search TextBoxes.
        PreviewKeyDown += HandleNavigationKeys;
    }
    public void Navigate(ReportPageCommand command) => PageIndex = Position.Move(command).Index;
    public void FitWidth() => Fit(ReportFitMode.Width);
    public void FitPage() => Fit(ReportFitMode.WholePage);
    private void Fit(ReportFitMode mode)
    {
        if (report?.Pages.ElementAtOrDefault(pageIndex) is { } page && ReportPreviewFit.Calculate(page.Width, page.Height, scroll.ViewportWidth, scroll.ViewportHeight, mode) is { } fitted)
        {
            Zoom = fitted;
            if (mode == ReportFitMode.WholePage) scroll.ChangeView(0, 0, null, true);
        }
    }
    private void HandleNavigationKeys(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || e.OriginalSource is TextBox) return;
        bool ctrl = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        ReportPageCommand? command = e.Key switch
        {
            VirtualKey.PageUp => ReportPageCommand.Previous, VirtualKey.PageDown => ReportPageCommand.Next,
            VirtualKey.Home when ctrl => ReportPageCommand.First, VirtualKey.End when ctrl => ReportPageCommand.Last, _ => null
        };
        if (command is null) return;
        e.Handled = true;
        try { Navigate(command.Value); } catch (Exception error) { Failed?.Invoke(error); }
    }
    public void Refresh()
    {
        surface.SetPage(report?.Pages.ElementAtOrDefault(pageIndex)); surface.PointScale = Zoom * 96 / 72;
        if (surface.Page is { } page) { surface.Width = page.Width * surface.PointScale; surface.Height = page.Height * surface.PointScale; surface.Visibility = Visibility.Visible; }
        else { surface.Width = surface.Height = 0; surface.Visibility = Visibility.Collapsed; }
        surface.Invalidate(); ViewChanged?.Invoke();
    }
    public void Highlight(SearchHit? hit)
    {
        if (hit is not null)
        {
            // Search identifies physical page metadata, while navigation positions are
            // ordinal within the displayed list (which may be a selected page range).
            int index = -1;
            if (report is not null) for (int n = 0; n < report.Pages.Count; n++) if (report.Pages[n].Number == hit.Page) { index = n; break; }
            if (index < 0) { surface.Highlight = null; surface.Invalidate(); return; }
            PageIndex = index;
        }
        surface.Highlight = hit?.Bounds; surface.Invalidate();
        if (hit is not null) scroll.ChangeView(null, Math.Max(0, hit.Bounds.Y * surface.PointScale - 100), null);
    }
    private sealed class PreviewCanvas(ReportRenderer renderer) : SKCanvasElement
    {
        private PreparedPage? sourcePage;
        private PreparedPageIndex? index;
        public PreparedPage? Page => index?.Page;
        public RectD? Highlight { get; set; }
        public double PointScale { get; set; }
        public long RenderCount { get; private set; }
        public long IndexBuildCount { get; private set; }
        public int LastCandidatesExamined { get; private set; }
        public PreviewPaintSnapshot LastPaint { get; private set; }
        public event Action<Exception>? Failed;
        private string? lastError;
        public void ResetPage() { sourcePage = null; index = null; Highlight = null; LastCandidatesExamined = 0; lastError = null; LastPaint = default; }
        public void SetPage(PreparedPage? page)
        {
            if (ReferenceEquals(sourcePage, page)) return;
            ResetPage();
            if (page is null) return;
            var created = new PreparedPageIndex(page); sourcePage = page; index = created; IndexBuildCount++;
        }
        protected override void RenderOverride(SKCanvas canvas, Size area)
        {
            if (index is null) return;
            canvas.Save(); canvas.Scale((float)PointScale); RenderCount++;
            try
            {
                var clip = canvas.LocalClipBounds; var matrix = canvas.TotalMatrix;
                LastPaint = new(area.Width, area.Height, PointScale, new(clip.Left, clip.Top, clip.Width, clip.Height),
                    new(0, 0, index.Page.Width, index.Page.Height), matrix.ScaleX, matrix.ScaleY, matrix.TransX, matrix.TransY);
                LastCandidatesExamined = renderer.DrawIndexedPage(canvas, index, new(clip.Left, clip.Top, clip.Width, clip.Height));
                if (Highlight is { } bounds) { using var paint = new SKPaint { Color = new(255, 211, 58, 115) }; canvas.DrawRect(ReportRenderer.Rect(bounds), paint); }
                lastError = null;
            }
            catch (Exception e) { if (lastError != e.Message) { lastError = e.Message; Failed?.Invoke(e); } }
            finally { canvas.Restore(); }
        }
    }
}
