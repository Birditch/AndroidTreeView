namespace AndroidTreeView.Models.Rooting;

/// <summary>An extracted original boot image and the evidence-backed partition it belongs to.</summary>
public sealed record BootImageInfo
{
    public required string Path { get; init; }

    public required string WorkDirectory { get; init; }

    public required string OriginalPackageName { get; init; }

    public BootPartitionTarget TargetPartition { get; init; }

    public BootImageSource Source { get; init; }

    public FirmwarePackageMetadata? PackageMetadata { get; init; }
}
