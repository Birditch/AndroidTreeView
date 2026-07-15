namespace AndroidTreeView.Models.Rooting;

/// <summary>
/// Immutable observable state for a root session. It contains machine data only; presentation and localization
/// belong to the App layer.
/// </summary>
public sealed record RootWizardSnapshot
{
    public RootWizardState State { get; init; } = RootWizardState.Idle;

    public Guid SessionId { get; init; } = Guid.NewGuid();

    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public RootDeviceIdentity? DeviceIdentity { get; init; }

    public string? PackagePath { get; init; }

    public FirmwarePackageMetadata? PackageMetadata { get; init; }

    public BootImageInfo? BootImage { get; init; }

    public string? WorkDirectory { get; init; }

    public string? PatchedImagePath { get; init; }

    public string? BackupPath { get; init; }

    public FastbootBaseline? FastbootBaseline { get; init; }

    public FastbootIdentityMatch? FastbootIdentityMatch { get; init; }

    public FastbootBootLayout? BootLayout { get; init; }

    public FlashResult? FlashResult { get; init; }

    public RootErrorCode ErrorCode { get; init; }

    /// <summary>Sanitized technical detail for diagnostics, not user-facing localized text.</summary>
    public string? DiagnosticSummary { get; init; }

    public BootPartitionTarget TargetPartition =>
        BootImage?.TargetPartition ?? BootPartitionTarget.Unknown;

    public string? MatchedFastbootSerial =>
        FastbootIdentityMatch?.IsVerified == true ? FastbootIdentityMatch.Device!.Serial : null;
}
