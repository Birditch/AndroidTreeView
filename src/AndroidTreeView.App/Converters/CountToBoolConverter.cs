using System.Collections;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AndroidTreeView.App.Converters;

/// <summary>
/// True when a count/collection is non-empty. Accepts int/long counts, strings, and any
/// <see cref="IEnumerable"/>. Pass <c>invert</c> to test for empty instead.
/// </summary>
public sealed class CountToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasItems = value switch
        {
            null => false,
            string s => !string.IsNullOrEmpty(s),
            int i => i > 0,
            long l => l > 0,
            ICollection c => c.Count > 0,
            IEnumerable e => e.Cast<object?>().Any(),
            _ => true
        };

        return ConverterHelpers.IsInvert(parameter) ? !hasItems : hasItems;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
