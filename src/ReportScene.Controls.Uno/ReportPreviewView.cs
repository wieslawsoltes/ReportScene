using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReportScene.Model;
using ReportScene.Rendering.Skia;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace ReportScene.Controls.Uno;

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
        set { report = value; pageIndex = 0; surface.ResetPage(); Refresh(); scroll.ChangeView(0, 0, null); }
    }
    public int PageIndex
    {
        get => pageIndex;
        set
        {
            int next = Math.Clamp(value, 0, Math.Max(0, (report?.Pages.Count ?? 1) - 1));
            if (next == pageIndex) return;
            pageIndex = next; surface.Highlight = null; Refresh(); scroll.ChangeView(0, 0, null);
        }
    }
    public double Zoom
    {
        get => zoom;
        set { if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value)); double next = Math.Clamp(value, .1, 4); if (next == zoom) return; zoom = next; Refresh(); }
    }
    public long RenderCount => surface.RenderCount;
    public long IndexBuildCount => surface.IndexBuildCount;
    public int LastCandidatesExamined => surface.LastCandidatesExamined;
    public FrameworkElement CanvasElement => surface;
    public event Action? ViewChanged;
    public event Action<Exception>? Failed;
    public ReportPreviewView(ReportRenderer renderer)
    {
        surface = new(renderer) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new(16) };
        surface.Failed += e => Failed?.Invoke(e);
        scroll = new() { Content = surface, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled, Background = ControlTheme.Brush("#AAB2BA") };
        Content = scroll; ControlTheme.Identify(this, "report-preview-view", "Prepared report preview");
    }
    public void FitWidth()
    {
        if (report?.Pages.ElementAtOrDefault(pageIndex) is { } page && scroll.ViewportWidth > 40) Zoom = (scroll.ViewportWidth - 40) / page.Width / (96d / 72);
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
            // Prepared page numbers are metadata, not guaranteed to equal list indices for
            // hosts displaying a selected page range or a reloaded prepared report.
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
        public event Action<Exception>? Failed;
        private string? lastError;
        public void ResetPage() { sourcePage = null; index = null; Highlight = null; LastCandidatesExamined = 0; lastError = null; }
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
                var clip = canvas.LocalClipBounds;
                LastCandidatesExamined = renderer.DrawIndexedPage(canvas, index, new(clip.Left, clip.Top, clip.Width, clip.Height));
                if (Highlight is { } bounds) { using var paint = new SKPaint { Color = new(255, 211, 58, 115) }; canvas.DrawRect(ReportRenderer.Rect(bounds), paint); }
                lastError = null;
            }
            catch (Exception e) { if (lastError != e.Message) { lastError = e.Message; Failed?.Invoke(e); } }
            finally { canvas.Restore(); }
        }
    }
}
