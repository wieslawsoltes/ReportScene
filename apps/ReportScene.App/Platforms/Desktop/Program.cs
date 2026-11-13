using Uno.UI.Hosting;
using Uno.UI.Composition.Skia;
using Uno.UI.Composition.WebGpu;

namespace ReportScene.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .GraphicsBackend(WebGpuBackend.CreateGraphicsProvider())
            .GraphicsBackend(SkiaBackend.CreateGraphicsProvider())
            .UseX11().UseLinuxFrameBuffer().UseMacOS().UseWin32().Build();
        host.Run();
    }
}
