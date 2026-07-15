using AndroidTreeView.Models.Rooting;

namespace AndroidTreeView.Adb.Commands;

/// <summary>Argument arrays for strict root-workflow fastboot invocations.</summary>
public static class FastbootArgs
{
    public static readonly string[] DevicesLong = { "devices", "-l" };

    public static string[] GetVar(string serial, string variable)
        => new[] { "-s", Require(serial, nameof(serial)), "getvar", Require(variable, nameof(variable)) };

    public static string[] Flash(string serial, string partition, string imagePath)
        => new[]
        {
            "-s",
            Require(serial, nameof(serial)),
            "flash",
            Require(partition, nameof(partition)),
            Require(imagePath, nameof(imagePath))
        };

    public static string[] Reboot(string serial)
        => new[] { "-s", Require(serial, nameof(serial)), "reboot" };

    public static string PartitionName(BootPartitionTarget target) => target switch
    {
        BootPartitionTarget.Boot => "boot",
        BootPartitionTarget.InitBoot => "init_boot",
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown targets are not flashable.")
    };

    private static string Require(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}
