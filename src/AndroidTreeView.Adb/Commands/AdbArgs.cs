using AndroidTreeView.Models.Logs;

namespace AndroidTreeView.Adb.Commands;

/// <summary>
/// Argument arrays / builders for every adb invocation used by the services.
/// These map one-to-one onto the command set documented in the architecture contract (§7.4).
/// </summary>
public static class AdbArgs
{
    // Global (non-device) commands
    public static readonly string[] Devices = { "devices", "-l" };
    public static readonly string[] Version = { "version" };

    // Device shell commands
    public static readonly string[] GetProp = { "getprop" };
    public static readonly string[] DumpsysBattery = { "dumpsys", "battery" };
    public static readonly string[] CatCpuInfo = { "cat", "/proc/cpuinfo" };
    public static readonly string[] CatMemInfo = { "cat", "/proc/meminfo" };
    public static readonly string[] CatProcVersion = { "cat", "/proc/version" };
    public static readonly string[] WmSize = { "wm", "size" };
    public static readonly string[] WmDensity = { "wm", "density" };
    public static readonly string[] Df = { "df" };
    public static readonly string[] Getenforce = { "getenforce" };
    public static readonly string[] Uptime = { "uptime" };
    public static readonly string[] Id = { "id" };
    public static readonly string[] WhichSu = { "which", "su" };
    public static readonly string[] CommandVSu = { "command", "-v", "su" };
    public static readonly string[] SuId = { "su", "-c", "id" };
    public static readonly string[] IpAddr = { "ip", "addr" };
    public static readonly string[] Ifconfig = { "ifconfig" };
    public static readonly string[] MagiskClientVersion = { "magisk", "-c" };

    // Device (adb-level, not shell) commands
    public static readonly string[] LogcatClear = { "logcat", "-c" };

    /// <summary>Candidate sysfs paths for the (non-root) battery cycle count.</summary>
    public static readonly string[] CycleCountPaths =
    {
        "/sys/class/power_supply/battery/cycle_count",
        "/sys/class/power_supply/bms/cycle_count",
        "/sys/class/power_supply/Battery/cycle_count"
    };

    /// <summary>The sysfs path exposing the Wi-Fi interface MAC address (readable without root).</summary>
    public const string WlanMacPath = "/sys/class/net/wlan0/address";

    /// <summary>Builds a <c>cat &lt;path&gt;</c> shell argument array.</summary>
    public static string[] Cat(string path) => new[] { "cat", path };

    /// <summary>
    /// Builds the <c>logcat -v threadtime</c> argument array, appending a
    /// <c>*:&lt;priority&gt;</c> spec when a minimum priority above Verbose is requested,
    /// and an optional tag filter.
    /// </summary>
    public static string[] Logcat(LogPriority minPriority, string? tagFilter = null)
    {
        var args = new List<string> { "logcat", "-v", "threadtime" };

        if (!string.IsNullOrWhiteSpace(tagFilter))
        {
            args.Add($"{tagFilter}:{PriorityChar(minPriority)}");
            args.Add("*:S");
            return args.ToArray();
        }

        if (minPriority is not LogPriority.Unknown and not LogPriority.Verbose)
        {
            args.Add($"*:{PriorityChar(minPriority)}");
        }

        return args.ToArray();
    }

    /// <summary>Maps a <see cref="LogPriority"/> to the single-letter code used by logcat filters.</summary>
    public static char PriorityChar(LogPriority priority) => priority switch
    {
        LogPriority.Verbose => 'V',
        LogPriority.Debug => 'D',
        LogPriority.Info => 'I',
        LogPriority.Warn => 'W',
        LogPriority.Error => 'E',
        LogPriority.Fatal => 'F',
        LogPriority.Silent => 'S',
        _ => 'V'
    };
}
