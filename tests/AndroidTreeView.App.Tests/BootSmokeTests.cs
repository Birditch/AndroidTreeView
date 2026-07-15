using System.Threading.Tasks;
using AndroidTreeView.App;
using AndroidTreeView.App.ViewModels;
using AndroidTreeView.App.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AndroidTreeView.App.Tests;

/// <summary>
/// Boots the real <see cref="AndroidTreeView.App.App"/> on the Avalonia headless platform (see
/// <see cref="TestAppBuilder"/>): builds the production service graph, resolves the shell view model,
/// constructs and shows the main window, and confirms the ViewLocator materializes real views. This is
/// the end-to-end proof that the application starts without throwing.
/// </summary>
public sealed class BootSmokeTests
{
    [AvaloniaFact]
    public void App_is_initialized_with_styles_and_view_locator()
    {
        Assert.NotNull(Application.Current);

        // The app's styles/resources (FluentTheme, Colors/Glass/Badges/Converters/Controls) loaded
        // during headless initialization, and the ViewLocator data template is registered.
        Assert.NotEmpty(Application.Current!.Styles);
        Assert.Contains(Application.Current.DataTemplates, t => t is ViewLocator);
    }

    [AvaloniaFact]
    public async Task Main_window_boots_with_resolved_shell_view_model()
    {
        var services = new ServiceCollection();
        services.ConfigureAppServices();
        await using var provider = services.BuildServiceProvider();

        // Bridge the provider onto the app exactly as Program.Main does.
        AndroidTreeView.App.App.ServiceProvider = provider;
        try
        {
            var viewModel = provider.GetRequiredService<MainWindowViewModel>();
            Assert.NotNull(viewModel);

            var window = new MainWindow { DataContext = viewModel };
            window.Show();

            Assert.NotNull(window);
            Assert.Same(viewModel, window.DataContext);

            window.Close();
        }
        finally
        {
            AndroidTreeView.App.App.ServiceProvider = null;
        }
    }

    [AvaloniaFact]
    public async Task ViewLocator_resolves_devices_setup_and_root_views()
    {
        var services = new ServiceCollection();
        services.ConfigureAppServices();
        await using var provider = services.BuildServiceProvider();

        var locator = new ViewLocator();

        var devicesView = locator.Build(provider.GetRequiredService<DevicesViewModel>());
        Assert.IsType<DevicesView>(devicesView);

        var setupView = locator.Build(provider.GetRequiredService<SetupViewModel>());
        Assert.IsType<SetupView>(setupView);

        var rootView = locator.Build(provider.GetRequiredService<RootWizardViewModel>());
        Assert.IsType<RootWizardView>(rootView);
    }
}
