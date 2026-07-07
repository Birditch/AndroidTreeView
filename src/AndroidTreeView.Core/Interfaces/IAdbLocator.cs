using AndroidTreeView.Core.Services;

namespace AndroidTreeView.Core.Interfaces;

/// <summary>
/// Discovers the adb executable on the current machine.
/// </summary>
public interface IAdbLocator
{
    /// <summary>
    /// Attempts to locate a usable adb executable, preferring <paramref name="configuredPath"/>.
    /// Returns <see langword="null"/> when adb cannot be found.
    /// </summary>
    Task<AdbLocation?> LocateAsync(string? configuredPath, CancellationToken ct = default);
}
