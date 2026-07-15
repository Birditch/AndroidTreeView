namespace AndroidTreeView.Models.Rooting;

/// <summary>Explicit states of the semi-automatic root workflow.</summary>
public enum RootWizardState
{
    Idle = 0,
    PackageSelected = 1,
    Extracting = 2,
    BootExtracted = 3,
    Patching = 4,
    BootPatched = 5,
    AwaitingBootloaderConfirm = 6,
    RebootingToBootloader = 7,
    InFastboot = 8,
    BlockedUnsupportedTarget = 9,
    BlockedFastbootIdentity = 10,
    BlockedLocked = 11,
    BlockedBootLayout = 12,
    AwaitingFlashConfirm = 13,
    Flashing = 14,
    Rebooting = 15,
    Completed = 16,
    Failed = 17,
    FailedPartialFlash = 18,
    FlashOutcomeUnknown = 19,
    Cancelled = 20
}
