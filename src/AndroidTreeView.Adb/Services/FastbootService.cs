using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Services;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.Adb.Services;

/// <summary>
/// <see cref="IFastbootService"/> backed by the bundled <c>fastboot</c> binary (found next to adb).
/// Everything is best-effort: a missing binary or a non-zero exit yields empty results, never an
/// exception (only cancellation propagates), so the device-list poll never breaks on fastboot errors.
/// </summary>
public sealed class FastbootService : IFastbootService
{
    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(8);

    private readonly IAdbEnvironment _environment;
    private readonly IExternalCommandRunner _runner;
    private readonly ILogger<FastbootService> _logger;

    public FastbootService(
        IAdbEnvironment environment,
        IExternalCommandRunner runner,
        ILogger<FastbootService> logger)
    {
        _environment = environment;
        _runner = runner;
        _logger = logger;
    }

    public string? ExecutablePath
    {
        get
        {
            var adb = _environment.Location?.ExecutablePath;
            if (string.IsNullOrEmpty(adb))
            {
                return null;
            }

            var directory = Path.GetDirectoryName(adb);
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            var name = OperatingSystem.IsWindows() ? "fastboot.exe" : "fastboot";
            var path = Path.Combine(directory, name);
            return File.Exists(path) ? path : null;
        }
    }

    public async Task<IReadOnlyList<string>> ListSerialsAsync(CancellationToken ct = default)
    {
        var exe = ExecutablePath;
        if (exe is null)
        {
            return Array.Empty<string>();
        }

        try
        {
            var result = await _runner.RunAsync(CreateRequest(exe, ["devices"], ListTimeout), ct)
                .ConfigureAwait(false);
            return !result.IsSuccess
                ? Array.Empty<string>()
                : ParseSerials(result.StandardOutput);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "fastboot devices failed.");
            return Array.Empty<string>();
        }
    }

    public async Task<IReadOnlyDictionary<string, string>> GetVariablesAsync(string serial, CancellationToken ct = default)
    {
        var empty = (IReadOnlyDictionary<string, string>)new Dictionary<string, string>();
        var exe = ExecutablePath;
        if (exe is null)
        {
            return empty;
        }

        try
        {
            var result = await _runner
                .RunAsync(CreateRequest(exe, ["-s", serial, "getvar", "all"], ListTimeout), ct)
                .ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                return empty;
            }

            // fastboot writes getvar output to STDERR, so parse both streams.
            return ParseVariables(result.StandardOutput + "\n" + result.StandardError);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "fastboot getvar all failed for {Serial}.", serial);
            return empty;
        }
    }

    public Task RebootAsync(string serial, FastbootTarget target, CancellationToken ct = default)
    {
        var args = target switch
        {
            FastbootTarget.Bootloader => new[] { "-s", serial, "reboot", "bootloader" },
            FastbootTarget.Recovery => new[] { "-s", serial, "reboot", "recovery" },
            _ => new[] { "-s", serial, "reboot" },
        };
        return RunAsync(args, ct);
    }

    public Task PowerOffAsync(string serial, CancellationToken ct = default)
        => RunAsync(new[] { "-s", serial, "oem", "poweroff" }, ct);

    private async Task RunAsync(string[] args, CancellationToken ct)
    {
        var exe = ExecutablePath;
        if (exe is null)
        {
            return;
        }

        try
        {
            await _runner.RunAsync(CreateRequest(exe, args, ActionTimeout), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "fastboot '{Args}' failed.", string.Join(' ', args));
        }
    }

    private static ExternalCommandRequest CreateRequest(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout)
        => new()
        {
            FileName = executablePath,
            Arguments = arguments,
            Timeout = timeout
        };

    // Lines look like "SERIAL\tfastboot" (tabs or spaces). Keep the serial of any line that names fastboot.
    private static IReadOnlyList<string> ParseSerials(string output)
    {
        var serials = new List<string>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[1].Contains("fastboot", StringComparison.OrdinalIgnoreCase))
            {
                serials.Add(parts[0]);
            }
        }

        return serials;
    }

    // getvar lines look like "(bootloader) product: devon" or "product: devon". Strip the prefix and
    // split on the first ':'. Skip the trailing "all:" / "finished." noise.
    private static IReadOnlyDictionary<string, string> ParseVariables(string output)
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("(bootloader)", StringComparison.OrdinalIgnoreCase))
            {
                line = line["(bootloader)".Length..].Trim();
            }

            var idx = line.IndexOf(':');
            if (idx <= 0)
            {
                continue;
            }

            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            if (key.Length == 0
                || key.Equals("all", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("finished", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            vars[key] = value;
        }

        return vars;
    }
}
