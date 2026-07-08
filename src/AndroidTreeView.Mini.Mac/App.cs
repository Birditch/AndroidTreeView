using AndroidTreeView.Core.Options;
using AndroidTreeView.Shared;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Microsoft.Extensions.DependencyInjection;

namespace AndroidTreeView.Mini.Mac;

public sealed class App : Application
{
    private ServiceProvider? _services;

    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _services = BuildServices();
            var agent = ActivatorUtilities.CreateInstance<MiniAgent>(_services);
            desktop.MainWindow = new MainWindow(agent);
            desktop.Exit += (_, _) => _services?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddAndroidTreeViewSharedServices(UpdateProductOptions.ForMiniApp());
        return services.BuildServiceProvider();
    }
}
