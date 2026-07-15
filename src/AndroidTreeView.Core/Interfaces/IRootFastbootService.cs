using AndroidTreeView.Models.Rooting;

namespace AndroidTreeView.Core.Interfaces;

/// <summary>
/// Strict fastboot operations used by the root workflow. Unlike <see cref="IFastbootService"/>,
/// failures are retained as stable workflow errors and writes require a verified device serial.
/// </summary>
public interface IRootFastbootService
{
    string? ExecutablePath { get; }

    Task<FastbootBaseline> CaptureBaselineAsync(CancellationToken ct = default);

    Task RebootToBootloaderAsync(string adbSerial, CancellationToken ct = default);

    Task<FastbootIdentityMatch> WaitForMatchingDeviceAsync(
        RootDeviceIdentity device,
        FastbootBaseline baseline,
        TimeSpan timeout,
        CancellationToken ct = default);

    /// <summary>Freshly revalidates the locked identity immediately before a write.</summary>
    Task<FastbootIdentityMatch> VerifyCurrentIdentityAsync(
        RootDeviceIdentity device,
        string expectedSerial,
        CancellationToken ct = default);

    Task<bool> IsBootloaderUnlockedAsync(string fastbootSerial, CancellationToken ct = default);

    Task<FastbootBootLayout> GetBootLayoutAsync(
        string fastbootSerial,
        BootPartitionTarget target,
        CancellationToken ct = default);

    Task<FlashResult> FlashAsync(
        string fastbootSerial,
        BootPartitionTarget target,
        string imagePath,
        FastbootBootLayout layout,
        IReadOnlyCollection<string>? alreadySucceededPartitions = null,
        CancellationToken ct = default);

    Task RebootAsync(string fastbootSerial, CancellationToken ct = default);
}
