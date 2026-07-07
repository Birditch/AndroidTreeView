using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AndroidTreeView.App.Converters;

/// <summary>
/// Maps a <c>DeviceBadgeKind</c> to a status-pill brush. Prefers the themed <c>Badge.*</c> resources
/// (defined in Styles/Badges.axaml) and falls back to matching solid colors when unavailable.
/// Compared by name so the converter never hard-references the enum type.
/// </summary>
public sealed class BadgeKindToBrushConverter : IValueConverter
{
    private static readonly IBrush OnlineFallback = new ImmutableSolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
    private static readonly IBrush OfflineFallback = new ImmutableSolidColorBrush(Color.FromRgb(0x5B, 0x61, 0x6B));
    private static readonly IBrush UnauthorizedFallback = new ImmutableSolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C));
    private static readonly IBrush NeutralFallback = new ImmutableSolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() switch
        {
            "Online" => Resource("Badge.Online") ?? OnlineFallback,
            "Offline" => Resource("Badge.Offline") ?? OfflineFallback,
            "Unauthorized" => Resource("Badge.Unauthorized") ?? UnauthorizedFallback,
            _ => Resource("Badge.Neutral") ?? NeutralFallback
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static IBrush? Resource(string key)
    {
        // Badge.* brushes are declared at the root of Styles/Badges.axaml (not theme-scoped),
        // so a null theme resolves them; casting to IResourceHost avoids depending on
        // Application-specific members.
        if (Application.Current is IResourceHost host &&
            host.TryGetResource(key, null, out var value) &&
            value is IBrush brush)
        {
            return brush;
        }

        return null;
    }
}
