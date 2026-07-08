using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AndroidTreeView.App.Converters;

/// <summary>
/// Converts a boolean (e.g. "a dialog is open") into a <see cref="BlurEffect"/> so the content behind a
/// modal is Gaussian-blurred, or <c>null</c> (no effect) when false. One-way only.
/// </summary>
public sealed class BoolToBlurConverter : IValueConverter
{
    public static BoolToBlurConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? new BlurEffect { Radius = 12 } : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
