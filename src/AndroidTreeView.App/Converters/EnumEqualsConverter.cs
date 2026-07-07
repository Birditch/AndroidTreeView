using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace AndroidTreeView.App.Converters;

/// <summary>
/// True when the bound value's name equals the converter parameter (case-insensitive). Used to drive
/// selection highlights for tabs / nav sections. Supports two-way binding for radio-style selectors:
/// <see cref="ConvertBack"/> returns the parsed enum when checked and does nothing otherwise.
/// </summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ConverterHelpers.NameEquals(value, parameter);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is not null)
        {
            var enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (enumType.IsEnum)
            {
                try
                {
                    return Enum.Parse(enumType, parameter.ToString()!, ignoreCase: true);
                }
                catch (ArgumentException)
                {
                    return BindingOperations.DoNothing;
                }
            }
        }

        return BindingOperations.DoNothing;
    }
}
