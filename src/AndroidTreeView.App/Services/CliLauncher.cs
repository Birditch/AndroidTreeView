using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AndroidTreeView.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.App.Services;

/// <summary>Opens a menu-driven CLI terminal scoped to a device (adb for online devices, fastboot for bootloader).</summary>
public interface ICliLauncher
{
    /// <summary>Opens a terminal with a numbered menu of universal, non-destructive operations for the device.</summary>
    void Open(string serial, string title, bool fastboot);

    /// <summary>Closes every terminal this app launched.</summary>
    void ShutdownAll();
}

/// <summary>
/// Writes a small menu <c>.bat</c> (in the spirit of Tiny-Fastboot-Script, but only universal,
/// non-destructive operations — no OEM flashing / wipe / unlock) and launches it in a console with the
/// bundled adb + fastboot on PATH. This is how the user drives a device from the "CLI" right-click item;
/// for a fastboot device it's the only way to interact (no mirrorable screen).
/// </summary>
public sealed class CliLauncher : ICliLauncher
{
    private readonly IAdbEnvironment _environment;
    private readonly ILocalizationService _localization;
    private readonly ILogger<CliLauncher> _logger;
    private readonly List<Process> _processes = new();
    private readonly object _gate = new();

    public CliLauncher(IAdbEnvironment environment, ILocalizationService localization, ILogger<CliLauncher> logger)
    {
        _environment = environment;
        _localization = localization;
        _logger = logger;
    }

    public void Open(string serial, string title, bool fastboot)
    {
        try
        {
            var toolsDir = ResolveToolsDir() ?? AppContext.BaseDirectory;
            var safe = new string(serial.Where(char.IsLetterOrDigit).ToArray());
            if (safe.Length == 0)
            {
                safe = "device";
            }

            if (OperatingSystem.IsWindows())
            {
                OpenWindows(serial, safe, toolsDir, fastboot);
            }
            else if (OperatingSystem.IsMacOS())
            {
                OpenMac(serial, safe, toolsDir, fastboot);
            }
            else
            {
                Notifier.Notify(_localization.Get("cli.unsupported"), NotifierLevel.Warning);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open CLI terminal for {Serial}.", serial);
            Notifier.Notify(_localization.Get("cli.launchFailed"), NotifierLevel.Error);
        }
    }

    // Windows: write a menu .bat and open it in a cmd.exe /K console.
    private void OpenWindows(string serial, string safe, string toolsDir, bool fastboot)
    {
        var script = fastboot ? BuildFastbootMenu(serial, toolsDir) : BuildAdbMenu(serial, toolsDir);
        var batPath = Path.Combine(Path.GetTempPath(), $"atv-cli-{safe}.bat");
        File.WriteAllText(batPath, script);

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/K \"{batPath}\"",
            WorkingDirectory = toolsDir,
            UseShellExecute = true, // reliably opens a visible console window from a GUI process
        };

        var process = Process.Start(psi);
        if (process is not null)
        {
            Track(process);
        }
    }

    // macOS: write an equivalent .command shell script and open it in Terminal.app.
    // Note: `open` returns immediately and the Terminal window is an independent process, so
    // ShutdownAll() cannot close it — that's intentional; we don't force-close a user's terminal.
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private void OpenMac(string serial, string safe, string toolsDir, bool fastboot)
    {
        var script = fastboot ? BuildFastbootMenuSh(serial, toolsDir) : BuildAdbMenuSh(serial, toolsDir);
        var scriptPath = Path.Combine(Path.GetTempPath(), $"atv-cli-{safe}.command");
        File.WriteAllText(scriptPath, script);
        File.SetUnixFileMode(
            scriptPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        var psi = new ProcessStartInfo
        {
            FileName = "open",
            WorkingDirectory = toolsDir,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-a");
        psi.ArgumentList.Add("Terminal.app");
        psi.ArgumentList.Add(scriptPath);

        var process = Process.Start(psi);
        if (process is not null)
        {
            Track(process);
        }
    }

    public void ShutdownAll()
    {
        List<Process> processes;
        lock (_gate)
        {
            processes = _processes.ToList();
            _processes.Clear();
        }

        foreach (var process in processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to close a CLI terminal on shutdown.");
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private void Track(Process process)
    {
        lock (_gate)
        {
            _processes.Add(process);
        }

        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => Untrack(process);

            if (process.HasExited)
            {
                Untrack(process);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not observe CLI terminal exit.");
        }
    }

    private void Untrack(Process process)
    {
        lock (_gate)
        {
            _processes.Remove(process);
        }
    }

    // Fastboot menu: universal + non-destructive only (info / reboots / power / A-B slot / raw shell).
    private static string BuildFastbootMenu(string serial, string toolsDir) => Join(new[]
    {
        "@echo off",
        "chcp 65001>nul",
        // Grab the ESC char so we can print ANSI colors (cyan headers / green numbers / yellow values).
        "for /f %%a in ('echo prompt $E ^| cmd') do set \"E=%%a\"",
        "set \"H=%E%[96m\"",
        "set \"N=%E%[92m\"",
        "set \"V=%E%[93m\"",
        "set \"R=%E%[0m\"",
        $"set \"PATH={toolsDir};%PATH%\"",
        $"set \"SERIAL={serial}\"",
        "title AndroidTreeView Fastboot CLI - %SERIAL%",
        ":menu",
        "cls",
        "echo %H%================ AndroidTreeView Fastboot CLI ================%R%",
        "echo   设备 Device: %V%%SERIAL%%R%   (fastboot / bootloader)",
        "echo %H%-------------------------------------------------------------%R%",
        "echo   %N%[1]%R% 设备信息  Device info (getvar all)",
        "echo   %N%[2]%R% 重启到系统  Reboot to system",
        "echo   %N%[3]%R% 重启到 Bootloader  Reboot to bootloader",
        "echo   %N%[4]%R% 重启到 Recovery  Reboot to recovery",
        "echo   %N%[5]%R% 关机  Power off",
        "echo   %N%[6]%R% 切换到 A 槽  Set active slot A",
        "echo   %N%[7]%R% 切换到 B 槽  Set active slot B",
        "echo   %N%[9]%R% 原生命令行  Raw CLI (adb + fastboot on PATH)",
        "echo   %N%[0]%R% 退出  Exit",
        "echo %H%-------------------------------------------------------------%R%",
        "set \"sel=\"",
        "set /p sel=\" %H%输入序号并回车 Enter choice >%R% \"",
        "if \"%sel%\"==\"1\" goto info",
        "if \"%sel%\"==\"2\" goto rsys",
        "if \"%sel%\"==\"3\" goto rbl",
        "if \"%sel%\"==\"4\" goto rrec",
        "if \"%sel%\"==\"5\" goto poff",
        "if \"%sel%\"==\"6\" goto slota",
        "if \"%sel%\"==\"7\" goto slotb",
        "if \"%sel%\"==\"9\" goto raw",
        "if \"%sel%\"==\"0\" goto end",
        "goto menu",
        ":info",
        "fastboot -s %SERIAL% getvar all",
        "pause",
        "goto menu",
        ":rsys",
        "fastboot -s %SERIAL% reboot",
        "pause",
        "goto menu",
        ":rbl",
        "fastboot -s %SERIAL% reboot bootloader",
        "pause",
        "goto menu",
        ":rrec",
        "fastboot -s %SERIAL% reboot recovery",
        "pause",
        "goto menu",
        ":poff",
        "fastboot -s %SERIAL% oem poweroff",
        "pause",
        "goto menu",
        ":slota",
        "fastboot -s %SERIAL% set_active a",
        "pause",
        "goto menu",
        ":slotb",
        "fastboot -s %SERIAL% set_active b",
        "pause",
        "goto menu",
        ":raw",
        "echo adb / fastboot 已在 PATH。输入 exit 返回菜单 (type exit to return).",
        "cmd /k",
        "goto menu",
        ":end",
        "exit",
    });

    // adb menu for an online device: info / shell / logcat / reboots / power / raw shell.
    private static string BuildAdbMenu(string serial, string toolsDir) => Join(new[]
    {
        "@echo off",
        "chcp 65001>nul",
        "for /f %%a in ('echo prompt $E ^| cmd') do set \"E=%%a\"",
        "set \"H=%E%[96m\"",
        "set \"N=%E%[92m\"",
        "set \"V=%E%[93m\"",
        "set \"R=%E%[0m\"",
        $"set \"PATH={toolsDir};%PATH%\"",
        $"set \"SERIAL={serial}\"",
        "title AndroidTreeView ADB CLI - %SERIAL%",
        ":menu",
        "cls",
        "echo %H%================== AndroidTreeView ADB CLI ==================%R%",
        "echo   设备 Device: %V%%SERIAL%%R%",
        "echo %H%------------------------------------------------------------%R%",
        "echo   %N%[1]%R% 设备信息  Device info (getprop)",
        "echo   %N%[2]%R% 进入 Shell  adb shell",
        "echo   %N%[3]%R% 查看日志  Logcat  (Ctrl+C 停止)",
        "echo   %N%[4]%R% 重启  Reboot",
        "echo   %N%[5]%R% 重启到 Bootloader  Reboot to bootloader",
        "echo   %N%[6]%R% 重启到 Recovery  Reboot to recovery",
        "echo   %N%[7]%R% 关机  Power off",
        "echo   %N%[9]%R% 原生命令行  Raw CLI (adb + fastboot on PATH)",
        "echo   %N%[0]%R% 退出  Exit",
        "echo %H%------------------------------------------------------------%R%",
        "set \"sel=\"",
        "set /p sel=\" %H%输入序号并回车 Enter choice >%R% \"",
        "if \"%sel%\"==\"1\" goto info",
        "if \"%sel%\"==\"2\" goto shell",
        "if \"%sel%\"==\"3\" goto logcat",
        "if \"%sel%\"==\"4\" goto reboot",
        "if \"%sel%\"==\"5\" goto rbl",
        "if \"%sel%\"==\"6\" goto rrec",
        "if \"%sel%\"==\"7\" goto poff",
        "if \"%sel%\"==\"9\" goto raw",
        "if \"%sel%\"==\"0\" goto end",
        "goto menu",
        ":info",
        "adb -s %SERIAL% shell getprop",
        "pause",
        "goto menu",
        ":shell",
        "adb -s %SERIAL% shell",
        "goto menu",
        ":logcat",
        "adb -s %SERIAL% logcat",
        "goto menu",
        ":reboot",
        "adb -s %SERIAL% reboot",
        "pause",
        "goto menu",
        ":rbl",
        "adb -s %SERIAL% reboot bootloader",
        "pause",
        "goto menu",
        ":rrec",
        "adb -s %SERIAL% reboot recovery",
        "pause",
        "goto menu",
        ":poff",
        "adb -s %SERIAL% shell reboot -p",
        "pause",
        "goto menu",
        ":raw",
        "echo adb / fastboot 已在 PATH。输入 exit 返回菜单 (type exit to return).",
        "cmd /k",
        "goto menu",
        ":end",
        "exit",
    });

    // macOS fastboot menu — bash equivalent of BuildFastbootMenu (universal + non-destructive only).
    private static string BuildFastbootMenuSh(string serial, string toolsDir) => JoinLf(new[]
    {
        "#!/bin/bash",
        // ANSI colors (cyan headers / green numbers / yellow values) matching the Windows menu.
        "H=$'\\033[96m'; N=$'\\033[92m'; V=$'\\033[93m'; R=$'\\033[0m'",
        $"export PATH=\"{ShEscape(toolsDir)}:$PATH\"",
        $"SERIAL=\"{ShEscape(serial)}\"",
        "while true; do",
        "  clear",
        "  echo \"${H}================ AndroidTreeView Fastboot CLI ================${R}\"",
        "  echo \"  设备 Device: ${V}${SERIAL}${R}   (fastboot / bootloader)\"",
        "  echo \"${H}-------------------------------------------------------------${R}\"",
        "  echo \"  ${N}[1]${R} 设备信息  Device info (getvar all)\"",
        "  echo \"  ${N}[2]${R} 重启到系统  Reboot to system\"",
        "  echo \"  ${N}[3]${R} 重启到 Bootloader  Reboot to bootloader\"",
        "  echo \"  ${N}[4]${R} 重启到 Recovery  Reboot to recovery\"",
        "  echo \"  ${N}[5]${R} 关机  Power off\"",
        "  echo \"  ${N}[6]${R} 切换到 A 槽  Set active slot A\"",
        "  echo \"  ${N}[7]${R} 切换到 B 槽  Set active slot B\"",
        "  echo \"  ${N}[9]${R} 原生命令行  Raw CLI (adb + fastboot on PATH)\"",
        "  echo \"  ${N}[0]${R} 退出  Exit\"",
        "  echo \"${H}-------------------------------------------------------------${R}\"",
        "  read -r -p \"${H}输入序号并回车 Enter choice >${R} \" sel",
        "  case \"$sel\" in",
        "    1) fastboot -s \"$SERIAL\" getvar all; read -n1 -s -r -p \"按任意键返回菜单 Press any key... \" ;;",
        "    2) fastboot -s \"$SERIAL\" reboot; read -n1 -s -r -p \"按任意键返回菜单 Press any key... \" ;;",
        "    3) fastboot -s \"$SERIAL\" reboot bootloader; read -n1 -s -r -p \"按任意键返回菜单 Press any key... \" ;;",
        "    4) fastboot -s \"$SERIAL\" reboot recovery; read -n1 -s -r -p \"按任意键返回菜单 Press any key... \" ;;",
        "    5) fastboot -s \"$SERIAL\" oem poweroff; read -n1 -s -r -p \"按任意键返回菜单 Press any key... \" ;;",
        "    6) fastboot -s \"$SERIAL\" set_active a; read -n1 -s -r -p \"按任意键返回菜单 Press any key... \" ;;",
        "    7) fastboot -s \"$SERIAL\" set_active b; read -n1 -s -r -p \"按任意键返回菜单 Press any key... \" ;;",
        "    9) echo \"adb / fastboot 已在 PATH。输入 exit 返回菜单 (type exit to return).\"; \"${SHELL:-/bin/bash}\" ;;",
        "    0) exit 0 ;;",
        "  esac",
        "done",
    });

    // macOS adb menu — bash equivalent of BuildAdbMenu (info / shell / logcat / reboots / power / raw).
    private static string BuildAdbMenuSh(string serial, string toolsDir) => JoinLf(new[]
    {
        "#!/bin/bash",
        "H=$'\\033[96m'; N=$'\\033[92m'; V=$'\\033[93m'; R=$'\\033[0m'",
        $"export PATH=\"{ShEscape(toolsDir)}:$PATH\"",
        $"SERIAL=\"{ShEscape(serial)}\"",
        "while true; do",
        "  clear",
        "  echo \"${H}================== AndroidTreeView ADB CLI ==================${R}\"",
        "  echo \"  设备 Device: ${V}${SERIAL}${R}\"",
        "  echo \"${H}------------------------------------------------------------${R}\"",
        "  echo \"  ${N}[1]${R} 设备信息  Device info (getprop)\"",
        "  echo \"  ${N}[2]${R} 进入 Shell  adb shell\"",
        "  echo \"  ${N}[3]${R} 查看日志  Logcat  (Ctrl+C 停止)\"",
        "  echo \"  ${N}[4]${R} 重启  Reboot\"",
        "  echo \"  ${N}[5]${R} 重启到 Bootloader  Reboot to bootloader\"",
        "  echo \"  ${N}[6]${R} 重启到 Recovery  Reboot to recovery\"",
        "  echo \"  ${N}[7]${R} 关机  Power off\"",
        "  echo \"  ${N}[9]${R} 原生命令行  Raw CLI (adb + fastboot on PATH)\"",
        "  echo \"  ${N}[0]${R} 退出  Exit\"",
        "  echo \"${H}------------------------------------------------------------${R}\"",
        "  read -r -p \"${H}输入序号并回车 Enter choice >${R} \" sel",
        "  case \"$sel\" in",
        "    1) adb -s \"$SERIAL\" shell getprop; read -n1 -s -r -p \"按任意键返回菜单 Press any key... \" ;;",
        "    2) adb -s \"$SERIAL\" shell ;;",
        "    3) adb -s \"$SERIAL\" logcat ;;",
        "    4) adb -s \"$SERIAL\" reboot; read -n1 -s -r -p \"按任意键返回菜单 Press any key... \" ;;",
        "    5) adb -s \"$SERIAL\" reboot bootloader; read -n1 -s -r -p \"按任意键返回菜单 Press any key... \" ;;",
        "    6) adb -s \"$SERIAL\" reboot recovery; read -n1 -s -r -p \"按任意键返回菜单 Press any key... \" ;;",
        "    7) adb -s \"$SERIAL\" shell reboot -p; read -n1 -s -r -p \"按任意键返回菜单 Press any key... \" ;;",
        "    9) echo \"adb / fastboot 已在 PATH。输入 exit 返回菜单 (type exit to return).\"; \"${SHELL:-/bin/bash}\" ;;",
        "    0) exit 0 ;;",
        "  esac",
        "done",
    });

    // Escape for embedding a literal into a double-quoted bash string safely.
    private static string ShEscape(string value) =>
        value.Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("$", "\\$")
            .Replace("`", "\\`");

    private static string Join(IEnumerable<string> lines) => string.Join("\r\n", lines) + "\r\n";

    private static string JoinLf(IEnumerable<string> lines) => string.Join("\n", lines) + "\n";

    private string? ResolveToolsDir()
    {
        var adb = _environment.Location?.ExecutablePath;
        if (!string.IsNullOrEmpty(adb))
        {
            var dir = Path.GetDirectoryName(adb);
            if (!string.IsNullOrEmpty(dir))
            {
                return dir;
            }
        }

        var bundled = Path.Combine(AppContext.BaseDirectory, "scrcpy");
        return Directory.Exists(bundled) ? bundled : null;
    }
}
