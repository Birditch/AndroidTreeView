using AndroidTreeView.Adb.Commands;
using AndroidTreeView.Adb.Parsers;
using AndroidTreeView.Models.System;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Parsers;

public class SystemInfoBuilderTests
{
    [Fact]
    public void Build_MapsPropsKernelSelinuxAndUptime()
    {
        var props = new Dictionary<string, string>
        {
            [PropKeys.Bootloader] = "SLIDER-1.0-8748789",
            [PropKeys.VerifiedBootState] = "green",
            [PropKeys.BuildTags] = "release-keys",
            [PropKeys.BuildType] = "user",
            [PropKeys.LocalePersist] = "en-US",
            [PropKeys.Timezone] = "America/New_York",
            [PropKeys.Sdk] = "33"
        };

        var info = SystemInfoBuilder.Build(props, "5.10.101", "Enforcing", TimeSpan.FromHours(5));

        Assert.Equal("5.10.101", info.KernelVersion);
        Assert.Equal("Enforcing", info.SelinuxStatus);
        Assert.Equal(TimeSpan.FromHours(5), info.Uptime);
        Assert.Equal("SLIDER-1.0-8748789", info.Bootloader);
        Assert.Equal("green", info.VerifiedBootState);
        Assert.Equal("release-keys", info.BuildTags);
        Assert.Equal("user", info.BuildType);
        Assert.Equal("en-US", info.Locale);
        Assert.Equal("America/New_York", info.Timezone);
        Assert.Equal(33, info.SdkVersion);
    }

    [Fact]
    public void Build_Locale_FallsBackToProductLocale()
    {
        var props = new Dictionary<string, string>
        {
            [PropKeys.LocaleProduct] = "zh-CN"
        };

        var info = SystemInfoBuilder.Build(props, null, null, null);
        Assert.Equal("zh-CN", info.Locale);
    }

    [Fact]
    public void ParseKernel_ExtractsVersionToken()
    {
        const string procVersion =
            "Linux version 4.19.113-perf+ (build@host) (clang version 12.0.5) #1 SMP PREEMPT";

        Assert.Equal("4.19.113-perf+", SystemInfoBuilder.ParseKernel(procVersion));
    }

    [Fact]
    public void ParseKernel_NullOrGarbage_ReturnsNull()
    {
        Assert.Null(SystemInfoBuilder.ParseKernel(null));
        Assert.Null(SystemInfoBuilder.ParseKernel("no linux here"));
    }

    [Fact]
    public void ParseUptime_ProcUptimeSeconds()
    {
        var uptime = SystemInfoBuilder.ParseUptime("123456.78 654321.00");
        Assert.Equal(TimeSpan.FromSeconds(123456.78), uptime);
    }

    [Fact]
    public void ParseUptime_DaysHoursMinutes()
    {
        var uptime = SystemInfoBuilder.ParseUptime(
            " 14:23:01 up 3 days,  2:45,  1 user,  load average: 0.52, 0.44, 0.33");

        Assert.Equal(new TimeSpan(3, 2, 45, 0), uptime);
    }

    [Fact]
    public void ParseUptime_MinutesOnly()
    {
        var uptime = SystemInfoBuilder.ParseUptime(
            " 10:00:00 up 5 min,  0 users,  load average: 0.00, 0.00, 0.00");

        Assert.Equal(TimeSpan.FromMinutes(5), uptime);
    }

    [Fact]
    public void ParseUptime_HoursMinutesNoDays()
    {
        var uptime = SystemInfoBuilder.ParseUptime(" 10:00:00 up  2:03,  0 users");
        Assert.Equal(new TimeSpan(2, 3, 0), uptime);
    }

    [Fact]
    public void ParseUptime_NullOrGarbage_ReturnsNull()
    {
        Assert.Null(SystemInfoBuilder.ParseUptime(null));
        Assert.Null(SystemInfoBuilder.ParseUptime("nonsense text"));
    }
}
