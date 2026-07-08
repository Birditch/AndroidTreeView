using System;
using AndroidTreeView.App.Localization;
using AndroidTreeView.App.Services;
using AndroidTreeView.App.ViewModels;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Options;
using AndroidTreeView.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace AndroidTreeView.App;

/// <summary>
/// The application composition root. Registers the entire service graph (services + view models) onto a
/// <see cref="IServiceCollection"/> so that both <see cref="Program.Main"/> and the test suite can build
/// the identical provider without drift.
/// </summary>
public static class AppServices
{
    /// <summary>
    /// Registers every application service and view model. Returns the same collection to allow chaining.
    /// </summary>
    public static IServiceCollection ConfigureAppServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAndroidTreeViewSharedServices(UpdateProductOptions.ForMainApp());

        // App-owned services.
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IScreenMirrorLauncher, ScreenMirrorLauncher>();
        services.AddSingleton<ICliLauncher, CliLauncher>();
        services.AddSingleton<IDialogService, DialogService>();

        // Shell + device grid view models (singletons).
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<DevicesViewModel>();

        // Page + detail view models (transient).
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SetupViewModel>();
        services.AddTransient<AboutViewModel>();
        services.AddTransient<DeviceDetailViewModel>();
        services.AddTransient<ScreenMirrorViewModel>();

        // Category view models (transient).
        services.AddTransient<OverviewViewModel>();
        services.AddTransient<HardwareViewModel>();
        services.AddTransient<BatteryViewModel>();
        services.AddTransient<SystemInfoViewModel>();
        services.AddTransient<StorageViewModel>();
        services.AddTransient<NetworkViewModel>();
        services.AddTransient<RootStatusViewModel>();
        services.AddTransient<LogcatViewModel>();
        services.AddTransient<RawPropertiesViewModel>();

        // Factories so the shell can create transient content on demand.
        services.AddTransient<Func<AboutViewModel>>(sp => sp.GetRequiredService<AboutViewModel>);
        services.AddTransient<Func<DeviceDetailViewModel>>(sp => sp.GetRequiredService<DeviceDetailViewModel>);
        services.AddTransient<Func<ScreenMirrorViewModel>>(sp => sp.GetRequiredService<ScreenMirrorViewModel>);

        return services;
    }
}
