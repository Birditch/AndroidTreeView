using AndroidTreeView.Core.Options;
using AndroidTreeView.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AndroidTreeView.Infrastructure.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _directory;
    private readonly string _settingsPath;

    public SettingsServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "AndroidTreeViewTests", Guid.NewGuid().ToString("N"));
        _settingsPath = Path.Combine(_directory, "settings.json");
    }

    private SettingsService CreateService() =>
        new(NullLogger<SettingsService>.Instance, _settingsPath);

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsAllFields()
    {
        var service = CreateService();
        var settings = new AppSettings
        {
            AdbPath = @"C:\tools\adb.exe",
            Theme = ThemeMode.Dark,
            AutoRefreshEnabled = false,
            DeviceRefreshIntervalSeconds = 7,
            BatteryRefreshIntervalSeconds = 42,
            LogcatMaxLines = 1234,
            Startup = StartupBehavior.StartMinimized,
            RememberLastSelectedDevice = false,
            LastSelectedSerial = "SERIAL123",
            Language = AppLanguage.English,
            AutoCheckUpdates = false,
            AccentColor = "#0A84FF",
        };

        await service.SaveAsync(settings);

        var reloaded = await CreateService().LoadAsync();

        Assert.Equal(settings.AdbPath, reloaded.AdbPath);
        Assert.Equal(settings.Theme, reloaded.Theme);
        Assert.Equal(settings.AutoRefreshEnabled, reloaded.AutoRefreshEnabled);
        Assert.Equal(settings.DeviceRefreshIntervalSeconds, reloaded.DeviceRefreshIntervalSeconds);
        Assert.Equal(settings.BatteryRefreshIntervalSeconds, reloaded.BatteryRefreshIntervalSeconds);
        Assert.Equal(settings.LogcatMaxLines, reloaded.LogcatMaxLines);
        Assert.Equal(settings.Startup, reloaded.Startup);
        Assert.Equal(settings.RememberLastSelectedDevice, reloaded.RememberLastSelectedDevice);
        Assert.Equal(settings.LastSelectedSerial, reloaded.LastSelectedSerial);
        Assert.Equal(settings.Language, reloaded.Language);
        Assert.Equal(settings.AutoCheckUpdates, reloaded.AutoCheckUpdates);
        Assert.Equal(settings.AccentColor, reloaded.AccentColor);
    }

    [Fact]
    public async Task SaveAsync_WritesIndentedJsonWithEnumNames()
    {
        var service = CreateService();

        await service.SaveAsync(new AppSettings { Theme = ThemeMode.Dark, Language = AppLanguage.English });

        var json = await File.ReadAllTextAsync(_settingsPath);
        Assert.Contains("\"Dark\"", json, StringComparison.Ordinal);
        Assert.Contains("\"English\"", json, StringComparison.Ordinal);
        Assert.Contains("\n", json, StringComparison.Ordinal); // WriteIndented emits line breaks
    }

    [Fact]
    public async Task SaveAsync_UpdatesCurrentWithIndependentCopy()
    {
        var service = CreateService();
        var settings = new AppSettings { AdbPath = "before" };

        await service.SaveAsync(settings);
        settings.AdbPath = "after";

        Assert.Equal("before", service.Current.AdbPath);
        Assert.NotSame(settings, service.Current);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsDefaults()
    {
        Assert.False(File.Exists(_settingsPath));

        var loaded = await CreateService().LoadAsync();

        var defaults = new AppSettings();
        Assert.Equal(defaults.Theme, loaded.Theme);
        Assert.Equal(defaults.Language, loaded.Language);
        Assert.Equal(defaults.DeviceRefreshIntervalSeconds, loaded.DeviceRefreshIntervalSeconds);
        Assert.Equal(defaults.AutoCheckUpdates, loaded.AutoCheckUpdates);
        Assert.Null(loaded.AdbPath);
    }

    [Fact]
    public async Task LoadAsync_CorruptJson_ReturnsDefaultsWithoutThrowing()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(_settingsPath, "{ this is not valid json ]]");

        var loaded = await CreateService().LoadAsync();

        Assert.Equal(new AppSettings().Theme, loaded.Theme);
        Assert.Equal(AppLanguage.ChineseSimplified, loaded.Language);
    }

    [Fact]
    public async Task LoadAsync_EmptyFile_ReturnsDefaults()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(_settingsPath, string.Empty);

        var loaded = await CreateService().LoadAsync();

        Assert.Equal(AppLanguage.ChineseSimplified, loaded.Language);
    }

    [Fact]
    public async Task SaveAsync_RaisesSettingsChanged()
    {
        var service = CreateService();
        AppSettings? captured = null;
        service.SettingsChanged += (_, s) => captured = s;

        await service.SaveAsync(new AppSettings { AccentColor = "#FF0000" });

        Assert.NotNull(captured);
        Assert.Equal("#FF0000", captured!.AccentColor);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort temp cleanup; ignore transient file locks.
        }
    }
}
