using Uno.UI.Hosting;
using Uno.UI.Composition.Skia;
using Uno.UI.Composition.WebGpu;

namespace ReportScene.App;

public static class Program
{
    private static async Task Main(string[] args)
    {
        var host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .GraphicsBackend(WebGpuBackend.CreateGraphicsProvider())
            .GraphicsBackend(SkiaBackend.CreateGraphicsProvider())
            .UseWebAssembly().Build();
        await host.RunAsync();
    }
}
