using System;
using Avalonia.Data;

namespace AndroidTreeView.App.Localization;

/// <summary>
/// XAML markup extension used as <c>{loc:Localize Key=some.key}</c>. It produces a one-way binding onto
/// the <see cref="LocalizationService"/> indexer so the resolved string refreshes automatically when the
/// active language changes (the service raises <see cref="System.ComponentModel.INotifyPropertyChanged"/>
/// for its indexer). If the service is not available yet the raw key is returned so nothing crashes.
/// </summary>
/// <remarks>
/// Recognised by Avalonia through the markup-extension convention (a public <c>ProvideValue</c> method),
/// so no base class is required. Consumers add <c>xmlns:loc="using:AndroidTreeView.App.Localization"</c>.
/// </remarks>
public sealed class LocalizeExtension
{
    public LocalizeExtension()
    {
    }

    public LocalizeExtension(string key)
    {
        Key = key;
    }

    /// <summary>The localization key to resolve (e.g. <c>nav.devices</c>).</summary>
    public string Key { get; set; } = string.Empty;

    public object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key))
        {
            return string.Empty;
        }

        var service = LocalizationService.Instance;
        if (service is null)
        {
            return Key;
        }

        // Bind to the service's LanguageTick (a plain property that changes on every language switch) and
        // resolve the key in the converter. This refreshes reliably on language change, unlike binding the
        // indexer directly (whose "Item[]" notification did not re-read in this Avalonia version).
        return new Binding(nameof(LocalizationService.LanguageTick))
        {
            Mode = BindingMode.OneWay,
            Source = service,
            Converter = LocalizeKeyConverter.Instance,
            ConverterParameter = Key,
        };
    }
}
