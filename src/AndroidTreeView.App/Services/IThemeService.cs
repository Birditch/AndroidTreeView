using AndroidTreeView.Core.Options;

namespace AndroidTreeView.App.Services;

/// <summary>
/// Applies the requested <see cref="ThemeMode"/> and optional accent color to the running application.
/// </summary>
public interface IThemeService
{
    /// <summary>Performs any one-time theme setup.</summary>
    void Initialize();

    /// <summary>Applies the requested theme variant.</summary>
    void Apply(ThemeMode mode);

    /// <summary>Applies an optional accent color (hex string); null restores the theme default.</summary>
    void ApplyAccent(string? hexColor);
}
