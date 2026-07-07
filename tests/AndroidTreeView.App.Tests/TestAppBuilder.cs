using AndroidTreeView.App.Tests;
using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace AndroidTreeView.App.Tests;

/// <summary>
/// Builds the real <see cref="AndroidTreeView.App.App"/> on the Avalonia headless platform so
/// <c>[AvaloniaTest]</c> methods run on a working UI thread/dispatcher with the app's styles and
/// resources loaded. No windowing backend is required.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<AndroidTreeView.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
