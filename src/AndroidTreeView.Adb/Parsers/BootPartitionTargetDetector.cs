using System.Globalization;
using AndroidTreeView.Models.Rooting;

namespace AndroidTreeView.Adb.Parsers;

/// <summary>Combines package contents and device evidence without guessing a flash partition.</summary>
public static class BootPartitionTargetDetector
{
    private static readonly Version MinimumAndroid13GkiKernel = new(5, 10);

    public static BootPartitionDetection Detect(
        bool packageHasBoot,
        bool packageHasInitBoot,
        bool deviceHasInitBoot,
        int? androidSdk,
        Version? kernelVersion)
    {
        if (!packageHasBoot && !packageHasInitBoot)
        {
            return BootPartitionDetection.Blocked(RootErrorCode.TargetImageMissing);
        }

        var initBootEvidenceIncomplete = deviceHasInitBoot
            && (androidSdk is null or <= 0 || kernelVersion is null);
        if (initBootEvidenceIncomplete)
        {
            return BootPartitionDetection.Blocked(RootErrorCode.TargetEvidenceConflict);
        }

        var initBootRequired = deviceHasInitBoot
            && androidSdk >= 33
            && kernelVersion is not null
            && kernelVersion >= MinimumAndroid13GkiKernel;

        if (initBootRequired)
        {
            return packageHasInitBoot
                ? BootPartitionDetection.Selected(BootPartitionTarget.InitBoot)
                : BootPartitionDetection.Blocked(RootErrorCode.TargetImageMissing);
        }

        if (packageHasBoot)
        {
            return BootPartitionDetection.Selected(BootPartitionTarget.Boot);
        }

        return BootPartitionDetection.Blocked(RootErrorCode.TargetEvidenceConflict);
    }

    public static int? ParseAndroidSdk(string? output)
        => int.TryParse(output?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var sdk)
            ? sdk
            : null;

    public static Version? ParseKernelVersion(string? output)
    {
        var token = (output ?? string.Empty)
            .Trim()
            .Split(new[] { '-', ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (token is null)
        {
            return null;
        }

        var components = token.Split('.');
        if (components.Length < 2
            || !int.TryParse(components[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(components[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
        {
            return null;
        }

        return new Version(major, minor);
    }

    public static bool ParsePartitionExists(string? output)
        => (output ?? string.Empty).Trim().Equals("yes", StringComparison.OrdinalIgnoreCase);

    public static BootPartitionDetection ValidateRamdisk(
        BootPartitionTarget target,
        BootRamdiskEvidence evidence)
        => evidence switch
        {
            BootRamdiskEvidence.Present => BootPartitionDetection.Selected(target),
            BootRamdiskEvidence.Absent when target == BootPartitionTarget.Boot
                => BootPartitionDetection.Blocked(RootErrorCode.RecoveryOnlyUnsupported),
            _ => BootPartitionDetection.Blocked(RootErrorCode.TargetEvidenceConflict)
        };
}

public sealed record BootPartitionDetection
{
    public BootPartitionTarget Target { get; init; }

    public RootErrorCode ErrorCode { get; init; }

    public bool IsSupported => Target != BootPartitionTarget.Unknown && ErrorCode == RootErrorCode.None;

    public static BootPartitionDetection Selected(BootPartitionTarget target)
        => new() { Target = target };

    public static BootPartitionDetection Blocked(RootErrorCode errorCode)
        => new() { ErrorCode = errorCode };
}
