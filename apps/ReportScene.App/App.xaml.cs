using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReportScene.Designer.Uno;
using ReportScene.Rendering.Skia;
using Windows.Storage;

namespace ReportScene.App;

public sealed partial class App : Application
{
    private Window? window;
    private ReportDesignerControl? designer;
    public App() { InitializeComponent(); UnhandledException += (_, e) => { System.Diagnostics.Debug.WriteLine(e.Exception); }; }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new Window { Title = "ReportScene — Report Designer", Content = new TextBlock { Text = "Loading ReportScene…", Margin = new(32), FontSize = 22 } };
        window.Activate();
        try
        {
            // Open Sans is a declared Uno SDK dependency, not a font copied from a developer's machine.
            try
            {
                var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Uno.Fonts.OpenSans/Fonts/OpenSans.ttf"));
                using var stream = await file.OpenStreamForReadAsync(); using var bytes = new MemoryStream(); await stream.CopyToAsync(bytes);
                SkiaTextMeasurer.SetFallbackFont(bytes.ToArray());
            }
            catch (Exception fontError) when (!OperatingSystem.IsBrowser()) { System.Diagnostics.Debug.WriteLine("Using system fonts: " + fontError.Message); }
#if __WASM__
            var host = new BrowserWorkspaceHost();
#else
            var host = new DesktopWorkspaceHost();
#endif
            designer = new ReportDesignerControl(host);
            window.Content = designer;
            window.Closed += (_, _) => designer.Dispose();
#if __WASM__
            host.Attach(designer);
            if (!host.SelfTest) await designer.RestoreRecoveryAsync();
#else
            await designer.RestoreRecoveryAsync();
#endif
        }
        catch (Exception e)
        {
            window.Content = new TextBox { Text = "ReportScene could not start.\n\n" + e, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Margin = new(24), FontSize = 14 };
#if __WASM__
            Uno.Foundation.WebAssemblyRuntime.InvokeJS("window.reportSceneFatal = " + System.Text.Json.JsonSerializer.Serialize(e.ToString(), AppJsonContext.Default.String) + ";");
#endif
        }
    }
}
