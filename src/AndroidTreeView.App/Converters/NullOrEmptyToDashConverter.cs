using System.Globalization;
using Avalonia.Data.Converters;

namespace AndroidTreeView.App.Converters;

/// <summary>
/// Renders a value as text, substituting an em dash ("—") when it is null, empty or whitespace.
/// </summary>
public sealed class NullOrEmptyToDashConverter : IValueConverter
{
    /// <summary>The placeholder shown for missing values.</summary>
    public const string Dash = "—";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string ?? value?.ToString();
        return string.IsNullOrWhiteSpace(text) ? Dash : text;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
