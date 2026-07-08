using System;
using AndroidTreeView.Core.Options;
using AndroidTreeView.Mini.ViewModels;
using AndroidTreeView.Mini.Views;
using AndroidTreeView.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Windows.Forms;

namespace AndroidTreeView.Mini;

/// <summary>
/// Entry point and composition root for the Mini companion. The Mini intentionally uses the native
/// Windows Forms stack so the portable package does not carry Avalonia/Skia payloads.
/// </summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var services = new ServiceCollection();

        services.AddAndroidTreeViewSharedServices(UpdateProductOptions.ForMiniApp());
        services.AddSingleton<MiniViewModel>();

        using var provider = services.BuildServiceProvider();
        var viewModel = provider.GetRequiredService<MiniViewModel>();
        var logger = provider.GetService<ILogger<MiniForm>>();

        Application.Run(new MiniForm(viewModel, logger));
    }
}
