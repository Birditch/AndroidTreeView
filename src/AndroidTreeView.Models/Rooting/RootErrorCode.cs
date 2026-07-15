namespace AndroidTreeView.Models.Rooting;

/// <summary>
/// Stable, non-localized error identifiers used across services, tests, logs and UI resource lookup.
/// Numeric values are grouped by workflow stage and must not be repurposed.
/// </summary>
public enum RootErrorCode
{
    None = 0,

    InvalidState = 100,
    DeviceNotSelected = 101,
    DeviceUnavailable = 102,
    DeviceUnauthorized = 103,
    OperationCancelled = 104,

    PackageNotFound = 200,
    PackageUnsupported = 201,
    PackageCorrupt = 202,
    PackageMetadataMismatch = 203,
    PackageMetadataUnverified = 204,
    PackageExtractionFailed = 205,
    PackageSizeLimitExceeded = 206,
    PackagePathUnsafe = 207,
    PayloadToolUnavailable = 208,
    PayloadExtractionFailed = 209,
    TargetImageMissing = 210,
    TargetPartitionUnknown = 211,
    TargetEvidenceConflict = 212,
    RecoveryOnlyUnsupported = 213,

    BackupSourceMissing = 300,
    BackupFailed = 301,
    BackupVerificationFailed = 302,

    MagiskToolUnavailable = 400,
    MagiskInstallFailed = 401,
    DeviceAbiUnsupported = 402,
    ImagePushFailed = 403,
    MagiskPatchFailed = 404,
    PatchedImagePullFailed = 405,
    PatchedImageInvalid = 406,
    MagiskFlagProbeFailed = 407,
    PatchedImageFlagMismatch = 408,

    FastbootUnavailable = 500,
    FastbootBaselineFailed = 501,
    FastbootDeviceNotFound = 502,
    FastbootIdentityUnverified = 503,
    FastbootIdentityConflict = 504,
    BootloaderLocked = 505,
    BootLayoutUnknown = 506,
    BootLayoutConflict = 507,
    RebootToBootloaderFailed = 508,

    RiskNotAcknowledged = 600,
    FlashFailed = 601,
    FlashPartiallyWritten = 602,
    FlashOutcomeUnknown = 603,
    RebootFailed = 604,

    UnexpectedFailure = 900
}
