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
    public static readonly string[] PmListPackages = { "pm", "list", "packages" };

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

    // ── Device-action commands (AdbDeviceActionsService) ─────────────────────

    // Reboot / power (global adb commands, RunInShell = false)
    public static readonly string[] Reboot           = { "reboot" };
    public static readonly string[] RebootRecovery   = { "reboot", "recovery" };
    public static readonly string[] RebootBootloader = { "reboot", "bootloader" };

    // Power-off (shell)
    public static readonly string[] PowerOff = { "reboot", "-p" };

    // FRP removal (shell)
    public static readonly string[] SettingsPutSecureUserSetupComplete =
        { "settings", "put", "secure", "user_setup_complete", "1" };
    public static readonly string[] SettingsPutGlobalDeviceProvisioned =
        { "settings", "put", "global", "device_provisioned", "1" };
    public static readonly string[] SettingsGetSecureUserSetupComplete =
        { "settings", "get", "secure", "user_setup_complete" };
    public static readonly string[] SettingsGetGlobalDeviceProvisioned =
        { "settings", "get", "global", "device_provisioned" };

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

    // ── Screen capture / input / install (ScreenCaptureService) ──────────────

    /// <summary>
    /// The exec-out tokens for <c>adb -s &lt;serial&gt; exec-out screencap -p</c>.
    /// Stdout is a binary PNG; always read via the binary-capable
    /// <c>ProcessRunner.RunBinaryAsync</c> overload.
    /// </summary>
    public static readonly string[] ExecOutScreencap = { "exec-out", "screencap", "-p" };

    /// <summary>Builds an <c>input tap x y</c> shell argument array.</summary>
    public static string[] InputTap(int x, int y)
        => new[] { "input", "tap", x.ToString(), y.ToString() };

    /// <summary>Builds an <c>input swipe x1 y1 x2 y2 duration</c> shell argument array.</summary>
    public static string[] InputSwipe(int x1, int y1, int x2, int y2, int durationMs)
        => new[] { "input", "swipe", x1.ToString(), y1.ToString(), x2.ToString(), y2.ToString(), durationMs.ToString() };

    /// <summary>Builds an <c>input keyevent &lt;code&gt;</c> shell argument array.</summary>
    public static string[] InputKeyEvent(int keyCode) => new[] { "input", "keyevent", keyCode.ToString() };

    /// <summary>Builds an <c>install -r &lt;apkPath&gt;</c> global argument array.</summary>
    public static string[] InstallReplace(string apkPath) => new[] { "install", "-r", apkPath };

    /// <summary>Builds an <c>install -r -d &lt;apkPath&gt;</c> fallback argument array.</summary>
    public static string[] InstallReplaceAllowDowngrade(string apkPath) => new[] { "install", "-r", "-d", apkPath };

    /// <summary>Builds an <c>install -r --no-streaming &lt;apkPath&gt;</c> fallback argument array.</summary>
    public static string[] InstallReplaceNoStreaming(string apkPath) => new[] { "install", "-r", "--no-streaming", apkPath };

    /// <summary>Builds an <c>install -r -d --no-streaming &lt;apkPath&gt;</c> fallback argument array.</summary>
    public static string[] InstallReplaceAllowDowngradeNoStreaming(string apkPath) => new[] { "install", "-r", "-d", "--no-streaming", apkPath };

    /// <summary>Builds an <c>adb push &lt;localPath&gt; &lt;remotePath&gt;</c> global argument array.</summary>
    public static string[] Push(string localPath, string remotePath) => new[] { "push", localPath, remotePath };

    /// <summary>Builds a <c>mkdir -p &lt;path&gt;</c> shell argument array.</summary>
    public static string[] MkdirP(string path) => new[] { "mkdir", "-p", path };
}
