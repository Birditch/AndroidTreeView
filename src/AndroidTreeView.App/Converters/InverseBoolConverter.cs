using System.Globalization;
using Avalonia.Data.Converters;

namespace AndroidTreeView.App.Converters;

/// <summary>Inverts a boolean. Null is treated as <c>false</c> (so it inverts to <c>true</c>).</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !(value as bool? ?? false);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !(value as bool? ?? false);
}
