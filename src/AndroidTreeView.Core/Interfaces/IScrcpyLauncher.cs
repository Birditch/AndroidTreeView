using System.Diagnostics;

namespace AndroidTreeView.Core.Interfaces;

/// <summary>Starts scrcpy with the shared ADB environment and primes the device for the first video frame.</summary>
public interface IScrcpyLauncher
{
    ScrcpyLaunchResult Launch(string serial, string title);

    void PrimeFirstFrame(string serial);
}

public sealed class ScrcpyLaunchResult
{
    public required string ExecutablePath { get; init; }

    public Process? Process { get; init; }

    public string? ErrorMessage { get; init; }

    public bool Started => Process is not null;
}
