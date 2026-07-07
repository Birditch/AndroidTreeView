using System.Globalization;
using Avalonia.Data.Converters;

namespace AndroidTreeView.App.Converters;

/// <summary>
/// Maps a boolean to an opacity: <c>true</c> =&gt; 1.0, <c>false</c> =&gt; a dimmed value.
/// The dimmed value defaults to 0.4 and can be overridden with a numeric converter parameter.
/// </summary>
public sealed class BoolToOpacityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var dimmed = 0.4;
        if (parameter is string s &&
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var custom))
        {
            dimmed = custom;
        }

        var on = value is bool b && b;
        return on ? 1.0 : dimmed;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
