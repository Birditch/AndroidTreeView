using AndroidTreeView.Core.Options;

namespace AndroidTreeView.App.ViewModels;

/// <summary>
/// Static option sources for settings controls that bind to fixed enum sets via <c>x:Static</c>.
/// Kept separate from the view-model so XAML can reference the arrays without a live instance.
/// </summary>
public static class SettingsOptions
{
    /// <summary>All startup-behavior options in declaration order.</summary>
    public static StartupBehavior[] StartupOptions { get; } = Enum.GetValues<StartupBehavior>();
}
