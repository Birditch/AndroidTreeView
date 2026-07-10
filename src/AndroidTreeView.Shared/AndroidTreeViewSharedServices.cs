using AndroidTreeView.Adb.Services;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Options;
using AndroidTreeView.Core.Services;
using AndroidTreeView.Infrastructure.Settings;
using AndroidTreeView.Infrastructure.Update;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AndroidTreeView.Shared;

/// <summary>
/// Shared composition root for everything the full app and Mini should run identically: ADB discovery,
/// device monitoring, scrcpy launching, settings, update checks and update installation.
/// </summary>
public static class AndroidTreeViewSharedServices
{
    public static IServiceCollection AddAndroidTreeViewSharedServices(
        this IServiceCollection services,
        UpdateProductOptions? updateProduct = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        updateProduct ??= UpdateProductOptions.ForMainApp();

        services.AddLogging();
        services.TryAddSingleton(updateProduct);
        services.TryAddSingleton(sp => CreateHttpClient(sp.GetRequiredService<UpdateProductOptions>()));
        services.TryAddSingleton<AdbOptions>();

        services.TryAddSingleton<IAdbEnvironment, AdbEnvironment>();
        services.TryAddSingleton<IAdbLocator, AdbLocator>();
        services.TryAddSingleton<IAdbCommandExecutor, AdbCommandExecutor>();
        services.TryAddSingleton<IDeviceService, AdbDeviceService>();
        services.TryAddSingleton<ILogcatService, LogcatService>();
        services.TryAddSingleton<IDeviceMonitor, DeviceMonitor>();
        services.TryAddSingleton<IDeviceActionsService, AdbDeviceActionsService>();
        services.TryAddSingleton<IFastbootService, FastbootService>();
        services.TryAddSingleton<IScreenCaptureService, ScreenCaptureService>();
        services.TryAddSingleton<DeviceFileTransferService>();
        services.TryAddSingleton<IScrcpyLauncher, ScrcpyLauncher>();
        services.TryAddSingleton<ISettingsService, SettingsService>();
        services.TryAddSingleton<IUpdateService, GitHubUpdateService>();
        services.TryAddSingleton<IUpdateInstaller, UpdateInstaller>();

        return services;
    }

    private static HttpClient CreateHttpClient(UpdateProductOptions product)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{product.Name.Replace(' ', '-')}/{product.Version}");
        return client;
    }
}
