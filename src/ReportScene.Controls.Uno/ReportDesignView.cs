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
    private Point start;
    private bool dragging, resizing, marquee, additive;
    private double zoom = .85;
    public DesignerSession Session { get; }
    public ReportRenderer Renderer { get; }
    public bool ShowGrid { get => surface.ShowGrid; set { surface.ShowGrid = value; surface.Invalidate(); } }
    public double Zoom { get => zoom; set { zoom = Math.Clamp(value, .15, 4); Refresh(); } }
    public double PointScale => Zoom * 96 / 72;
    public DesignLayout Layout => surface.Layout;
    public long RenderCount => surface.RenderCount;
    public FrameworkElement CanvasElement => surface;
    public event Action<ReportItem>? EditRequested;
    public event Action<Exception>? Failed;
    public event Action? ViewChanged;
    public ReportDesignView(DesignerSession session, ReportRenderer renderer)
    {
        Session = session; Renderer = renderer; IsTabStop = true;
        surface = new(session, renderer) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        scroll = new() { Content = surface, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Enabled, Background = ControlTheme.Brush("#B7BEC5") };
        Content = scroll;
        ControlTheme.Identify(this, "report-design-view", "Report design surface");
        surface.PointerPressed += Pressed; surface.PointerMoved += Moved; surface.PointerReleased += Released;
        surface.PointerCanceled += (_, _) => Cancel(); surface.PointerCaptureLost += (_, _) => { if (dragging || marquee) Cancel(); };
        surface.DoubleTapped += (_, e) => { var p = e.GetPosition(surface); var hit = surface.Layout.HitTest(p.X / PointScale, p.Y / PointScale); if (hit is not null) EditRequested?.Invoke(hit.Item); e.Handled = true; };
        surface.PointerWheelChanged += (_, e) => { if ((e.KeyModifiers & VirtualKeyModifiers.Control) != 0) { Zoom *= e.GetCurrentPoint(surface).Properties.MouseWheelDelta > 0 ? 1.1 : 1 / 1.1; e.Handled = true; } };
        surface.RenderFailed += e => Failed?.Invoke(e);
        KeyDown += OnKeyDown;
        Session.Changed += SessionChanged; Session.SelectionChanged += SelectionChanged;
        Refresh();
    }
    private void SessionChanged(object? sender, EventArgs e) => Refresh();
    private void SelectionChanged(object? sender, EventArgs e) { surface.Invalidate(); ViewChanged?.Invoke(); }
    public void Refresh()
    {
        surface.Layout = DesignLayout.Create(Session.Page); surface.PointScale = PointScale;
        surface.Width = surface.Layout.Width * PointScale; surface.Height = surface.Layout.Height * PointScale;
        surface.Invalidate(); ViewChanged?.Invoke();
    }
    public void FitWidth() { if (scroll.ViewportWidth > 0) Zoom = Math.Max(.15, (scroll.ViewportWidth - 20) / surface.Layout.Width / (96d / 72)); }
    public void ScrollToSelection()
    {
        var item = surface.Layout.Objects.FirstOrDefault(i => Session.SelectedIds.Contains(i.Item.Id));
        if (item is not null) scroll.ChangeView(Math.Max(0, item.Bounds.X * PointScale - 80), Math.Max(0, item.Bounds.Y * PointScale - 80), null);
    }
    private void Pressed(object sender, PointerRoutedEventArgs e)
    {
        var pointer = e.GetCurrentPoint(surface);
        if (!pointer.Properties.IsLeftButtonPressed && pointer.PointerDevice.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Mouse) return;
        Focus(FocusState.Pointer);
        start = new(pointer.Position.X / PointScale, pointer.Position.Y / PointScale);
        additive = (e.KeyModifiers & VirtualKeyModifiers.Control) != 0;
        var hit = surface.Layout.HitTest(start.X, start.Y);
        var selected = surface.Layout.Objects.LastOrDefault(o => Session.SelectedIds.Contains(o.Item.Id) && !o.Item.Locked);
        resizing = selected is not null && Math.Abs(start.X - selected.Bounds.Right) < 7 / PointScale && Math.Abs(start.Y - selected.Bounds.Bottom) < 7 / PointScale;
        if (resizing) { dragging = true; Session.BeginDrag(); }
        else if (hit is not null)
        {
            if (additive || !Session.SelectedIds.Contains(hit.Item.Id)) Session.Select(hit.Item.Id, additive);
            dragging = true; Session.BeginDrag();
        }
        else
        {
            var band = surface.Layout.BandAt(start.X, start.Y);
            if (!additive && band is not null) Session.SelectBand(band.Band.Id); else if (!additive) Session.Select(null);
            if (start.X > DesignLayout.BandPanelWidth) { marquee = true; surface.Marquee = new(start.X, start.Y, 0, 0); }
        }
        surface.CapturePointer(e.Pointer); e.Handled = true;
    }
    private void Moved(object sender, PointerRoutedEventArgs e)
    {
        if (!dragging && !marquee) return;
        var p = e.GetCurrentPoint(surface).Position; double x = p.X / PointScale, y = p.Y / PointScale;
        if (dragging) Session.PreviewDrag(x - start.X, y - start.Y, resizing);
        else { surface.Marquee = new(Math.Min(start.X, x), Math.Min(start.Y, y), Math.Abs(x - start.X), Math.Abs(y - start.Y)); surface.Invalidate(); }
        e.Handled = true;
    }
    private void Released(object sender, PointerRoutedEventArgs e)
    {
        if (dragging) Session.CommitDrag();
        if (marquee && surface.Marquee is { } box && box.Width > 1 && box.Height > 1) Session.SelectMany(surface.Layout.Intersect(box).Select(o => o.Item.Id), additive);
        dragging = marquee = resizing = false; surface.Marquee = null; surface.ReleasePointerCapture(e.Pointer); surface.Invalidate(); e.Handled = true;
    }
    private void Cancel() { Session.CancelDrag(); dragging = marquee = resizing = false; surface.Marquee = null; surface.Invalidate(); }
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
    public new void Dispose() { Session.Changed -= SessionChanged; Session.SelectionChanged -= SelectionChanged; base.Dispose(); }

    private sealed class DesignerCanvas(DesignerSession session, ReportRenderer renderer) : SKCanvasElement
    {
        public DesignLayout Layout { get; set; } = DesignLayout.Create(session.Page);
        public double PointScale { get; set; } = 1;
        public bool ShowGrid { get; set; } = true;
        public RectD? Marquee { get; set; }
        public long RenderCount { get; private set; }
        public event Action<Exception>? RenderFailed;
        private string? lastError;
        protected override void RenderOverride(SKCanvas canvas, Size area)
        {
            canvas.Save(); canvas.Scale((float)PointScale); RenderCount++;
            using var paint = new SKPaint { IsAntialias = false };
            try
            {
                var paper = Layout.Paper; var viewport = canvas.LocalClipBounds;
                paint.Color = ReportRenderer.Color("#8C959E"); canvas.DrawRect(ReportRenderer.Rect(paper.Offset(3, 3)), paint);
                paint.Color = SKColors.White; canvas.DrawRect(ReportRenderer.Rect(paper), paint);
                DrawRulers(canvas, paint);
                foreach (var band in Layout.Bands)
                {
                    if (band.Bounds.Bottom < viewport.Top || band.Bounds.Y > viewport.Bottom) continue;
                    paint.Color = ReportRenderer.Color(band.Band.Kind == BandKind.Data ? "#EFC28E" : "#DEE3E8"); canvas.DrawRect(ReportRenderer.Rect(band.LabelBounds), paint);
                    if (session.BandId == band.Band.Id) { paint.Color = ReportRenderer.Color("#C2D7EB"); canvas.DrawRect((float)band.LabelBounds.X, (float)band.LabelBounds.Y, 4, (float)band.LabelBounds.Height, paint); }
                    renderer.DrawText(canvas, band.Band.Kind.ToString(), new(7, band.LabelBounds.Y + 3, 100, 16), new() { FontSize = 8, Bold = true, Padding = 0, Foreground = "#354454" });
                    if (band.LabelBounds.Height > 31) renderer.DrawText(canvas, band.Band.Name, new(7, band.LabelBounds.Y + 18, 100, 14), new() { FontSize = 7, Padding = 0, Foreground = "#667380" });
                    paint.Color = ReportRenderer.Color("#8CA1B4"); paint.StrokeWidth = (float)(1 / PointScale); canvas.DrawLine(0, (float)band.Bounds.Bottom, (float)paper.Right, (float)band.Bounds.Bottom, paint);
                    if (ShowGrid)
                    {
                        paint.Color = ReportRenderer.Color("#CBD0D5");
                        double spacing = Math.Max(4, session.GridSize), left = band.Bounds.X, top = band.Bounds.Y;
                        int firstY = Math.Max(0, (int)((viewport.Top - top) / spacing));
                        for (double y = top + firstY * spacing; y < Math.Min(band.Bounds.Bottom, viewport.Bottom); y += spacing)
                            for (double x = left; x < band.Bounds.Right; x += spacing) canvas.DrawPoint((float)x, (float)y, paint);
                    }
                }
                foreach (var entry in Layout.Objects)
                {
                    if (entry.Bounds.Bottom < viewport.Top || entry.Bounds.Y > viewport.Bottom || entry.Bounds.Right < viewport.Left || entry.Bounds.X > viewport.Right) continue;
                    string text = entry.Item switch { TextItem t => string.Join('\n', TextLayout.Wrap(t.Text, entry.Bounds.Width - t.Style.Padding * 2, t.Style, renderer.Metrics)), BarcodeItem b => b.Text, CheckBoxItem c => c.Expression, _ => "" };
                    try { renderer.DrawItem(canvas, new(entry.Item, entry.Bounds, text), true); }
                    catch (ReportRenderException error)
                    {
                        paint.Color = ReportRenderer.Color("#FFF0ED"); canvas.DrawRect(ReportRenderer.Rect(entry.Bounds), paint);
                        renderer.DrawText(canvas, error.Message, entry.Bounds, new() { FontSize = 8, Foreground = "#A3372E" });
                    }
                    paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = (float)(1 / PointScale); paint.Color = ReportRenderer.Color("#A4B3C1");
                    using (var dash = SKPathEffect.CreateDash([2, 2], 0)) { paint.PathEffect = dash; canvas.DrawRect(ReportRenderer.Rect(entry.Bounds), paint); paint.PathEffect = null; }
                    paint.Style = SKPaintStyle.Fill;
                    if (session.SelectedIds.Contains(entry.Item.Id))
                    {
                        paint.Color = ReportRenderer.Color("#2874B8"); paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = (float)(1.5 / PointScale); canvas.DrawRect(ReportRenderer.Rect(entry.Bounds), paint); paint.Style = SKPaintStyle.Fill;
                        double size = 5 / PointScale;
                        foreach (var (x, y) in new[] { (entry.Bounds.X, entry.Bounds.Y), (entry.Bounds.Right, entry.Bounds.Y), (entry.Bounds.X, entry.Bounds.Bottom), (entry.Bounds.Right, entry.Bounds.Bottom) })
                            canvas.DrawRect((float)(x - size / 2), (float)(y - size / 2), (float)size, (float)size, paint);
                    }
                }
                if (Marquee is { } box) { paint.Color = ReportRenderer.Color("#303976B4"); canvas.DrawRect(ReportRenderer.Rect(box), paint); paint.Style = SKPaintStyle.Stroke; paint.Color = ReportRenderer.Color("#3976B4"); canvas.DrawRect(ReportRenderer.Rect(box), paint); }
                lastError = null;
            }
            catch (Exception e) { if (lastError != e.Message) { lastError = e.Message; RenderFailed?.Invoke(e); } }
            finally { canvas.Restore(); }
        }
        private void DrawRulers(SKCanvas canvas, SKPaint paint)
        {
            paint.Color = ReportRenderer.Color("#ECEEF0");
            canvas.DrawRect((float)Layout.Paper.X, 0, (float)Layout.Paper.Width, (float)DesignLayout.RulerSize, paint);
            canvas.DrawRect((float)DesignLayout.BandPanelWidth, 0, (float)DesignLayout.RulerSize, (float)Layout.Height, paint);
            var style = new ItemStyle { FontSize = 7, Foreground = "#5E6872", Padding = 0 };
            double pointPerMm = 72 / 25.4;
            for (int mm = 0; mm * pointPerMm < Layout.Page.Width; mm += 5)
            {
                float x = (float)(Layout.Paper.X + mm * pointPerMm); paint.Color = ReportRenderer.Color("#7A8792"); paint.StrokeWidth = .5f;
                canvas.DrawLine(x, (float)DesignLayout.RulerSize - (mm % 10 == 0 ? 7 : 3), x, (float)DesignLayout.RulerSize, paint);
                if (mm % 10 == 0) renderer.DrawText(canvas, mm.ToString(), new(x + 2, 1, 24, 10), style);
            }
            for (int mm = 0; mm * pointPerMm < Layout.Paper.Height; mm += 5)
            {
                float y = (float)(Layout.Paper.Y + mm * pointPerMm); paint.Color = ReportRenderer.Color("#7A8792"); paint.StrokeWidth = .5f;
                canvas.DrawLine((float)Layout.Paper.X - (mm % 10 == 0 ? 7 : 3), y, (float)Layout.Paper.X, y, paint);
                if (mm % 20 == 0) renderer.DrawText(canvas, mm.ToString(), new(DesignLayout.BandPanelWidth + 1, y + 2, 18, 11), style with { FontSize = 6 });
            }
        }
    }
}
