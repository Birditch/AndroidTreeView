using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AndroidTreeView.App.Converters;

/// <summary>
/// Maps a battery level (0-100) to a fill brush: red when low (&lt;=15), amber mid-range (&lt;=40),
/// green when healthy. A null/unknown level renders gray.
/// </summary>
public sealed class BatteryLevelToBrushConverter : IValueConverter
{
    private static readonly IBrush Low = new ImmutableSolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
    private static readonly IBrush Mid = new ImmutableSolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C));
    private static readonly IBrush High = new ImmutableSolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
    private static readonly IBrush Unknown = new ImmutableSolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int? level = value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            _ => null
        };

        if (level is null)
        {
            return Unknown;
        }

        return level.Value <= 15 ? Low : level.Value <= 40 ? Mid : High;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
