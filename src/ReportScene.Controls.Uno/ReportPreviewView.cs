using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReportScene.Model;
using ReportScene.Rendering.Skia;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace ReportScene.Controls.Uno;

/// <summary>Only the current page is realized; prepared reports with thousands of pages do not create thousands of UI elements.</summary>
public sealed class ReportPreviewView : UserControl
{
    private readonly PreviewCanvas surface;
    private readonly ScrollViewer scroll;
    private PreparedReport? report;
    private int pageIndex;
    private double zoom = 1;
    public PreparedReport? Report { get => report; set { report = value; pageIndex = 0; Refresh(); } }
    public int PageIndex { get => pageIndex; set { pageIndex = Math.Clamp(value, 0, Math.Max(0, (report?.Pages.Count ?? 1) - 1)); Refresh(); } }
    public double Zoom { get => zoom; set { zoom = Math.Clamp(value, .1, 4); Refresh(); } }
    public long RenderCount => surface.RenderCount;
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
    public void FitWidth() { if (report is not null && scroll.ViewportWidth > 0) Zoom = (scroll.ViewportWidth - 40) / report.Pages[pageIndex].Width / (96d / 72); }
    public void Refresh()
    {
        surface.Page = report?.Pages.ElementAtOrDefault(pageIndex); surface.Scale = Zoom * 96 / 72;
        if (surface.Page is { } page) { surface.Width = page.Width * surface.Scale; surface.Height = page.Height * surface.Scale; }
        surface.Invalidate(); ViewChanged?.Invoke();
    }
    public void Highlight(SearchHit? hit)
    {
        if (hit is not null) PageIndex = hit.Page - 1;
        surface.Highlight = hit?.Bounds; surface.Invalidate();
        if (hit is not null) scroll.ChangeView(null, Math.Max(0, hit.Bounds.Y * surface.Scale - 100), null);
    }
    private sealed class PreviewCanvas(ReportRenderer renderer) : SKCanvasElement
    {
        public PreparedPage? Page { get; set; }
        public RectD? Highlight { get; set; }
        public double Scale { get; set; }
        public long RenderCount { get; private set; }
        public event Action<Exception>? Failed;
        private string? lastError;
        protected override void RenderOverride(SKCanvas canvas, Size area)
        {
            if (Page is null) return;
            canvas.Save(); canvas.Scale((float)Scale); RenderCount++;
            try
            {
                var clip = canvas.LocalClipBounds;
                renderer.DrawPage(canvas, Page, new(clip.Left, clip.Top, clip.Width, clip.Height));
                if (Highlight is { } bounds) { using var paint = new SKPaint { Color = new(255, 211, 58, 115) }; canvas.DrawRect(ReportRenderer.Rect(bounds), paint); }
                lastError = null;
            }
            catch (Exception e) { if (lastError != e.Message) { lastError = e.Message; Failed?.Invoke(e); } }
            finally { canvas.Restore(); }
        }
    }
}
