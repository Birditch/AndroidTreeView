namespace AndroidTreeView.Models.Rooting;

/// <summary>Identity fields reported for one device by fastboot.</summary>
public sealed record FastbootDeviceIdentity
{
    public required string Serial { get; init; }

    public string? UsbPath { get; init; }

    public string? Product { get; init; }

    public IReadOnlyDictionary<string, string> Descriptors { get; init; }
        = new Dictionary<string, string>();
}

/// <summary>Fastboot devices already present before the selected ADB device is rebooted.</summary>
public sealed record FastbootBaseline
{
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<FastbootDeviceIdentity> Devices { get; init; }
        = Array.Empty<FastbootDeviceIdentity>();
}

public enum FastbootIdentityMatchStatus
{
    Unverified = 0,
    Verified = 1,
    TargetNotFound = 2,
    Ambiguous = 3,
    ConflictingEvidence = 4
}

/// <summary>Independent evidence used to correlate ADB and fastboot identities.</summary>
[Flags]
public enum FastbootIdentityEvidence
{
    None = 0,
    Serial = 1,
    UsbPath = 2,
    Product = 4
}

/// <summary>
/// Correlation result after reboot. A device may be used for writes only when <see cref="Status"/> is
/// <see cref="FastbootIdentityMatchStatus.Verified"/>.
/// </summary>
public sealed record FastbootIdentityMatch
{
    public FastbootIdentityMatchStatus Status { get; init; }

    public FastbootDeviceIdentity? Device { get; init; }

    public FastbootIdentityEvidence Evidence { get; init; }

    public bool IsVerified => Status == FastbootIdentityMatchStatus.Verified && Device is not null;
}
