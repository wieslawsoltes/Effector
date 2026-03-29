using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Effector.PackageIntegration.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;

            if (IntegrationLaunchOptions.AutoExitRequested)
            {
                window.Opened += (_, _) =>
                {
                    DispatcherTimer.RunOnce(
                        () => desktop.Shutdown(),
                        TimeSpan.FromMilliseconds(750));
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
