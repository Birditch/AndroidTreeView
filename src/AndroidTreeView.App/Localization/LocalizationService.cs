using System;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Options;

namespace AndroidTreeView.App.Localization;

/// <summary>
/// Resource-backed implementation of <see cref="ILocalizationService"/>. Strings live in
/// <c>Resources/Strings.resx</c> (neutral = English fallback) and <c>Resources/Strings.zh-Hans.resx</c>
/// (Simplified Chinese). The default language at construction is
/// <see cref="AppLanguage.ChineseSimplified"/>. It also raises <see cref="INotifyPropertyChanged"/> for
/// its indexer so that <c>{loc:Localize}</c> bindings refresh live when the language changes.
/// </summary>
public sealed class LocalizationService : ILocalizationService, INotifyPropertyChanged
{
    /// <summary>Base name of the embedded resource set (matches the project root namespace).</summary>
    private const string ResourceBaseName = "AndroidTreeView.App.Resources.Strings";

    private static readonly CultureInfo ChineseCulture = CultureInfo.GetCultureInfo("zh-Hans");
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en");

    private readonly ResourceManager _resourceManager;

    /// <summary>
    /// The active singleton, exposed so <see cref="LocalizeExtension"/> can reach the service from XAML
    /// even before the DI container hands it out. Set from the constructor; may also be assigned at
    /// startup (e.g. from <c>App.Services</c>).
    /// </summary>
    public static LocalizationService? Instance { get; set; }

    public LocalizationService()
    {
        _resourceManager = new ResourceManager(ResourceBaseName, typeof(LocalizationService).Assembly);
        ApplyLanguage(AppLanguage.ChineseSimplified);
        Instance = this;
    }

    /// <inheritdoc />
    public AppLanguage CurrentLanguage { get; private set; } = AppLanguage.ChineseSimplified;

    /// <inheritdoc />
    public CultureInfo CurrentCulture { get; private set; } = ChineseCulture;

    private int _languageTick;

    /// <summary>
    /// Monotonic counter bumped on every language change. <c>{loc:Localize}</c> bindings watch this plain
    /// property (through <see cref="LocalizeKeyConverter"/>) and re-resolve their key against the new
    /// culture — reflection bindings refresh reliably on a normal property's INotifyPropertyChanged,
    /// whereas the indexer ("Item[]") notification did not in this Avalonia version.
    /// </summary>
    public int LanguageTick => _languageTick;

    /// <inheritdoc />
    public event EventHandler? LanguageChanged;

    /// <summary>Raised for the indexer so localized XAML bindings re-read on language change.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc />
    public void SetLanguage(AppLanguage language)
    {
        ApplyLanguage(language);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
        // Bump the plain LanguageTick property; {loc:Localize} bindings watch it and re-resolve their key
        // against the new culture. (A normal property's INotifyPropertyChanged refreshes reflection
        // bindings reliably, unlike the indexer "Item[]" notification used previously.)
        _languageTick++;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LanguageTick)));
    }

    /// <inheritdoc />
    public string Get(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return key ?? string.Empty;
        }

        try
        {
            return _resourceManager.GetString(key, CurrentCulture) ?? key;
        }
        catch (MissingManifestResourceException)
        {
            return key;
        }
        catch (InvalidOperationException)
        {
            return key;
        }
    }

    /// <inheritdoc />
    public string Format(string key, params object[] args)
    {
        var template = Get(key);
        if (args is null || args.Length == 0)
        {
            return template;
        }

        try
        {
            return string.Format(CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    /// <inheritdoc />
    public string this[string key] => Get(key);

    private void ApplyLanguage(AppLanguage language)
    {
        CurrentLanguage = language;
        CurrentCulture = ResolveCulture(language);
        CultureInfo.CurrentUICulture = CurrentCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CurrentCulture;
    }

    private static CultureInfo ResolveCulture(AppLanguage language) => language switch
    {
        AppLanguage.English => EnglishCulture,
        AppLanguage.ChineseSimplified => ChineseCulture,
        AppLanguage.System => ResolveSystemCulture(),
        _ => ChineseCulture,
    };

    private static CultureInfo ResolveSystemCulture()
    {
        var os = CultureInfo.CurrentUICulture;
        var language = os.TwoLetterISOLanguageName;

        if (string.Equals(language, "zh", StringComparison.OrdinalIgnoreCase))
        {
            return ChineseCulture;
        }

        if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            return EnglishCulture;
        }

        // Unsupported OS language: fall back to the application default (Simplified Chinese).
        return ChineseCulture;
    }
}
