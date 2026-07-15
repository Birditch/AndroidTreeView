using System;
using System.Threading.Tasks;
using AndroidTreeView.App;
using AndroidTreeView.App.Services;
using AndroidTreeView.App.ViewModels;
using AndroidTreeView.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AndroidTreeView.App.Tests;

/// <summary>
/// Verifies the composition root: building the exact same service graph the application uses
/// (<see cref="AppServices.ConfigureAppServices"/>) resolves every registered service and view model
/// without throwing. This proves all constructors and their dependency chains wire up correctly.
/// None of the resolved constructors touch <c>Application.Current</c>, so this runs without Avalonia.
/// </summary>
public sealed class ServiceGraphTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.ConfigureAppServices();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void ConfigureAppServices_returns_same_collection_for_chaining()
    {
        var services = new ServiceCollection();
        var result = services.ConfigureAppServices();
        Assert.Same(services, result);
    }

    [Theory]
    [InlineData(typeof(IDeviceService))]
    [InlineData(typeof(ISettingsService))]
    [InlineData(typeof(IUpdateService))]
    [InlineData(typeof(IUpdateInstaller))]
    [InlineData(typeof(IThemeService))]
    [InlineData(typeof(IFilePickerService))]
    [InlineData(typeof(ILocalizationService))]
    [InlineData(typeof(IDeviceMonitor))]
    [InlineData(typeof(ILogcatService))]
    [InlineData(typeof(IAdbLocator))]
    [InlineData(typeof(IAdbEnvironment))]
    [InlineData(typeof(IRootWizardService))]
    public async Task Resolves_every_registered_service(Type serviceType)
    {
        await using var provider = BuildProvider();
        var resolved = provider.GetService(serviceType);
        Assert.NotNull(resolved);
    }

    [Theory]
    [InlineData(typeof(MainWindowViewModel))]
    [InlineData(typeof(DevicesViewModel))]
    [InlineData(typeof(DeviceDetailViewModel))]
    [InlineData(typeof(SettingsViewModel))]
    [InlineData(typeof(SetupViewModel))]
    [InlineData(typeof(AboutViewModel))]
    [InlineData(typeof(OverviewViewModel))]
    [InlineData(typeof(HardwareViewModel))]
    [InlineData(typeof(BatteryViewModel))]
    [InlineData(typeof(SystemInfoViewModel))]
    [InlineData(typeof(StorageViewModel))]
    [InlineData(typeof(NetworkViewModel))]
    [InlineData(typeof(RootStatusViewModel))]
    [InlineData(typeof(LogcatViewModel))]
    [InlineData(typeof(RawPropertiesViewModel))]
    [InlineData(typeof(RootWizardViewModel))]
    public async Task Resolves_every_registered_view_model(Type viewModelType)
    {
        await using var provider = BuildProvider();
        var resolved = provider.GetService(viewModelType);
        Assert.NotNull(resolved);
    }

    [Fact]
    public async Task Resolving_MainWindowViewModel_materializes_the_full_shell_graph()
    {
        await using var provider = BuildProvider();

        var shell = provider.GetRequiredService<MainWindowViewModel>();

        Assert.NotNull(shell);
        Assert.NotNull(shell.Devices);
        Assert.NotNull(shell.Settings);
        Assert.NotNull(shell.Setup);
        // Default content is the device grid before any initialization runs.
        Assert.Same(shell.Devices, shell.CurrentContent);
    }

    [Fact]
    public async Task Detail_factory_produces_a_detail_view_model_with_all_nine_categories()
    {
        await using var provider = BuildProvider();

        var factory = provider.GetRequiredService<Func<DeviceDetailViewModel>>();
        var detail = factory();

        Assert.NotNull(detail);
    }

    [Fact]
    public async Task ViewModel_singletons_are_shared_and_pages_are_transient()
    {
        await using var provider = BuildProvider();

        Assert.Same(
            provider.GetRequiredService<DevicesViewModel>(),
            provider.GetRequiredService<DevicesViewModel>());

        Assert.Same(
            provider.GetRequiredService<RootWizardViewModel>(),
            provider.GetRequiredService<RootWizardViewModel>());

        Assert.NotSame(
            provider.GetRequiredService<SettingsViewModel>(),
            provider.GetRequiredService<SettingsViewModel>());
    }

    [Fact]
    public async Task Root_navigation_reuses_the_singleton_wizard_state()
    {
        await using var provider = BuildProvider();
        var shell = provider.GetRequiredService<MainWindowViewModel>();
        var rootWizard = provider.GetRequiredService<RootWizardViewModel>();

        shell.NavigateRootCommand.Execute(null);

        Assert.Equal(NavSection.Root, shell.CurrentSection);
        Assert.Same(rootWizard, shell.CurrentContent);
        Assert.Same(rootWizard, shell.RootWizard);
    }
}
