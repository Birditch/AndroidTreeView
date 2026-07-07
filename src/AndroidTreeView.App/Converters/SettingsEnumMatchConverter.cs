using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace AndroidTreeView.App.Converters;

/// <summary>
/// Two-way enum equality converter for radio-button selection. <see cref="Convert"/> returns true when
/// the bound enum equals the <c>ConverterParameter</c>; <see cref="ConvertBack"/> returns the parameter
/// when the control becomes checked and <see cref="BindingOperations.DoNothing"/> otherwise, so an
/// unchecked radio button never clears the source value.
/// </summary>
public sealed class SettingsEnumMatchConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null && parameter is not null && value.Equals(parameter);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true && parameter is not null ? parameter : BindingOperations.DoNothing;
}
