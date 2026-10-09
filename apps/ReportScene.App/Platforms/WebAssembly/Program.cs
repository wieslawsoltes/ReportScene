using Uno.UI.Hosting;

namespace ReportScene.App;

public static class Program
{
    private static async Task Main(string[] args)
    {
        var host = UnoPlatformHostBuilder.Create().App(() => new App()).UseWebAssembly().Build();
        await host.RunAsync();
    }
}
