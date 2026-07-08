namespace AndroidTreeView.Core.Interfaces;

/// <summary>
/// Captures the device screen and injects basic input for the "screen mirror" (投屏) window. Frames are
/// polled screenshots (<c>adb exec-out screencap -p</c>) — a lightweight, universal, no-root approach
/// (not a real-time scrcpy stream). All methods are resilient: capture returns <c>null</c> on failure.
/// </summary>
public interface IScreenCaptureService
{
    /// <summary>Captures one screen frame as PNG bytes; returns <c>null</c> when the frame can't be read.</summary>
    Task<byte[]?> CaptureFrameAsync(string serial, CancellationToken ct = default);

    /// <summary>Injects a tap at device pixel coordinates (<c>input tap x y</c>).</summary>
    Task TapAsync(string serial, int x, int y, CancellationToken ct = default);

    /// <summary>Installs / reinstalls an APK (<c>install -r</c>); returns true on success.</summary>
    Task<bool> InstallApkAsync(string serial, string apkPath, CancellationToken ct = default);

    /// <summary>Pushes a local file to the device; returns true on success.</summary>
    Task<bool> PushFileAsync(string serial, string filePath, string? remoteDirectory = null, CancellationToken ct = default);

    /// <summary>Prepares the remote directory used for drag-drop file transfer; best-effort.</summary>
    Task<bool> PrepareFileTransferAsync(string serial, string? remoteDirectory = null, CancellationToken ct = default);

    /// <summary>Injects a swipe / drag gesture in device pixels (<c>input swipe</c>).</summary>
    Task SwipeAsync(string serial, int x1, int y1, int x2, int y2, int durationMs, CancellationToken ct = default);

    /// <summary>Injects a key event by Android key code (<c>input keyevent</c>; 4=Back, 3=Home, 187=Recents).</summary>
    Task KeyEventAsync(string serial, int keyCode, CancellationToken ct = default);
}
