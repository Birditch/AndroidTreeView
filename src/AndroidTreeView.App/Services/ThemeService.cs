using AndroidTreeView.Core.Options;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace AndroidTreeView.App.Services;

/// <summary>
/// Applies theme variants and accent colors to <see cref="Application.Current"/>. All members must be
/// invoked on the UI thread (callers are view models reacting to user actions).
/// </summary>
public sealed class ThemeService : IThemeService
{
    private const string AccentColorKey = "SystemAccentColor";
    private const string AccentBrushKey = "Accent.Brush";

    /// <inheritdoc />
    public void Initialize()
    {
        // The default variant is honoured until the user picks an explicit theme; nothing to do here.
    }

    /// <inheritdoc />
    public void Apply(ThemeMode mode)
    {
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        application.RequestedThemeVariant = mode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    /// <inheritdoc />
    public void ApplyAccent(string? hexColor)
    {
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(hexColor) || !Color.TryParse(hexColor, out var color))
        {
            return;
        }

        application.Resources[AccentColorKey] = color;
        application.Resources[AccentBrushKey] = new SolidColorBrush(color);
    }
}
