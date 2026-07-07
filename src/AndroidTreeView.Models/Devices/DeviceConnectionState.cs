namespace AndroidTreeView.Models.Devices;

/// <summary>
/// Connection state of an Android device as reported by <c>adb devices -l</c>.
/// </summary>
public enum DeviceConnectionState
{
    Unknown,
    Online,
    Offline,
    Unauthorized,
    Authorizing,
    Bootloader,
    Recovery,
    Sideload,
    NoPermission,
    Connecting,
    Disconnected
}
