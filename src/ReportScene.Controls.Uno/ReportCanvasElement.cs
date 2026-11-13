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
}

/// <summary>
/// A logical drawing surface with a viewport-sized WebGPU target. The outer element
/// retains the document extent and pointer coordinate system; only the visible child
/// allocates a texture. Scrolling never allocates a full-document render target.
/// </summary>
public abstract class ReportCanvasElement : Grid, IDisposable
{
    private readonly GpuView gpu;
    private readonly TranslateTransform translation = new();
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
    private RectD viewport;
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
        pixelWidth, pixelHeight, viewport, submissionMilliseconds, lastError) { Statistics = statistics };

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
            Visibility = Visibility.Collapsed,
            RenderTransform = translation
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
        Children.Add(gpu);
        Children.Add(errorPresenter);
        Loaded += (_, _) => Invalidate();
        SizeChanged += (_, _) => Invalidate();
        LayoutUpdated += (_, _) => { if (awaitingGeometry) Invalidate(); };
        Unloaded += (_, _) =>
        {
            awaitingGeometry = true;
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
                rect = PreparedVisibility.Intersect(rect, new(0, 0, ActualWidth, ActualHeight));
                viewport = rect;
                if (rect.Width <= 0 || rect.Height <= 0)
                {
                    gpu.Visibility = Visibility.Collapsed;
                    errorPresenter.Visibility = Visibility.Collapsed;
                    return;
                }
                if (gpu.Width != rect.Width) gpu.Width = rect.Width;
                if (gpu.Height != rect.Height) gpu.Height = rect.Height;
                translation.X = errorTranslation.X = rect.X;
                translation.Y = errorTranslation.Y = rect.Y;
                errorPresenter.Width = Math.Min(560, rect.Width);
                errorPresenter.Visibility = ShowRenderingErrors && lastError is not null ? Visibility.Visible : Visibility.Collapsed;
                gpu.Visibility = Visibility.Visible;
                gpu.Invalidate();
            }
            catch (Exception error) { ReportFailure(error); }
        })) Interlocked.Exchange(ref invalidateQueued, 0);
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
        state = "Disposed";
        gpu.ReleaseRenderer();
        Children.Clear();
        gpu.Dispose();
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
            if (owner.disposed || renderer is null) return;
            long started = Stopwatch.GetTimestamp();
            try
            {
                renderer.Render(context, owner.viewport,
                    canvas => owner.RenderOverride(canvas, new(owner.ActualWidth, owner.ActualHeight)));
                owner.submissionMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                owner.pixelWidth = context.PixelWidth;
                owner.pixelHeight = context.PixelHeight;
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
