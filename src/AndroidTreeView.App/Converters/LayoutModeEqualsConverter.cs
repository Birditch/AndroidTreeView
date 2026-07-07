using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace AndroidTreeView.App.Converters;

/// <summary>
/// True when the bound layout mode's name equals the converter parameter (case-insensitive).
/// One-way; used to toggle responsive layout branches from <c>AppLayoutMode</c>.
/// </summary>
public sealed class LayoutModeEqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ConverterHelpers.NameEquals(value, parameter);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        BindingOperations.DoNothing;
}
