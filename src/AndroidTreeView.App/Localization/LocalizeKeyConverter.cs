using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AndroidTreeView.App.Localization;

/// <summary>
/// Resolves a localization key (supplied as the converter parameter) to its localized string. The bound
/// value is the service's <see cref="LocalizationService.LanguageTick"/>, so the binding re-evaluates —
/// and the text re-resolves against the current culture — whenever the language changes. Used by
/// <see cref="LocalizeExtension"/>; not intended for direct XAML use.
/// </summary>
public sealed class LocalizeKeyConverter : IValueConverter
{
    /// <summary>Shared stateless instance.</summary>
    public static LocalizeKeyConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is not string key || string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        return LocalizationService.Instance?.Get(key) ?? key;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
