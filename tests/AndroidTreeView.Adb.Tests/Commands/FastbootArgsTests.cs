using AndroidTreeView.Adb.Commands;
using AndroidTreeView.Models.Rooting;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Commands;

public sealed class FastbootArgsTests
{
    [Fact]
    public void DeviceCommands_AlwaysCarryExplicitSerial()
    {
        Assert.Equal(new[] { "-s", "SERIAL", "getvar", "unlocked" }, FastbootArgs.GetVar("SERIAL", "unlocked"));
        Assert.Equal(new[] { "-s", "SERIAL", "flash", "init_boot_a", "/tmp/patched.img" },
            FastbootArgs.Flash("SERIAL", "init_boot_a", "/tmp/patched.img"));
        Assert.Equal(new[] { "-s", "SERIAL", "reboot" }, FastbootArgs.Reboot("SERIAL"));
    }

    [Theory]
    [InlineData(BootPartitionTarget.Boot, "boot")]
    [InlineData(BootPartitionTarget.InitBoot, "init_boot")]
    public void PartitionName_MapsOnlySupportedTargets(BootPartitionTarget target, string expected)
        => Assert.Equal(expected, FastbootArgs.PartitionName(target));
}
