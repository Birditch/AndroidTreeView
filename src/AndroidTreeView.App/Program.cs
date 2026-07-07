using System;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;

namespace AndroidTreeView.App;

/// <summary>
/// Application entry point and composition root. Builds the service provider (bridged onto
/// <see cref="App.ServiceProvider"/>) then starts the Avalonia classic desktop lifetime. The service
/// registration lives in <see cref="AppServices.ConfigureAppServices"/> so tests can build the same graph.
/// </summary>
internal static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any SynchronizationContext-reliant
    // code before AppMain is called: things aren't initialized yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        var services = new ServiceCollection();
        services.ConfigureAppServices();
        App.ServiceProvider = services.BuildServiceProvider();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
