using AndroidTreeView.Adb.Parsers;
using AndroidTreeView.Models.Rooting;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Parsers;

public sealed class BootPartitionTargetDetectorTests
{
    [Fact]
    public void Detect_Android13GkiWithPartition_SelectsInitBoot()
    {
        var result = BootPartitionTargetDetector.Detect(true, true, true, 33, new Version(5, 10));
        Assert.Equal(BootPartitionTarget.InitBoot, result.Target);
    }

    [Fact]
    public void Detect_Android12Kernel510_StillSelectsBoot()
    {
        var result = BootPartitionTargetDetector.Detect(true, true, true, 32, new Version(5, 10));
        Assert.Equal(BootPartitionTarget.Boot, result.Target);
    }

    [Fact]
    public void Detect_RequiredInitBootMissing_DoesNotFallback()
    {
        var result = BootPartitionTargetDetector.Detect(true, false, true, 34, new Version(6, 1));
        Assert.False(result.IsSupported);
        Assert.Equal(RootErrorCode.TargetImageMissing, result.ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Detect_InitBootWithMissingOrInvalidSdk_DoesNotFallbackToBoot(int? androidSdk)
    {
        var result = BootPartitionTargetDetector.Detect(true, true, true, androidSdk, new Version(5, 10));

        Assert.False(result.IsSupported);
        Assert.Equal(RootErrorCode.TargetEvidenceConflict, result.ErrorCode);
    }

    [Fact]
    public void Detect_InitBootWithMissingKernel_DoesNotFallbackToBoot()
    {
        var result = BootPartitionTargetDetector.Detect(true, true, true, 32, kernelVersion: null);

        Assert.False(result.IsSupported);
        Assert.Equal(RootErrorCode.TargetEvidenceConflict, result.ErrorCode);
    }

    [Fact]
    public void ValidateRamdisk_BootWithoutRamdisk_BlocksRecoveryOnly()
    {
        var result = BootPartitionTargetDetector.ValidateRamdisk(
            BootPartitionTarget.Boot,
            BootRamdiskEvidence.Absent);

        Assert.Equal(RootErrorCode.RecoveryOnlyUnsupported, result.ErrorCode);
    }

    [Theory]
    [InlineData(BootPartitionTarget.Boot)]
    [InlineData(BootPartitionTarget.InitBoot)]
    public void ValidateRamdisk_UnknownEvidence_BlocksWithoutGuessing(BootPartitionTarget target)
    {
        var result = BootPartitionTargetDetector.ValidateRamdisk(target, BootRamdiskEvidence.Unknown);

        Assert.Equal(RootErrorCode.TargetEvidenceConflict, result.ErrorCode);
    }
}
