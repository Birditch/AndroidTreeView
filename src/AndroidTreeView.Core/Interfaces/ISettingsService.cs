using AndroidTreeView.Core.Options;

namespace AndroidTreeView.Core.Interfaces;

/// <summary>
/// Loads and persists <see cref="AppSettings"/> and notifies subscribers of changes.
/// </summary>
public interface ISettingsService
{
    /// <summary>The currently loaded settings.</summary>
    AppSettings Current { get; }

    /// <summary>Raised after settings are loaded or saved.</summary>
    event EventHandler<AppSettings>? SettingsChanged;

    /// <summary>Loads settings from storage, falling back to defaults when missing/corrupt.</summary>
    Task<AppSettings> LoadAsync(CancellationToken ct = default);

    /// <summary>Persists the supplied settings.</summary>
    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
}
