using AndroidTreeView.Adb.Parsers;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Parsers;

public class CpuInfoParserTests
{
    private const string ArmCpuInfo =
        "processor\t: 0\n" +
        "BogoMIPS\t: 38.40\n" +
        "Features\t: fp asimd evtstrm aes pmull sha1 sha2 crc32\n" +
        "CPU implementer\t: 0x51\n" +
        "CPU architecture: 8\n" +
        "processor\t: 1\n" +
        "BogoMIPS\t: 38.40\n" +
        "processor\t: 2\n" +
        "processor\t: 3\n" +
        "Hardware\t: Qualcomm Technologies, Inc SDM7250\n";

    [Fact]
    public void Parse_CountsProcessors()
    {
        var cpu = CpuInfoParser.Parse(ArmCpuInfo);
        Assert.Equal(4, cpu.CoreCount);
    }

    [Fact]
    public void Parse_ReadsHardwareArchitectureAndFeatures()
    {
        var cpu = CpuInfoParser.Parse(ArmCpuInfo);

        Assert.Equal("Qualcomm Technologies, Inc SDM7250", cpu.Hardware);
        Assert.Equal("8", cpu.Architecture);
        Assert.Contains("aes", cpu.Features);
        Assert.Equal(8, cpu.Features.Count);
    }

    [Fact]
    public void Parse_ModelFallsBackToHardware_WhenNoModelName()
    {
        var cpu = CpuInfoParser.Parse(ArmCpuInfo);
        Assert.Equal("Qualcomm Technologies, Inc SDM7250", cpu.Model);
    }

    [Fact]
    public void Parse_PrefersModelName_WhenPresent()
    {
        const string x86 =
            "processor\t: 0\n" +
            "model name\t: Intel(R) Core(TM) i7\n" +
            "Hardware\t: board\n";

        var cpu = CpuInfoParser.Parse(x86);
        Assert.Equal("Intel(R) Core(TM) i7", cpu.Model);
    }

    [Fact]
    public void Parse_Empty_ReturnsEmptyCpu()
    {
        var cpu = CpuInfoParser.Parse(string.Empty);
        Assert.Null(cpu.CoreCount);
        Assert.Empty(cpu.Features);
    }
}
