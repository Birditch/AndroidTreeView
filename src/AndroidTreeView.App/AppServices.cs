using System;
using System.Net.Http;
using AndroidTreeView.Adb.Services;
using AndroidTreeView.App.Localization;
using AndroidTreeView.App.Services;
using AndroidTreeView.App.ViewModels;
using AndroidTreeView.Core;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Options;
using AndroidTreeView.Core.Services;
using AndroidTreeView.Infrastructure.Settings;
using AndroidTreeView.Infrastructure.Update;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

        services.AddLogging(builder => builder.AddConsole());

        services.AddSingleton(CreateHttpClient());
        services.AddSingleton<AdbOptions>();

        // ADB / Core / Infrastructure services (singletons; concrete names fixed by earlier phases).
        services.AddSingleton<IAdbEnvironment, AdbEnvironment>();
        services.AddSingleton<IAdbLocator, AdbLocator>();
        services.AddSingleton<IAdbCommandExecutor, AdbCommandExecutor>();
        services.AddSingleton<IDeviceService, AdbDeviceService>();
        services.AddSingleton<ILogcatService, LogcatService>();
        services.AddSingleton<IDeviceMonitor, DeviceMonitor>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IUpdateService, GitHubUpdateService>();

        // App-owned services.
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();

        // Shell + device grid view models (singletons).
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<DevicesViewModel>();

        // Page + detail view models (transient).
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SetupViewModel>();
        services.AddTransient<AboutViewModel>();
        services.AddTransient<DeviceDetailViewModel>();

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

        return services;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppInfo.Name}/{AppInfo.Version}");
        return client;
    }
}
