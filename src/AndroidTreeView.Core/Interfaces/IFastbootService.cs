namespace AndroidTreeView.Core.Interfaces;

/// <summary>Reboot destinations for a device in fastboot / bootloader mode.</summary>
public enum FastbootTarget
{
    System,
    Bootloader,
    Recovery
}

/// <summary>
/// Detects and controls devices in fastboot / bootloader mode via the bundled <c>fastboot</c> binary.
/// A fastboot device runs no Android OS, so it can only be listed, rebooted, or powered off — never
/// inspected or mirrored. All methods are resilient: normal failures are swallowed, not thrown.
/// </summary>
public interface IFastbootService
{
    /// <summary>Absolute path to the bundled fastboot executable, or <see langword="null"/> when unavailable.</summary>
    string? ExecutablePath { get; }

    /// <summary>Serials of devices currently in fastboot mode (empty when none / fastboot missing). Never throws.</summary>
    Task<IReadOnlyList<string>> ListSerialsAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads the variables a fastboot device exposes via <c>fastboot getvar all</c> (product, bootloader
    /// version, baseband, unlocked/secure state, current slot, …). Empty dictionary on any failure.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetVariablesAsync(string serial, CancellationToken ct = default);

    /// <summary>Reboots a fastboot device to the given target.</summary>
    Task RebootAsync(string serial, FastbootTarget target, CancellationToken ct = default);

    /// <summary>Powers a fastboot device off (best-effort: <c>fastboot oem poweroff</c>).</summary>
    Task PowerOffAsync(string serial, CancellationToken ct = default);
}
