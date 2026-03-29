using Avalonia;

namespace Effector.PackageIntegration.App;

internal static class Program
{
    [System.STAThread]
    public static void Main(string[] args)
    {
        IntegrationLaunchOptions.Initialize(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
