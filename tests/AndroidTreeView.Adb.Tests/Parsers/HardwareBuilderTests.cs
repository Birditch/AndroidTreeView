using AndroidTreeView.Adb.Commands;
using AndroidTreeView.Adb.Parsers;
using AndroidTreeView.Models.Hardware;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Parsers;

public class HardwareBuilderTests
{
    private static Dictionary<string, string> Props() => new()
    {
        [PropKeys.AbiList]         = "arm64-v8a,armeabi-v7a,armeabi",
        [PropKeys.BoardPlatform]   = "lito",
        [PropKeys.ProductBoard]    = "redfin",
        [PropKeys.SocManufacturer] = "Qualcomm",
        [PropKeys.SocModel]        = "SM7250"
    };

    private static readonly MemoryInfo Memory = new()
    {
        TotalBytes = 4L * 1024 * 1024 * 1024,
        AvailableBytes = 2L * 1024 * 1024 * 1024
    };

    private static readonly ScreenInfo Screen = new()
    {
        Resolution = "1080x2340",
        DensityDpi = 440
    };

    [Fact]
    public void Build_MapsCpuMemoryScreenAndBoard()
    {
        var cpu = new CpuInfo { Model = "SDM7250", CoreCount = 8, Architecture = null };

        var hardware = HardwareBuilder.Build(Props(), cpu, Memory, Screen);

        Assert.Equal("Qualcomm SM7250", hardware.CpuModel);
        Assert.Equal(8, hardware.CpuCoreCount);
        Assert.Equal(3, hardware.AbiList.Count);
        Assert.Equal("arm64-v8a", hardware.AbiList[0]);
        Assert.Equal(4L * 1024 * 1024 * 1024, hardware.RamTotalBytes);
        Assert.Equal(2L * 1024 * 1024 * 1024, hardware.RamAvailableBytes);
        Assert.Equal("1080x2340", hardware.ScreenResolution);
        Assert.Equal(440, hardware.ScreenDensityDpi);
        Assert.Equal("lito", hardware.HardwarePlatform);
        Assert.Equal("redfin", hardware.BoardName);
        Assert.Null(hardware.Gpu);
    }

    [Fact]
    public void Build_CpuArchitecture_FallsBackToFirstAbi()
    {
        var cpu = new CpuInfo { Architecture = null };

        var hardware = HardwareBuilder.Build(Props(), cpu, Memory, Screen);
        Assert.Equal("arm64-v8a", hardware.CpuArchitecture);
    }

    [Fact]
    public void Build_CpuArchitecture_MapsBareNumberToHumanFriendly()
    {
        // "CPU architecture: 8" from /proc/cpuinfo must become "ARMv8-A", never a bare "8"
        var cpu = new CpuInfo { Architecture = "8" };

        var hardware = HardwareBuilder.Build(Props(), cpu, Memory, Screen);
        Assert.Equal("ARMv8-A", hardware.CpuArchitecture);
    }

    [Fact]
    public void Build_NoAbiList_IsEmpty()
    {
        var hardware = HardwareBuilder.Build(
            new Dictionary<string, string>(),
            new CpuInfo(),
            new MemoryInfo(),
            new ScreenInfo());

        Assert.Empty(hardware.AbiList);
    }

    [Fact]
    public void Build_CpuModel_UsesSocGetpropWhenCpuInfoLacksModel()
    {
        // On ARM devices /proc/cpuinfo has no "model name" line; SoC getprop is the authoritative source.
        var props = new Dictionary<string, string>
        {
            [PropKeys.SocManufacturer] = "Qualcomm",
            [PropKeys.SocModel]        = "SM7635"
        };
        var cpu = new CpuInfo { Model = null };

        var hardware = HardwareBuilder.Build(props, cpu, new MemoryInfo(), new ScreenInfo());

        Assert.Equal("Qualcomm SM7635", hardware.CpuModel);
    }

    [Fact]
    public void Build_CpuArchitecture_PrefersCpuAbiGetpropOverBareNumber()
    {
        // ro.product.cpu.abi takes priority even when cpuinfo carries a raw architecture number.
        var props = new Dictionary<string, string>
        {
            [PropKeys.CpuAbi] = "arm64-v8a"
        };
        var cpu = new CpuInfo { Architecture = "8" };

        var hardware = HardwareBuilder.Build(props, cpu, new MemoryInfo(), new ScreenInfo());

        Assert.Equal("arm64-v8a", hardware.CpuArchitecture);
    }
}
