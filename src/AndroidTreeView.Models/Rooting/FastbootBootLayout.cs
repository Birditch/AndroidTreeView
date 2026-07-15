namespace AndroidTreeView.Models.Rooting;

public enum FastbootBootLayoutKind
{
    Unknown = 0,
    Single = 1,
    Ab = 2
}

/// <summary>Evidence-backed layout for the selected boot partition.</summary>
public sealed record FastbootBootLayout
{
    public FastbootBootLayoutKind Kind { get; init; }

    /// <summary>Active slot reported by fastboot, normally <c>a</c> or <c>b</c>.</summary>
    public string? CurrentSlot { get; init; }

    public int? SlotCount { get; init; }

    /// <summary>Value reported by <c>has-slot:&lt;target&gt;</c>, when available.</summary>
    public bool? TargetHasSlot { get; init; }

    public bool IsKnown => Kind != FastbootBootLayoutKind.Unknown;
}
