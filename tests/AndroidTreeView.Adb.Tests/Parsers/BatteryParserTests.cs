using AndroidTreeView.Adb.Parsers;
using AndroidTreeView.Models.Battery;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Parsers;

public class BatteryParserTests
{
    private const string Charging =
        "Current Battery Service state:\n" +
        "  AC powered: false\n" +
        "  USB powered: true\n" +
        "  Wireless powered: false\n" +
        "  Max charging current: 500000\n" +
        "  Charge counter: 2500000\n" +
        "  status: 2\n" +
        "  health: 2\n" +
        "  present: true\n" +
        "  level: 82\n" +
        "  scale: 100\n" +
        "  voltage: 4123\n" +
        "  temperature: 305\n" +
        "  technology: Li-ion\n" +
        "  Cycle count: 145\n";

    private const string Discharging =
        "  AC powered: false\n" +
        "  USB powered: false\n" +
        "  Wireless powered: false\n" +
        "  status: 3\n" +
        "  health: 2\n" +
        "  present: true\n" +
        "  level: 55\n" +
        "  scale: 100\n" +
        "  temperature: 250\n" +
        "  plugged: 0\n";

    [Fact]
    public void Parse_Charging_MapsAllFields()
    {
        var battery = BatteryParser.Parse(Charging);

        Assert.Equal(BatteryStatus.Charging, battery.Status);
        Assert.Equal(BatteryHealth.Good, battery.Health);
        Assert.Equal(BatteryPluggedType.Usb, battery.Plugged);
        Assert.Equal(82, battery.LevelPercent);
        Assert.Equal(82, battery.RawLevel);
        Assert.Equal(100, battery.Scale);
        Assert.Equal(4123, battery.VoltageMillivolts);
        Assert.Equal(30.5, battery.TemperatureCelsius);
        Assert.Equal("Li-ion", battery.Technology);
        Assert.True(battery.Present);
        Assert.Equal(145, battery.CycleCount);
        Assert.True(battery.IsCharging);
    }

    [Fact]
    public void Parse_Discharging_MapsStatusAndPluggedNone()
    {
        var battery = BatteryParser.Parse(Discharging);

        Assert.Equal(BatteryStatus.Discharging, battery.Status);
        Assert.Equal(BatteryPluggedType.None, battery.Plugged);
        Assert.Equal(25.0, battery.TemperatureCelsius);
        Assert.Equal(55, battery.LevelPercent);
        Assert.False(battery.IsCharging);
    }

    [Fact]
    public void Parse_ScaleNotHundred_ComputesPercent()
    {
        var battery = BatteryParser.Parse("  level: 5\n  scale: 10\n");

        Assert.Equal(5, battery.RawLevel);
        Assert.Equal(10, battery.Scale);
        Assert.Equal(50, battery.LevelPercent);
    }

    [Theory]
    [InlineData(1, BatteryStatus.Unknown)]
    [InlineData(2, BatteryStatus.Charging)]
    [InlineData(3, BatteryStatus.Discharging)]
    [InlineData(4, BatteryStatus.NotCharging)]
    [InlineData(5, BatteryStatus.Full)]
    [InlineData(99, BatteryStatus.Unknown)]
    public void Parse_StatusInt_MapsToEnum(int value, BatteryStatus expected)
    {
        var battery = BatteryParser.Parse($"  status: {value}\n");
        Assert.Equal(expected, battery.Status);
    }

    [Theory]
    [InlineData(1, BatteryHealth.Unknown)]
    [InlineData(2, BatteryHealth.Good)]
    [InlineData(3, BatteryHealth.Overheat)]
    [InlineData(4, BatteryHealth.Dead)]
    [InlineData(5, BatteryHealth.OverVoltage)]
    [InlineData(6, BatteryHealth.UnspecifiedFailure)]
    [InlineData(7, BatteryHealth.Cold)]
    public void Parse_HealthInt_MapsToEnum(int value, BatteryHealth expected)
    {
        var battery = BatteryParser.Parse($"  health: {value}\n");
        Assert.Equal(expected, battery.Health);
    }

    [Theory]
    [InlineData(0, BatteryPluggedType.None)]
    [InlineData(1, BatteryPluggedType.Ac)]
    [InlineData(2, BatteryPluggedType.Usb)]
    [InlineData(4, BatteryPluggedType.Wireless)]
    [InlineData(8, BatteryPluggedType.Dock)]
    public void Parse_PluggedInt_MapsToEnum(int value, BatteryPluggedType expected)
    {
        var battery = BatteryParser.Parse($"  plugged: {value}\n");
        Assert.Equal(expected, battery.Plugged);
    }

    [Fact]
    public void Parse_PluggedFromAcPoweredFlag_WhenNoNumericField()
    {
        var battery = BatteryParser.Parse("  AC powered: true\n  USB powered: false\n");
        Assert.Equal(BatteryPluggedType.Ac, battery.Plugged);
    }

    [Fact]
    public void Parse_CycleCountAbsent_IsNull()
    {
        var battery = BatteryParser.Parse(Discharging);
        Assert.Null(battery.CycleCount);
    }

    [Fact]
    public void Parse_FallbackCycleCount_UsedOnlyWhenDumpHasNone()
    {
        var fromFallback = BatteryParser.Parse(Discharging, fallbackCycleCount: 210);
        Assert.Equal(210, fromFallback.CycleCount);
    }

    [Fact]
    public void Parse_DumpCycleCount_WinsOverFallback()
    {
        var battery = BatteryParser.Parse(Charging, fallbackCycleCount: 999);
        Assert.Equal(145, battery.CycleCount);
    }

    [Fact]
    public void Parse_Empty_ReturnsUnknownDefaults()
    {
        var battery = BatteryParser.Parse(string.Empty);

        Assert.Equal(BatteryStatus.Unknown, battery.Status);
        Assert.Equal(BatteryPluggedType.None, battery.Plugged);
        Assert.Null(battery.LevelPercent);
        Assert.Null(battery.CycleCount);
    }
}
