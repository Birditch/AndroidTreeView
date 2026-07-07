using System.Globalization;
using Avalonia.Data.Converters;

namespace AndroidTreeView.App.Converters;

/// <summary>
/// Formats a byte count (long/int/double) into a human-readable string (e.g. "1.5 GB").
/// Null or negative values render as an em dash.
/// </summary>
public sealed class BytesToHumanConverter : IValueConverter
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double? bytes = value switch
        {
            long l => l,
            int i => i,
            double d => d,
            float f => f,
            _ => null
        };

        if (bytes is null || double.IsNaN(bytes.Value) || bytes.Value < 0)
        {
            return "—";
        }

        var size = bytes.Value;
        var unit = 0;
        while (size >= 1024 && unit < Units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        var format = unit == 0 ? "0" : "0.##";
        return $"{size.ToString(format, culture)} {Units[unit]}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
