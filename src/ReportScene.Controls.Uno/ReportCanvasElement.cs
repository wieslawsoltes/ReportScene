using System.Diagnostics;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ReportScene.Model;
using ReportScene.Rendering.Skia;
using ReportScene.Rendering.WebGpu;
using Uno.WinUI.Graphics3DWebGpu;
using Windows.Foundation;

namespace ReportScene.Controls.Uno;

/// <summary>
/// Read-only native GPU submission evidence. A submitted frame is not a compositor
/// presentation acknowledgement or a hardware performance measurement.
/// </summary>
public readonly record struct ReportGpuSurfaceSnapshot(string State, bool? Initialized, long SubmittedFrames,
    int PixelWidth, int PixelHeight, RectD Viewport, double SubmissionMilliseconds, string? Error)
{
    public WebGpuRenderStatistics Statistics { get; init; }
    public double RasterizationScale { get; init; }
    public RectD? RequestedViewport { get; init; }
    /// <summary>The arranged GPU child's observed bounds in root DIPs, before ancestor clipping.</summary>
    public RectD? PresentationBounds { get; init; }
}

/// <summary>
/// A logical drawing surface with a viewport-sized WebGPU target. The outer element
/// retains the document extent and pointer coordinate system; only the visible child
/// allocates a texture. Scrolling never allocates a full-document render target.
/// </summary>
public abstract class ReportCanvasElement : Grid, IDisposable
{
    private readonly GpuView gpu;
    private readonly Canvas gpuHost = new() { IsHitTestVisible = false, UseLayoutRounding = false };
    private readonly RectangleGeometry presentationClip = new();
    private readonly MatrixTransform presentationTransform = new();
    private readonly TranslateTransform errorTranslation = new();
    private readonly TextBlock errorText = new()
    {
        TextWrapping = TextWrapping.Wrap,
        FontSize = 14,
        Foreground = ControlTheme.Brush("#7B3027")
    };
    private readonly Border errorPresenter;
    private int invalidateQueued;
    private bool awaitingGeometry = true, disposed;
    private RectD viewport, submittedViewport;
    private RectD? requestedViewport;
    private ReportRasterViewport rasterViewport;
    private XamlRoot? observedRoot;
    private Point lastRootOrigin;
    private double rasterizationScale = 1;
    private string? lastError;
    private string state = "Waiting";
    private long submittedFrames;
    private int pixelWidth, pixelHeight;
    private double submissionMilliseconds;
    private WebGpuRenderStatistics statistics;

    public event Action<Exception>? RenderFailed;
    public event Action? RendererStateChanged;
    public string? RenderingError => lastError;
    public ReportGpuSurfaceSnapshot GpuSnapshot => new(state, gpu.IsWebGpuInitialized, submittedFrames,
        pixelWidth, pixelHeight, submittedFrames > 0 ? submittedViewport : viewport, submissionMilliseconds, lastError)
    {
        Statistics = statistics,
        RasterizationScale = rasterizationScale,
        RequestedViewport = requestedViewport,
        PresentationBounds = GetPresentationBounds()
    };

    protected RectD RenderViewport => viewport;
    protected virtual bool ShowRenderingErrors => true;

    protected ReportCanvasElement()
    {
        // Grid's transparent background preserves the original canvas hit area,
        // including offscreen coordinates used for captured drag operations.
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        gpu = new(this)
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            UseLayoutRounding = false,
            Visibility = Visibility.Collapsed,
            RenderTransform = presentationTransform
        };
        errorPresenter = new()
        {
            Child = errorText,
            Padding = new(20),
            Background = ControlTheme.Brush("#FFF3EF"),
            BorderBrush = ControlTheme.Brush("#D9A195"),
            BorderThickness = new(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            RenderTransform = errorTranslation
        };
        gpuHost.Clip = presentationClip;
        gpuHost.Children.Add(gpu);
        Children.Add(gpuHost);
        Children.Add(errorPresenter);
        Loaded += (_, _) => { ObserveRoot(); Invalidate(); };
        SizeChanged += (_, _) => Invalidate();
        LayoutUpdated += OnLayoutUpdated;
        Unloaded += (_, _) =>
        {
            ObserveRoot(null);
            awaitingGeometry = true;
            requestedViewport = null;
            if (!disposed) state = "Waiting";
        };
        gpu.RegisterPropertyChangedCallback(WebGpuCanvasElement.IsWebGpuInitializedProperty, (_, _) =>
        {
            if (disposed) return;
            if (gpu.IsWebGpuInitialized is false && lastError is null) state = "Unavailable";
            RendererStateChanged?.Invoke();
        });
    }

    /// <summary>Visible rectangle in the outer element's DIP coordinate system; null defers until layout.</summary>
    protected virtual RectD? GetRenderViewport()
        => IsLoaded && ActualWidth > 0 && ActualHeight > 0 ? new(0, 0, ActualWidth, ActualHeight) : null;

    protected abstract void RenderOverride(ReportCanvas canvas, Size area);

    /// <summary>Coalesces content, scroll and layout invalidations into the next native frame.</summary>
    public void Invalidate()
    {
        if (disposed || Interlocked.Exchange(ref invalidateQueued, 1) != 0) return;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref invalidateQueued, 0);
            if (disposed || !IsLoaded) return;
            try
            {
                var next = GetRenderViewport();
                if (next is null)
                {
                    awaitingGeometry = true;
                    requestedViewport = null;
                    gpu.Visibility = Visibility.Collapsed;
                    errorPresenter.Visibility = Visibility.Collapsed;
                    return;
                }
                awaitingGeometry = false;
                var rect = next.Value;
                if (!double.IsFinite(rect.X) || !double.IsFinite(rect.Y)
                    || !double.IsFinite(rect.Width) || !double.IsFinite(rect.Height)
                    || !double.IsFinite(rect.Right) || !double.IsFinite(rect.Bottom)
                    || rect.Width < 0 || rect.Height < 0)
                    throw new InvalidOperationException("The WebGPU viewport must have finite nonnegative dimensions.");
                if (!double.IsFinite(ActualWidth) || !double.IsFinite(ActualHeight))
                    throw new InvalidOperationException("The logical report surface must have finite dimensions.");
                rect = PreparedVisibility.Intersect(rect, new(0, 0, ActualWidth, ActualHeight));
                requestedViewport = rect;
                if (rect.Width <= 0 || rect.Height <= 0)
                {
                    viewport = rect;
                    gpu.Visibility = Visibility.Collapsed;
                    errorPresenter.Visibility = Visibility.Collapsed;
                    return;
                }
                ObserveRoot();
                rasterizationScale = XamlRoot?.RasterizationScale ?? 1;
                lastRootOrigin = TransformToVisual(null).TransformPoint(new(0, 0));
                rasterViewport = ReportRasterViewport.Calculate(rect, lastRootOrigin.X, lastRootOrigin.Y, rasterizationScale);
                viewport = rasterViewport.Bounds;
                // Uno 7.0.32 records this visual in its own logical coordinates:
                // WebGpuCanvasElement sizes its texture from that identity recorder,
                // while the root DPI transform is applied only when it is replayed.
                // Use integer physical dimensions here, then cancel DPI in this
                // private child's presentation transform. The outer document remains
                // in DIPs. Canvas measures independently, so the larger child cannot
                // change the document's desired size or be clipped to a small slot.
                // Keep the expanded viewport: intersecting it again would stretch
                // N texels over a nonintegral number of physical pixels.
                if (gpu.Width != rasterViewport.PixelWidth) gpu.Width = rasterViewport.PixelWidth;
                if (gpu.Height != rasterViewport.PixelHeight) gpu.Height = rasterViewport.PixelHeight;
                var matrix = new Matrix(1 / rasterizationScale, 0, 0, 1 / rasterizationScale, viewport.X, viewport.Y);
                if (!presentationTransform.Matrix.Equals(matrix)) presentationTransform.Matrix = matrix;
                var clip = new Rect(0, 0, ActualWidth, ActualHeight);
                if (presentationClip.Rect != clip) presentationClip.Rect = clip;
                errorTranslation.X = rect.X;
                errorTranslation.Y = rect.Y;
                errorPresenter.Width = Math.Min(560, rect.Width);
                errorPresenter.Visibility = ShowRenderingErrors && lastError is not null ? Visibility.Visible : Visibility.Collapsed;
                gpu.Visibility = Visibility.Visible;
                gpu.Invalidate();
            }
            catch (Exception error) { ReportFailure(error); }
        })) Interlocked.Exchange(ref invalidateQueued, 0);
    }

    private void ObserveRoot() => ObserveRoot(XamlRoot);

    private void ObserveRoot(XamlRoot? root)
    {
        if (ReferenceEquals(observedRoot, root)) return;
        if (observedRoot is not null) observedRoot.Changed -= OnRootChanged;
        observedRoot = root;
        if (root is not null) root.Changed += OnRootChanged;
    }

    private void OnRootChanged(XamlRoot root, XamlRootChangedEventArgs args)
    {
        if (!disposed && IsLoaded && root.RasterizationScale != rasterizationScale) Invalidate();
    }

    private void OnLayoutUpdated(object? sender, object args)
    {
        if (disposed || !IsLoaded) return;
        ObserveRoot();
        if (awaitingGeometry) { Invalidate(); return; }
        if (requestedViewport is not { Width: > 0, Height: > 0 } requested || lastError is not null) return;
        try
        {
            double scale = XamlRoot?.RasterizationScale ?? 1;
            var origin = TransformToVisual(null).TransformPoint(new(0, 0));
            if (scale == rasterizationScale && origin == lastRootOrigin) return;
            var next = ReportRasterViewport.Calculate(requested, origin.X, origin.Y, scale);
            lastRootOrigin = origin;
            // Integer-pixel translations keep the same local raster phase and do
            // not require new report commands. Observe only owner geometry, never
            // the enlarged child's layout, so this cannot form a resize feedback loop.
            if (scale != rasterizationScale || !next.HasSamePixelMapping(rasterViewport, scale)) Invalidate();
        }
        catch (Exception error) { ReportFailure(error); }
    }

    private RectD? GetPresentationBounds()
    {
        if (disposed || !gpu.IsLoaded || gpu.Visibility != Visibility.Visible || gpu.ActualWidth <= 0 || gpu.ActualHeight <= 0) return null;
        var bounds = gpu.TransformToVisual(null).TransformBounds(new(0, 0, gpu.ActualWidth, gpu.ActualHeight));
        return new(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    private void ReportFailure(Exception error, bool unavailable = false)
    {
        // WebGPU callbacks run during composition. Never mutate the XAML tree or
        // invoke host UI callbacks from inside a render pass.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (disposed) return;
            string message = unavailable
                ? "Report rendering requires WebGPU. Enable WebGPU in a supported browser or use a compatible graphics driver."
                : "WebGPU report rendering failed: " + error.Message;
            bool changed = !StringComparer.Ordinal.Equals(lastError, message);
            lastError = message;
            state = unavailable ? "Unavailable" : "Failed";
            errorText.Text = message;
            errorPresenter.Visibility = ShowRenderingErrors ? Visibility.Visible : Visibility.Collapsed;
            if (changed) RenderFailed?.Invoke(new InvalidOperationException(message, error));
            RendererStateChanged?.Invoke();
        });
    }

    public new void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ObserveRoot(null);
        state = "Disposed";
        gpu.ReleaseRenderer();
        gpuHost.Children.Clear();
        Children.Clear();
        gpu.Dispose();
        gpuHost.Dispose();
        base.Dispose();
    }

    private sealed class GpuView(ReportCanvasElement owner) : WebGpuCanvasElement
    {
        private WebGpuReportRenderer? renderer;

        protected override void Init(WebGpuCanvasContext context)
        {
            try { renderer = new(context); }
            catch (Exception error)
            {
                // Uno does not call OnDestroy after an unsuccessful Init.
                ReleaseRenderer();
                owner.ReportFailure(error);
                throw;
            }
        }

        protected override void RenderOverride(WebGpuCanvasContext context)
        {
            if (owner.disposed || renderer is null || owner.requestedViewport is not { Width: > 0, Height: > 0 }) return;
            // A queued paint can precede layout of a newly sized child. Wait for
            // Uno's resize repaint instead of recording new content into an old
            // density target or reporting that transitional frame as ready.
            if (context.PixelWidth != owner.rasterViewport.PixelWidth
                || context.PixelHeight != owner.rasterViewport.PixelHeight) return;
            long started = Stopwatch.GetTimestamp();
            try
            {
                renderer.Render(context, owner.viewport,
                    canvas => owner.RenderOverride(canvas, new(owner.ActualWidth, owner.ActualHeight)));
                owner.submissionMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                owner.pixelWidth = context.PixelWidth;
                owner.pixelHeight = context.PixelHeight;
                owner.submittedViewport = owner.viewport;
                owner.statistics = renderer.LastStatistics;
                owner.submittedFrames++;
                bool changed = owner.state != "WebGPU" || owner.lastError is not null;
                owner.state = "WebGPU";
                owner.lastError = null;
                if (changed) owner.DispatcherQueue.TryEnqueue(() =>
                {
                    if (owner.disposed) return;
                    owner.errorPresenter.Visibility = Visibility.Collapsed;
                    owner.RendererStateChanged?.Invoke();
                });
            }
            catch (Exception error)
            {
                owner.ReportFailure(error);
                throw;
            }
        }

        protected override void OnWebGpuUnavailable()
            => owner.ReportFailure(new PlatformNotSupportedException("The active Uno composition backend is not WebGPU."), unavailable: true);

        protected override void OnDestroy(WebGpuCanvasContext context) => ReleaseRenderer();

        internal void ReleaseRenderer()
        {
            var current = renderer;
            renderer = null;
            current?.Dispose();
        }
    }
}
