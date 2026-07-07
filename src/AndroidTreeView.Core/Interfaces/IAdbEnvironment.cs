using AndroidTreeView.Core.Services;

namespace AndroidTreeView.Core.Interfaces;

/// <summary>
/// Shared, mutable adb availability state updated by the locator and consumed by executors.
/// </summary>
public interface IAdbEnvironment
{
    /// <summary>True when a valid adb location is currently set.</summary>
    bool IsAvailable { get; }

    /// <summary>The current adb location, or <see langword="null"/> when unavailable.</summary>
    AdbLocation? Location { get; }

    /// <summary>
    /// Path to the adb executable. Throws
    /// <see cref="Exceptions.AdbNotFoundException"/> when adb is unavailable.
    /// </summary>
    string ExecutablePath { get; }

    /// <summary>Sets (or clears) the current adb location and raises <see cref="Changed"/>.</summary>
    void Set(AdbLocation? location);

    /// <summary>Raised whenever the adb location changes.</summary>
    event EventHandler? Changed;
}
