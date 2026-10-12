using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ReportScene.Designer;
using ReportScene.Model;
using ReportScene.Rendering.Skia;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;
using Windows.System;

namespace ReportScene.Controls.Uno;

public sealed class ReportDesignView : UserControl, IDisposable
{
    private readonly DesignerCanvas surface;
    private readonly ScrollViewer scroll;
    private readonly DesignerActivityTracker activity;
    private Point start;
    private bool dragging, marquee, additive, disposed;
    private ResizeHandle? resizeHandle;
    private uint? pointerId;
    private string? clickSelection;
    private bool pointerMoved;
    private double zoom = .85;
    private bool showFullPage;
    public bool ShowFullPage { get => showFullPage; set { if (showFullPage == value) return; showFullPage = value; Refresh(); } }
    public DesignerSession Session { get; }
    public ReportRenderer Renderer { get; }
    public bool ShowGrid { get => surface.ShowGrid; set { if (surface.ShowGrid == value) return; surface.ShowGrid = value; surface.Invalidate(); ViewChanged?.Invoke(); } }
    public bool ShowRulers { get => surface.ShowRulers; set { if (surface.ShowRulers == value) return; surface.ShowRulers = value; surface.Invalidate(); ViewChanged?.Invoke(); } }
    public DesignerUnit Unit { get => surface.Unit; set { _ = DesignerGeometry.PointsPerUnit(value); surface.Unit = value; surface.Invalidate(); ViewChanged?.Invoke(); } }
    public double Zoom
    {
        get => zoom;
        set { if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value)); double next = Math.Clamp(value, .15, 4); if (zoom == next) return; zoom = next; SizeSurface(); }
    }
    public double PointScale => Zoom * 96 / 72;
    public DesignLayout Layout => surface.Layout;
    public long RenderCount => surface.RenderCount;
    public TextLayoutCache TextLayouts => surface.TextLayouts;
    public long LayoutBuildCount { get; private set; } = 1;
    public FrameworkElement CanvasElement => surface;
    public FrameworkElement ViewportElement => scroll;
    public RectD LastPaintViewport => surface.LastViewport;
    public int LastDrawnObjectCount => surface.LastDrawnObjectCount;
    public event Action<ReportItem>? EditRequested;
    public event Action<Exception>? Failed;
    public event Action? ViewChanged;
    public event Action? InteractionCompleted;
    public ReportDesignView(DesignerSession session, ReportRenderer renderer)
    {
        Session = session; Renderer = renderer; activity = new(session); IsTabStop = true;
        surface = new(session, renderer, VisibleViewport) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        scroll = new() { Content = surface, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Enabled, Background = ControlTheme.Brush("#C3C3C3") };
        Content = scroll; ControlTheme.Identify(this, "report-design-view", "Report design surface");
        surface.PointerPressed += (_, e) => Guard(() => Pressed(e)); surface.PointerMoved += (_, e) => Guard(() => Moved(e)); surface.PointerReleased += (_, e) => Guard(() => Released(e));
        surface.PointerCanceled += (_, _) => Guard(Cancel); surface.PointerCaptureLost += (_, _) => { if (dragging || marquee) Guard(Cancel); };
        surface.DoubleTapped += (_, e) => Guard(() => { var p = e.GetPosition(surface); var hit = surface.Layout.HitTest(p.X / PointScale, p.Y / PointScale); if (hit is not null) EditRequested?.Invoke(hit.Item); e.Handled = true; });
        surface.PointerWheelChanged += (_, e) => { if ((e.KeyModifiers & VirtualKeyModifiers.Control) != 0) { Zoom *= e.GetCurrentPoint(surface).Properties.MouseWheelDelta > 0 ? 1.1 : 1 / 1.1; e.Handled = true; } };
        // Native layout and scrolling, not a recording canvas clip, define which
        // objects need repainting. No idle polling or model mutation is involved.
        surface.SizeChanged += (_, _) => surface.Invalidate();
        scroll.SizeChanged += (_, _) => surface.Invalidate();
        scroll.ViewChanged += (_, _) => surface.Invalidate();
        surface.RenderFailed += e => Failed?.Invoke(e); KeyDown += OnKeyDown;
        Session.Changed += SessionChanged; Session.SelectionChanged += SelectionChanged; SizeSurface();
    }
    private RectD? VisibleViewport()
    {
        if (!surface.IsLoaded || !scroll.IsLoaded || surface.ActualWidth <= 0 || surface.ActualHeight <= 0
            || scroll.ViewportWidth <= 0 || scroll.ViewportHeight <= 0) return null;
        var origin = surface.TransformToVisual(scroll).TransformPoint(new(0, 0));
        return ReportPreviewViewport.Calculate(new(origin.X, origin.Y, surface.ActualWidth, surface.ActualHeight),
            scroll.ViewportWidth, scroll.ViewportHeight, PointScale);
    }
    private void Guard(Action action) { try { action(); } catch (Exception e) { Failed?.Invoke(e); } }
    private void SessionChanged(object? sender, EventArgs e)
    {
        var change = activity.Observe(Session);
        if (change.InvalidatePrepared || !ReferenceEquals(surface.Layout.Page, Session.Page)) Refresh();
        else ViewChanged?.Invoke();
    }
    private void SelectionChanged(object? sender, EventArgs e) { surface.Invalidate(); ViewChanged?.Invoke(); }
    public void Refresh()
    {
        surface.Layout = DesignLayout.Create(Session.Page, ShowFullPage); LayoutBuildCount++; SizeSurface();
    }
    private void SizeSurface()
    {
        surface.PointScale = PointScale;
        surface.Width = surface.Layout.Width * PointScale; surface.Height = surface.Layout.Height * PointScale;
        surface.Invalidate(); ViewChanged?.Invoke();
    }
    public void FitWidth() { if (scroll.ViewportWidth > 0) Zoom = Math.Max(.15, (scroll.ViewportWidth - 20) / surface.Layout.Width / (96d / 72)); }
    public void ScrollToSelection()
    {
        var item = surface.Layout.Objects.FirstOrDefault(i => Session.SelectedIds.Contains(i.Item.Id));
        if (item is not null) scroll.ChangeView(Math.Max(0, item.Bounds.X * PointScale - 80), Math.Max(0, item.Bounds.Y * PointScale - 80), null, true);
    }
    private void Pressed(PointerRoutedEventArgs e)
    {
        if (pointerId is not null) return;
        var pointer = e.GetCurrentPoint(surface);
        if (!pointer.Properties.IsLeftButtonPressed && pointer.PointerDevice.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Mouse) return;
        Focus(FocusState.Pointer); start = new(pointer.Position.X / PointScale, pointer.Position.Y / PointScale);
        clickSelection = null; pointerMoved = false;
        additive = (e.KeyModifiers & (VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift)) != 0;
        resizeHandle = surface.Layout.HitSelectedHandle(Session.SelectedIds, start.X, start.Y, 5 / PointScale)?.Handle;
        if (resizeHandle is not null) { Session.BeginDrag(); dragging = Session.HasActiveDrag; }
        else if (surface.Layout.HitTest(start.X, start.Y) is { } hit)
        {
            if (!additive && Session.SelectedIds.Count > 1 && Session.SelectedIds.Contains(hit.Item.Id)) clickSelection = hit.Item.Id;
            if (additive || !Session.SelectedIds.Contains(hit.Item.Id)) Session.Select(hit.Item.Id, additive);
            Session.BeginDrag(); dragging = Session.HasActiveDrag;
        }
        else
        {
            var band = surface.Layout.BandAt(start.X, start.Y);
            if (!additive && band is not null) Session.SelectBand(band.Band.Id); else if (!additive) Session.Select(null);
            if (start.X > DesignLayout.BandPanelWidth) { marquee = true; surface.Marquee = new(start.X, start.Y, 0, 0); }
        }
        if (surface.CapturePointer(e.Pointer)) pointerId = e.Pointer.PointerId;
        else Cancel();
        e.Handled = true;
    }
    private void Moved(PointerRoutedEventArgs e)
    {
        if (pointerId != e.Pointer.PointerId || !dragging && !marquee) return;
        var p = e.GetCurrentPoint(surface).Position; double x = p.X / PointScale, y = p.Y / PointScale;
        pointerMoved |= Math.Abs(x - start.X) * PointScale > 3 || Math.Abs(y - start.Y) * PointScale > 3;
        if (dragging)
        {
            if (resizeHandle is { } handle) Session.PreviewResize(x - start.X, y - start.Y, handle);
            else if (pointerMoved) Session.PreviewDrag(x - start.X, y - start.Y);
        }
        else { surface.Marquee = new(Math.Min(start.X, x), Math.Min(start.Y, y), Math.Abs(x - start.X), Math.Abs(y - start.Y)); surface.Invalidate(); }
        e.Handled = true;
    }
    private void Released(PointerRoutedEventArgs e)
    {
        if (pointerId != e.Pointer.PointerId) return;
        if (dragging) Session.CommitDrag();
        if (!pointerMoved && clickSelection is { } clicked) Session.Select(clicked);
        clickSelection = null;
        if (marquee && surface.Marquee is { } box && box.Width > 1 && box.Height > 1) Session.SelectMany(surface.Layout.Intersect(box).Select(o => o.Item.Id), additive);
        dragging = marquee = false; resizeHandle = null; pointerId = null; surface.Marquee = null;
        surface.ReleasePointerCapture(e.Pointer); surface.Invalidate(); InteractionCompleted?.Invoke(); ViewChanged?.Invoke(); e.Handled = true;
    }
    private void Cancel()
    {
        Session.CancelDrag(); clickSelection = null; dragging = marquee = false; resizeHandle = null; pointerId = null; surface.Marquee = null;
        surface.ReleasePointerCaptures(); surface.Invalidate(); InteractionCompleted?.Invoke(); ViewChanged?.Invoke();
    }
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        try
        {
            switch (e.Key)
            {
                case VirtualKey.Delete: Session.DeleteSelection(); break;
                case VirtualKey.Escape: Cancel(); Session.Select(null); break;
                case VirtualKey.Left: Session.Move(-1, 0); break;
                case VirtualKey.Right: Session.Move(1, 0); break;
                case VirtualKey.Up: Session.Move(0, -1); break;
                case VirtualKey.Down: Session.Move(0, 1); break;
                case VirtualKey.Enter: if (Session.Selection.FirstOrDefault() is { } item) EditRequested?.Invoke(item); break;
                default: return;
            }
            e.Handled = true;
        }
        catch (Exception ex) { Failed?.Invoke(ex); }
    }
    public new void Dispose()
    {
        if (disposed) return; disposed = true;
        Session.Changed -= SessionChanged; Session.SelectionChanged -= SelectionChanged;
        surface.ReleasePointerCaptures(); surface.TextLayouts.Clear(); base.Dispose();
    }
    private sealed class DesignerCanvas(DesignerSession session, ReportRenderer renderer, Func<RectD?> visibleViewport) : SKCanvasElement
    {
        private static readonly ResizeHandle[] SelectionHandles = Enum.GetValues<ResizeHandle>();
        public DesignLayout Layout { get; set; } = DesignLayout.Create(session.Page, false);
        public TextLayoutCache TextLayouts { get; } = new(renderer.Metrics);
        public double PointScale { get; set; } = 1;
        public bool ShowGrid { get; set; } = true;
        public bool ShowRulers { get; set; } = true;
        public DesignerUnit Unit { get; set; } = DesignerUnit.Points;
        public RectD? Marquee { get; set; }
        public long RenderCount { get; private set; }
        public RectD LastViewport { get; private set; }
        public int LastDrawnObjectCount { get; private set; }
        public event Action<Exception>? RenderFailed;
        private string? lastError;
        protected override void RenderOverride(SKCanvas canvas, Size area)
        {
            canvas.Save(); canvas.Scale((float)PointScale); RenderCount++; LastDrawnObjectCount = 0;
            using var paint = new SKPaint { IsAntialias = false };
            using var dash = SKPathEffect.CreateDash([2, 2], 0);
            try
            {
                var paper = Layout.Paper;
                LastViewport = visibleViewport() ?? new RectD(0, 0, Layout.Width, Layout.Height);
                var viewport = ReportRenderer.Rect(LastViewport);
                bool Visible(DesignObject entry) => DesignerPaintCulling.IsVisible(entry, LastViewport, PointScale);
                paint.Color = ReportRenderer.Color("#989898"); canvas.DrawRect(ReportRenderer.Rect(paper.Offset(3, 3)), paint);
                paint.Color = SKColors.White; canvas.DrawRect(ReportRenderer.Rect(paper), paint);
                if (ShowRulers) DrawRulers(canvas, paint, viewport);
                foreach (var band in Layout.Bands)
                {
                    if (band.Bounds.Bottom < viewport.Top || band.Bounds.Y > viewport.Bottom) continue;
                    paint.Color = ReportRenderer.Color(band.Band.Kind == BandKind.Data ? "#F2C69C" : "#E7E7E7"); canvas.DrawRect(ReportRenderer.Rect(band.LabelBounds), paint);
                    if (session.BandId == band.Band.Id) { paint.Color = ReportRenderer.Color("#409AD3"); canvas.DrawRect((float)band.LabelBounds.X, (float)band.LabelBounds.Y, 3, (float)band.LabelBounds.Height, paint); }
                    string kind = band.Band.Kind.ToString();
                    renderer.DrawText(canvas, kind, new(7, band.LabelBounds.Y + 3, 100, 16), new() { FontSize = 8, Bold = true, Padding = 0, Foreground = "#333333" });
                    if (band.LabelBounds.Height > 31) renderer.DrawText(canvas, band.Band.Name, new(7, band.LabelBounds.Y + 18, 100, 14), new() { FontSize = 7, Padding = 0, Foreground = "#666666" });
                    paint.Color = ReportRenderer.Color("#A4A4A4"); paint.StrokeWidth = (float)(1 / PointScale); canvas.DrawLine(0, (float)band.Bounds.Bottom, (float)paper.Right, (float)band.Bounds.Bottom, paint);
                    if (ShowGrid)
                    {
                        paint.Color = ReportRenderer.Color("#D6D6D6");
                        double spacing = double.IsFinite(session.GridSize) ? Math.Max(4, session.GridSize) : 4;
                        double firstX = band.Bounds.X + Math.Max(0, Math.Ceiling((viewport.Left - band.Bounds.X) / spacing)) * spacing;
                        double firstY = band.Bounds.Y + Math.Max(0, Math.Ceiling((viewport.Top - band.Bounds.Y) / spacing)) * spacing;
                        for (double y = firstY; y < Math.Min(band.Bounds.Bottom, viewport.Bottom); y += spacing)
                            for (double x = firstX; x < Math.Min(band.Bounds.Right, viewport.Right); x += spacing) canvas.DrawPoint((float)x, (float)y, paint);
                    }
                }
                foreach (var entry in Layout.Objects)
                {
                    if (!Visible(entry)) continue;
                    LastDrawnObjectCount++;
                    string text = entry.Item switch { TextItem t => TextLayouts.GetWrappedText(t.Text, entry.Bounds.Width - t.Style.Padding * 2, t.Style), BarcodeItem b => b.Text, CheckBoxItem c => c.Expression, _ => "" };
                    try { renderer.DrawItem(canvas, new(entry.Item, entry.Bounds, text), true); }
                    catch (ReportRenderException error)
                    {
                        paint.Color = ReportRenderer.Color("#FFF0ED"); canvas.DrawRect(ReportRenderer.Rect(entry.Bounds), paint);
                        renderer.DrawText(canvas, error.Message, entry.Bounds, new() { FontSize = 8, Foreground = "#A3372E" });
                    }
                    paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = (float)(1 / PointScale); paint.Color = ReportRenderer.Color("#B3B3B3");
                    paint.PathEffect = dash; canvas.DrawRect(ReportRenderer.Rect(entry.Bounds), paint); paint.PathEffect = null; paint.Style = SKPaintStyle.Fill;
                }
                if (session.SelectedIds.Count > 0) foreach (var entry in Layout.Objects)
                {
                    if (!session.SelectedIds.Contains(entry.Item.Id) || !Visible(entry)) continue;
                    var color = ReportRenderer.Color(entry.Item.Locked ? "#858585" : "#2374B0");
                    paint.Color = color; paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = (float)(1 / PointScale); canvas.DrawRect(ReportRenderer.Rect(entry.Bounds), paint);
                    double size = 6 / PointScale;
                    foreach (var handle in SelectionHandles)
                    {
                        var p = DesignerGeometry.HandlePoint(entry.Bounds, handle); var rect = new SKRect((float)(p.X - size / 2), (float)(p.Y - size / 2), (float)(p.X + size / 2), (float)(p.Y + size / 2));
                        paint.Style = SKPaintStyle.Fill; paint.Color = SKColors.White; canvas.DrawRect(rect, paint);
                        paint.Style = SKPaintStyle.Stroke; paint.Color = color; canvas.DrawRect(rect, paint);
                    }
                    paint.Style = SKPaintStyle.Fill;
                }
                if (Marquee is { } box) { paint.Color = ReportRenderer.Color("#303976B4"); canvas.DrawRect(ReportRenderer.Rect(box), paint); paint.Style = SKPaintStyle.Stroke; paint.Color = ReportRenderer.Color("#3976B4"); canvas.DrawRect(ReportRenderer.Rect(box), paint); }
                lastError = null;
            }
            catch (Exception e) { if (lastError != e.Message) { lastError = e.Message; RenderFailed?.Invoke(e); } }
            finally { canvas.Restore(); }
        }
        private void DrawRulers(SKCanvas canvas, SKPaint paint, SKRect viewport)
        {
            paint.Color = ReportRenderer.Color("#F0F0F0");
            canvas.DrawRect((float)Layout.Paper.X, 0, (float)Layout.Paper.Width, (float)DesignLayout.RulerSize, paint);
            canvas.DrawRect((float)DesignLayout.BandPanelWidth, 0, (float)DesignLayout.RulerSize, (float)Layout.Height, paint);
            var style = new ItemStyle { FontSize = 7, Foreground = "#626262", Padding = 0 };
            renderer.DrawText(canvas, DesignerGeometry.UnitLabel(Unit), new(DesignLayout.BandPanelWidth + 1, 2, 18, 12), style);
            double unitsPerTick = Unit switch { DesignerUnit.Points => 6, DesignerUnit.Millimeters => 1, DesignerUnit.Centimeters => .1, _ => .1 };
            double spacing = unitsPerTick * DesignerGeometry.PointsPerUnit(Unit);
            while (spacing * PointScale < 4) { spacing *= 2; unitsPerTick *= 2; }
            void Axis(bool vertical)
            {
                double origin = vertical ? Layout.Paper.Y : Layout.Paper.X, length = vertical ? Layout.Paper.Height : Layout.Paper.Width;
                double begin = vertical ? viewport.Top : viewport.Left, end = vertical ? viewport.Bottom : viewport.Right;
                int first = Math.Max(0, (int)Math.Floor((begin - origin) / spacing));
                int last = (int)Math.Min(Math.Ceiling(length / spacing), Math.Ceiling((end - origin) / spacing));
                for (int tick = first; tick <= last; tick++)
                {
                    bool major = tick % 5 == 0; float coordinate = (float)(origin + tick * spacing);
                    paint.Color = ReportRenderer.Color("#8B8B8B"); paint.StrokeWidth = (float)(1 / PointScale);
                    if (vertical) canvas.DrawLine((float)Layout.Paper.X - (major ? 7 : 3), coordinate, (float)Layout.Paper.X, coordinate, paint);
                    else canvas.DrawLine(coordinate, (float)DesignLayout.RulerSize - (major ? 7 : 3), coordinate, (float)DesignLayout.RulerSize, paint);
                    if (major) renderer.DrawText(canvas, (tick * unitsPerTick).ToString("0.##", CultureInfo.InvariantCulture), vertical ? new(DesignLayout.BandPanelWidth + 1, coordinate + 2, 18, 11) : new(coordinate + 2, 1, 32, 10), vertical ? style with { FontSize = 6 } : style);
                }
            }
            if (viewport.Top <= DesignLayout.RulerSize) Axis(false);
            if (viewport.Left <= Layout.Paper.X) Axis(true);
        }
    }
}
