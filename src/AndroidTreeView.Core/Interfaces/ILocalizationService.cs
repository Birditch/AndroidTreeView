using System.Globalization;
using AndroidTreeView.Core.Options;

namespace AndroidTreeView.Core.Interfaces;

/// <summary>
/// Provides localized UI strings and manages the active language. Backed by resource files so more
/// languages can be added without touching Views/ViewModels.
/// </summary>
public interface ILocalizationService
{
    /// <summary>The currently selected language preference.</summary>
    AppLanguage CurrentLanguage { get; }

    /// <summary>The resolved culture used for lookups and formatting.</summary>
    CultureInfo CurrentCulture { get; }

    /// <summary>Raised after the language changes so bindings can refresh.</summary>
    event EventHandler? LanguageChanged;

    /// <summary>Switches the active language and raises <see cref="LanguageChanged"/>.</summary>
    void SetLanguage(AppLanguage language);

    /// <summary>Resolves a localized string for <paramref name="key"/>; returns the key if missing.</summary>
    string Get(string key);

    /// <summary>Resolves and formats a localized string with <paramref name="args"/>.</summary>
    string Format(string key, params object[] args);

    /// <summary>Indexer form of <see cref="Get(string)"/> for convenient binding.</summary>
    string this[string key] { get; }
}
