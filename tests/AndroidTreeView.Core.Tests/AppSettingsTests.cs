using System.Text.Json;
using System.Text.Json.Serialization;
using AndroidTreeView.Core.Options;
using Xunit;

namespace AndroidTreeView.Core.Tests;

public sealed class AppSettingsTests
{
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    [Fact]
    public void Clone_ProducesEqualButIndependentCopy()
    {
        var original = new AppSettings
        {
            AdbPath = "adb",
            Theme = ThemeMode.Light,
            AutoRefreshEnabled = false,
            DeviceRefreshIntervalSeconds = 9,
            BatteryRefreshIntervalSeconds = 11,
            LogcatMaxLines = 999,
            Startup = StartupBehavior.RememberWindow,
            RememberLastSelectedDevice = false,
            LastSelectedSerial = "abc",
            Language = AppLanguage.English,
            AutoCheckUpdates = false,
            AccentColor = "#123456",
        };

        var clone = original.Clone();

        Assert.NotSame(original, clone);
        Assert.Equal(original.AdbPath, clone.AdbPath);
        Assert.Equal(original.Theme, clone.Theme);
        Assert.Equal(original.AutoRefreshEnabled, clone.AutoRefreshEnabled);
        Assert.Equal(original.DeviceRefreshIntervalSeconds, clone.DeviceRefreshIntervalSeconds);
        Assert.Equal(original.BatteryRefreshIntervalSeconds, clone.BatteryRefreshIntervalSeconds);
        Assert.Equal(original.LogcatMaxLines, clone.LogcatMaxLines);
        Assert.Equal(original.Startup, clone.Startup);
        Assert.Equal(original.RememberLastSelectedDevice, clone.RememberLastSelectedDevice);
        Assert.Equal(original.LastSelectedSerial, clone.LastSelectedSerial);
        Assert.Equal(original.Language, clone.Language);
        Assert.Equal(original.AutoCheckUpdates, clone.AutoCheckUpdates);
        Assert.Equal(original.AccentColor, clone.AccentColor);
    }

    [Fact]
    public void Clone_MutatingCopy_DoesNotAffectOriginal()
    {
        var original = new AppSettings { AdbPath = "before", Theme = ThemeMode.System };
        var clone = original.Clone();

        clone.AdbPath = "after";
        clone.Theme = ThemeMode.Dark;
        clone.AutoCheckUpdates = false;

        Assert.Equal("before", original.AdbPath);
        Assert.Equal(ThemeMode.System, original.Theme);
        Assert.True(original.AutoCheckUpdates);
    }

    [Fact]
    public void Defaults_MatchContract()
    {
        var settings = new AppSettings();

        Assert.Null(settings.AdbPath);
        Assert.Equal(ThemeMode.System, settings.Theme);
        Assert.True(settings.AutoRefreshEnabled);
        Assert.Equal(3, settings.DeviceRefreshIntervalSeconds);
        Assert.Equal(10, settings.BatteryRefreshIntervalSeconds);
        Assert.Equal(5000, settings.LogcatMaxLines);
        Assert.Equal(StartupBehavior.Normal, settings.Startup);
        Assert.True(settings.RememberLastSelectedDevice);
        Assert.Equal(AppLanguage.ChineseSimplified, settings.Language);
        Assert.True(settings.AutoCheckUpdates);
        Assert.Null(settings.AccentColor);
    }

    [Fact]
    public void JsonRoundTrip_PreservesValuesAndUsesEnumNames()
    {
        var options = CreateOptions();
        var settings = new AppSettings
        {
            Theme = ThemeMode.Dark,
            Language = AppLanguage.English,
            Startup = StartupBehavior.StartMinimized,
            AutoCheckUpdates = false,
            AccentColor = "#0A84FF",
            DeviceRefreshIntervalSeconds = 7,
            LastSelectedSerial = "SN-1",
        };

        var json = JsonSerializer.Serialize(settings, options);
        Assert.Contains("\"Dark\"", json, StringComparison.Ordinal);
        Assert.Contains("\"English\"", json, StringComparison.Ordinal);

        var restored = JsonSerializer.Deserialize<AppSettings>(json, options);

        Assert.NotNull(restored);
        Assert.Equal(settings.Theme, restored!.Theme);
        Assert.Equal(settings.Language, restored.Language);
        Assert.Equal(settings.Startup, restored.Startup);
        Assert.Equal(settings.AutoCheckUpdates, restored.AutoCheckUpdates);
        Assert.Equal(settings.AccentColor, restored.AccentColor);
        Assert.Equal(settings.DeviceRefreshIntervalSeconds, restored.DeviceRefreshIntervalSeconds);
        Assert.Equal(settings.LastSelectedSerial, restored.LastSelectedSerial);
    }
}
