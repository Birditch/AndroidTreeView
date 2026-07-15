using AndroidTreeView.Models.Rooting;

namespace AndroidTreeView.Core.Interfaces;

/// <summary>
/// UI-independent orchestrator for the root workflow. Confirmation methods never infer user consent.
/// </summary>
public interface IRootWizardService
{
    RootWizardSnapshot Snapshot { get; }

    event EventHandler<RootWizardSnapshot>? Changed;

    void SelectDevice(RootDeviceIdentity device);

    void SelectPackage(string packagePath);

    Task ExtractAndPatchAsync(CancellationToken ct = default);

    Task ConfirmBootloaderAsync(CancellationToken ct = default);

    Task DetectFastbootAsync(CancellationToken ct = default);

    Task ConfirmFlashAsync(bool riskAcknowledged, CancellationToken ct = default);

    Task RetryAsync(CancellationToken ct = default);

    Task CancelAsync();
}
