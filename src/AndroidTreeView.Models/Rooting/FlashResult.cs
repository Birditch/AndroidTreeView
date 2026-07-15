namespace AndroidTreeView.Models.Rooting;

/// <summary>Safety-relevant outcome of one single-slot or A/B flash operation.</summary>
public enum FlashOutcome
{
    /// <summary>Every requested partition was confirmed written.</summary>
    Succeeded = 0,

    /// <summary>The command failed before any partition was confirmed written.</summary>
    FailedBeforeWrite = 1,

    /// <summary>At least one requested partition was written and a later write failed.</summary>
    PartiallyWritten = 2,

    /// <summary>A command was interrupted or disconnected, so its write result cannot be established.</summary>
    Unknown = 3
}

/// <summary>Detailed flash result retained by the wizard for recovery guidance and retry decisions.</summary>
public sealed record FlashResult
{
    public required IReadOnlyList<string> RequestedPartitions { get; init; }

    public IReadOnlyList<string> SucceededPartitions { get; init; } = Array.Empty<string>();

    public string? FailedPartition { get; init; }

    public FlashOutcome Outcome { get; init; }

    public RootErrorCode ErrorCode { get; init; }

    /// <summary>Sanitized process detail for logs; callers must not use it as localized display text.</summary>
    public string? DiagnosticSummary { get; init; }

    public bool HasWrittenAnyPartition => SucceededPartitions.Count > 0;

    public bool OutcomeUnknown => Outcome == FlashOutcome.Unknown;
}
