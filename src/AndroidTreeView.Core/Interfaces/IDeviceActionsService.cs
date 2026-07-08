namespace AndroidTreeView.Core.Interfaces;

/// <summary>Reboot destinations for a connected device.</summary>
public enum RebootTarget
{
    System,
    Recovery,
    Bootloader
}

/// <summary>
/// Safe, universal, non-destructive device actions invoked from the right-click menu. None require root
/// (root only enhances), none flash or wipe the device. Each method runs the corresponding adb command
/// and throws only on adb-not-found; ordinary device errors surface via the returned task faulting, which
/// callers map to a user message.
/// </summary>
public interface IDeviceActionsService
{
    /// <summary>Reboots the device to system / recovery / bootloader (fastboot).</summary>
    Task RebootAsync(string serial, RebootTarget target, CancellationToken ct = default);

    /// <summary>Powers the device off (<c>reboot -p</c>).</summary>
    Task PowerOffAsync(string serial, CancellationToken ct = default);

    /// <summary>Clears Google Factory Reset Protection by marking setup complete (no root, non-destructive).</summary>
    Task RemoveFrpAsync(string serial, CancellationToken ct = default);

    /// <summary>True when FRP is already cleared (setup complete + device provisioned), so the action is a no-op.</summary>
    Task<bool> IsFrpRemovedAsync(string serial, CancellationToken ct = default);
}
