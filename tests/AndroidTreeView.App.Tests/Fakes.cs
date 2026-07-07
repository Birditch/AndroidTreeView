using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Options;
using AndroidTreeView.Models;
using AndroidTreeView.Models.Battery;
using AndroidTreeView.Models.Devices;
using AndroidTreeView.Models.Hardware;
using AndroidTreeView.Models.Logs;
using AndroidTreeView.Models.Network;
using AndroidTreeView.Models.Storage;
using AndroidTreeView.Models.System;

namespace AndroidTreeView.App.Tests;

/// <summary>
/// Deterministic <see cref="ILocalizationService"/> double: <see cref="Get"/> echoes the key so tests can
/// assert on the exact localization key that a view model chose, independent of the resx contents.
/// </summary>
internal sealed class FakeLocalizationService : ILocalizationService
{
    public AppLanguage CurrentLanguage { get; private set; } = AppLanguage.English;

    public CultureInfo CurrentCulture { get; } = CultureInfo.InvariantCulture;

    public event EventHandler? LanguageChanged;

    public void SetLanguage(AppLanguage language)
    {
        CurrentLanguage = language;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Get(string key) => key;

    public string Format(string key, params object[] args)
    {
        if (args is null || args.Length == 0)
        {
            return key;
        }

        try
        {
            return string.Format(CurrentCulture, key, args);
        }
        catch (FormatException)
        {
            return key;
        }
    }

    public string this[string key] => Get(key);
}

/// <summary>
/// Minimal <see cref="ISettingsService"/> double returning a fixed <see cref="AppSettings"/> snapshot.
/// </summary>
internal sealed class FakeSettingsService : ISettingsService
{
    public FakeSettingsService(AppSettings? settings = null) => Current = settings ?? new AppSettings();

    public AppSettings Current { get; private set; }

    public event EventHandler<AppSettings>? SettingsChanged;

    public Task<AppSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(Current);

    public Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        Current = settings;
        SettingsChanged?.Invoke(this, settings);
        return Task.CompletedTask;
    }
}

/// <summary>
/// <see cref="IDeviceService"/> double. Only <see cref="GetPropertiesAsync"/> is exercised by the tests;
/// the remaining members are intentionally unimplemented and throw if a test path reaches them.
/// </summary>
internal sealed class FakeDeviceService : IDeviceService
{
    private readonly DeviceProperties _properties;

    public FakeDeviceService(DeviceProperties? properties = null) =>
        _properties = properties ?? new DeviceProperties();

    public int PropertiesCallCount { get; private set; }

    public Task<DeviceProperties> GetPropertiesAsync(string serial, CancellationToken ct = default)
    {
        PropertiesCallCount++;
        return Task.FromResult(_properties);
    }

    public Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<DeviceOverview> GetOverviewAsync(string serial, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<HardwareInfo> GetHardwareAsync(string serial, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<BatteryInfo> GetBatteryAsync(string serial, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<SystemInfo> GetSystemInfoAsync(string serial, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<StorageInfo> GetStorageAsync(string serial, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<NetworkInfo> GetNetworkAsync(string serial, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<RootStatus> GetRootStatusAsync(string serial, CancellationToken ct = default) =>
        throw new NotSupportedException();
}

/// <summary>
/// <see cref="ILogcatService"/> double that yields a fixed set of entries. When
/// <see cref="StreamForever"/> is true it keeps yielding until cancellation (for stop/cancel tests).
/// </summary>
internal sealed class FakeLogcatService : ILogcatService
{
    private readonly IReadOnlyList<LogcatEntry> _entries;
    private readonly bool _streamForever;

    public FakeLogcatService(IReadOnlyList<LogcatEntry> entries, bool streamForever = false)
    {
        _entries = entries;
        _streamForever = streamForever;
    }

    public bool StreamForever => _streamForever;

    public int ClearCallCount { get; private set; }

    public async IAsyncEnumerable<LogcatEntry> StreamAsync(
        string serial,
        LogcatOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var entry in _entries)
        {
            ct.ThrowIfCancellationRequested();
            yield return entry;
            await Task.Yield();
        }

        while (_streamForever)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(20, ct).ConfigureAwait(false);
        }
    }

    public Task ClearAsync(string serial, CancellationToken ct = default)
    {
        ClearCallCount++;
        return Task.CompletedTask;
    }
}
