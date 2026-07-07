using System.Text.Json;
using System.Text.Json.Serialization;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Options;
using AndroidTreeView.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.Infrastructure.Settings;

/// <summary>
/// Persists <see cref="AppSettings"/> as indented JSON under the user's ApplicationData folder.
/// Loads are tolerant (missing/corrupt files yield defaults), writes are atomic and serialized through
/// a <see cref="SemaphoreSlim"/> so concurrent saves cannot interleave.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private const string DirectoryName = "AndroidTreeView";
    private const string FileName = "settings.json";

    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    private readonly JsonFileStore<AppSettings> _store;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public SettingsService(ILogger<SettingsService> logger)
        : this(logger, DefaultSettingsPath())
    {
    }

    internal SettingsService(ILogger<SettingsService> logger, string settingsFilePath)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _store = new JsonFileStore<AppSettings>(settingsFilePath, SerializerOptions, logger);
    }

    /// <inheritdoc />
    public AppSettings Current { get; private set; } = new();

    /// <inheritdoc />
    public event EventHandler<AppSettings>? SettingsChanged;

    /// <inheritdoc />
    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        var loaded = await _store.LoadAsync(static () => new AppSettings(), ct).ConfigureAwait(false);
        Current = loaded.Clone();
        SettingsChanged?.Invoke(this, Current);
        return Current;
    }

    /// <inheritdoc />
    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _store.SaveAsync(settings, ct).ConfigureAwait(false);
            Current = settings.Clone();
        }
        finally
        {
            _writeLock.Release();
        }

        SettingsChanged?.Invoke(this, Current);
    }

    /// <summary>The absolute path settings are persisted to by default.</summary>
    internal static string DefaultSettingsPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        DirectoryName,
        FileName);

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
