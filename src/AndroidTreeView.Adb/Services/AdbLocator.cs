using System.Text.RegularExpressions;
using AndroidTreeView.Adb.Commands;
using AndroidTreeView.Adb.Internal;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Services;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.Adb.Services;

/// <summary>
/// Locates a usable adb executable. Resolution order: configured path, then PATH, then common
/// SDK install locations. A candidate is validated by running <c>adb version</c>.
/// </summary>
public sealed partial class AdbLocator : IAdbLocator
{
    private static readonly TimeSpan ValidationTimeout = TimeSpan.FromSeconds(5);

    private readonly ILogger<AdbLocator> _logger;

    public AdbLocator(ILogger<AdbLocator> logger) => _logger = logger;

    [GeneratedRegex(@"version\s+([\d]+(?:\.[\d]+)+)", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRegex();

    private static string ExecutableName => OperatingSystem.IsWindows() ? "adb.exe" : "adb";

    public async Task<AdbLocation?> LocateAsync(string? configuredPath, CancellationToken ct = default)
    {
        var configured = await TryConfiguredAsync(configuredPath, ct).ConfigureAwait(false);
        if (configured is not null)
        {
            return configured;
        }

        var bundled = await TryBundledAsync(ct).ConfigureAwait(false);
        if (bundled is not null)
        {
            return bundled;
        }

        var onPath = await TryPathAsync(ct).ConfigureAwait(false);
        if (onPath is not null)
        {
            return onPath;
        }

        return await TrySdkLocationsAsync(ct).ConfigureAwait(false);
    }

    private async Task<AdbLocation?> TryBundledAsync(CancellationToken ct)
    {
        // adb shipped alongside the app. Tools are consolidated into a single "scrcpy" folder
        // (scrcpy ships its own adb, so we no longer keep a separate platform-tools copy); an older
        // "platform-tools" layout is still probed as a fallback for compatibility.
        foreach (var folder in new[] { "scrcpy", "platform-tools" })
        {
            var candidate = Path.Combine(AppContext.BaseDirectory, folder, ExecutableName);
            var location = await ValidateAsync(candidate, AdbLocationSource.Bundled, ct).ConfigureAwait(false);
            if (location is not null)
            {
                return location;
            }
        }

        return null;
    }

    private async Task<AdbLocation?> TryConfiguredAsync(string? configuredPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        var candidate = Directory.Exists(configuredPath)
            ? Path.Combine(configuredPath, ExecutableName)
            : configuredPath;

        return await ValidateAsync(candidate, AdbLocationSource.Configured, ct).ConfigureAwait(false);
    }

    private async Task<AdbLocation?> TryPathAsync(CancellationToken ct)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable))
        {
            return null;
        }

        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            ct.ThrowIfCancellationRequested();

            string candidate;
            try
            {
                candidate = Path.Combine(directory.Trim(), ExecutableName);
            }
            catch (ArgumentException)
            {
                continue; // Malformed PATH entry.
            }

            var location = await ValidateAsync(candidate, AdbLocationSource.EnvironmentPath, ct).ConfigureAwait(false);
            if (location is not null)
            {
                return location;
            }
        }

        return null;
    }

    private async Task<AdbLocation?> TrySdkLocationsAsync(CancellationToken ct)
    {
        foreach (var candidate in EnumerateSdkCandidates())
        {
            ct.ThrowIfCancellationRequested();

            var location = await ValidateAsync(candidate, AdbLocationSource.CommonSdkLocation, ct).ConfigureAwait(false);
            if (location is not null)
            {
                return location;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateSdkCandidates()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var roots = new List<string?>
        {
            Combine(localAppData, "Android", "Sdk", "platform-tools"),
            Combine(home, "Library", "Android", "sdk", "platform-tools"),
            Combine(home, "Android", "Sdk", "platform-tools"),
            "/usr/local/bin",
            "/opt/android-sdk/platform-tools",
            Combine(Environment.GetEnvironmentVariable("ANDROID_HOME"), "platform-tools"),
            Combine(Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"), "platform-tools")
        };

        foreach (var root in roots)
        {
            if (!string.IsNullOrEmpty(root))
            {
                yield return Path.Combine(root, ExecutableName);
            }
        }
    }

    private static string? Combine(string? root, params string[] segments)
    {
        if (string.IsNullOrEmpty(root))
        {
            return null;
        }

        var parts = new string[segments.Length + 1];
        parts[0] = root;
        Array.Copy(segments, 0, parts, 1, segments.Length);
        return Path.Combine(parts);
    }

    private async Task<AdbLocation?> ValidateAsync(string candidate, AdbLocationSource source, CancellationToken ct)
    {
        // Every candidate (including PATH entries, which we resolve to an absolute path) must
        // point at a real file before we spend time trying to run it.
        if (!File.Exists(candidate))
        {
            return null;
        }

        try
        {
            var run = await ProcessRunner.RunAsync(candidate, AdbArgs.Version, ValidationTimeout, ct)
                .ConfigureAwait(false);

            if (run.TimedOut || run.ExitCode != 0)
            {
                return null;
            }

            if (!run.StandardOutput.Contains("Android Debug Bridge", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            _logger.LogInformation("Resolved adb at {Path} (source {Source}).", candidate, source);
            return new AdbLocation
            {
                ExecutablePath = candidate,
                Version = ParseVersion(run.StandardOutput),
                Source = source
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "adb candidate {Path} failed validation.", candidate);
            return null;
        }
    }

    private static string? ParseVersion(string output)
    {
        var match = VersionRegex().Match(output);
        return match.Success ? match.Groups[1].Value : null;
    }
}
