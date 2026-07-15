namespace AndroidTreeView.Models.Rooting;

/// <summary>Result of comparing package metadata with the locked device identity.</summary>
public enum FirmwarePackageMatchStatus
{
    /// <summary>The package did not provide enough metadata for a comparison.</summary>
    Unverified = 0,

    /// <summary>At least one declared package identity matches and none conflict.</summary>
    Matched = 1,

    /// <summary>Package metadata explicitly identifies a different device.</summary>
    Mismatched = 2
}

/// <summary>Parsed, UI-independent identity metadata for a selected firmware package.</summary>
public sealed record FirmwarePackageMetadata
{
    public required string PackagePath { get; init; }

    public required string OriginalPackageName { get; init; }

    public FirmwarePackageType PackageType { get; init; }

    public IReadOnlyList<string> DeclaredProducts { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> DeclaredDevices { get; init; } = Array.Empty<string>();

    public FirmwarePackageMatchStatus MatchStatus { get; init; }

    /// <summary>Machine-readable values that established a match, never localized display text.</summary>
    public IReadOnlyList<string> MatchingValues { get; init; } = Array.Empty<string>();
}
