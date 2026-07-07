using System.Globalization;
using Avalonia.Data.Converters;

namespace AndroidTreeView.App.Converters;

/// <summary>Maps a value to <c>true</c> when it is non-null. Pass <c>invert</c> to flip the result.</summary>
public sealed class NullToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasValue = value is not null;
        return ConverterHelpers.IsInvert(parameter) ? !hasValue : hasValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
