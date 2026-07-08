using System;
using System.Threading.Tasks;
using AndroidTreeView.App.Services;
using AndroidTreeView.App.ViewModels;
using AndroidTreeView.App.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.App;

/// <summary>
/// The Avalonia application object. Resolves the <see cref="MainWindowViewModel"/> from the DI provider
/// bridged by <see cref="Program"/>, shows the main window, and starts shell initialization as a logged
/// fire-and-forget so startup is never blocked.
/// </summary>
public partial class App : Application
{
    /// <summary>The DI provider assigned by <see cref="Program.Main"/> before the app starts.</summary>
    public static IServiceProvider? ServiceProvider { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && ServiceProvider is { } provider)
        {
            var viewModel = provider.GetRequiredService<MainWindowViewModel>();
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = new MainWindow { DataContext = viewModel };

            // Close any app-owned helper windows/processes when the main window closes.
            var mirrors = provider.GetService<IScreenMirrorLauncher>();
            var cli = provider.GetService<ICliLauncher>();
            if (mirrors is not null || cli is not null)
            {
                void ShutdownOwned()
                {
                    mirrors?.ShutdownAll();
                    cli?.ShutdownAll();
                }

                desktop.ShutdownRequested += (_, _) => ShutdownOwned();
                desktop.Exit += (_, _) => ShutdownOwned();
            }

            var logger = provider.GetService<ILogger<App>>();
            _ = InitializeShellAsync(viewModel, logger);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task InitializeShellAsync(MainWindowViewModel viewModel, ILogger<App>? logger)
    {
        try
        {
            await viewModel.InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Shell initialization failed.");
        }
    }
}
