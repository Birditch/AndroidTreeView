using AndroidTreeView.Adb.Parsers;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Parsers;

public sealed class MagiskFlagsParserTests
{
    // Verbatim app_init output captured from a real cannon (M2007J22C, Android 12, MIUI 14).
    private const string RealProbeOutput = """
        SLOT=
        SYSTEM_AS_ROOT=true
        RAMDISKEXIST=true
        ISAB=false
        CRYPTOTYPE=file
        PATCHVBMETAFLAG=false
        LEGACYSAR=false
        RECOVERYMODE=false
        KEEPVERITY=true
        KEEPFORCEENCRYPT=true
        VENDORBOOT=false
        """;

    // Verbatim .backup/.magisk from the official Magisk v30.7 output for the same device. Note it carries
    // neither PATCHVBMETAFLAG nor LEGACYSAR.
    private const string RealPatchedConfig = """
        KEEPVERITY=true
        KEEPFORCEENCRYPT=true
        RECOVERYMODE=false
        VENDORBOOT=false
        PREINITDEVICE=cache
        SHA1=161a5ed94f06974120cc8f0985cbd3fc430f350e
        """;

    [Fact]
    public void Parse_reads_every_flag_from_real_device_probe_output()
    {
        var flags = MagiskFlagsParser.Parse(RealProbeOutput);

        Assert.NotNull(flags);
        Assert.True(flags.KeepVerity);
        Assert.True(flags.KeepForceEncrypt);
        Assert.False(flags.PatchVbmetaFlag);
        Assert.False(flags.RecoveryMode);
        Assert.False(flags.LegacySar);
    }

    [Fact]
    public void Parse_tolerates_magiskboot_noise_around_the_pairs()
    {
        var flags = MagiskFlagsParser.Parse("Loading cpio: [ramdisk.cpio]\n" + RealProbeOutput + "\nDumping cpio: [x]");

        Assert.NotNull(flags);
        Assert.True(flags.KeepVerity);
    }

    [Theory]
    [InlineData("KEEPVERITY")]
    [InlineData("KEEPFORCEENCRYPT")]
    [InlineData("PATCHVBMETAFLAG")]
    [InlineData("RECOVERYMODE")]
    [InlineData("LEGACYSAR")]
    public void Parse_rejects_output_missing_any_required_flag(string dropped)
    {
        var lines = RealProbeOutput.Split('\n')
            .Where(line => !line.TrimStart().StartsWith(dropped + "=", StringComparison.Ordinal));

        Assert.Null(MagiskFlagsParser.Parse(string.Join('\n', lines)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("KEEPVERITY=yes\nKEEPFORCEENCRYPT=true\nPATCHVBMETAFLAG=false\nRECOVERYMODE=false\nLEGACYSAR=false")]
    public void Parse_rejects_empty_or_non_boolean_output(string? output) =>
        Assert.Null(MagiskFlagsParser.Parse(output));

    [Fact]
    public void ParsePatchedConfig_reads_a_real_patched_config_that_lacks_patch_time_only_flags()
    {
        var config = MagiskFlagsParser.ParsePatchedConfig(RealPatchedConfig);

        Assert.NotNull(config);
        Assert.True(config.Value.KeepVerity);
        Assert.True(config.Value.KeepForceEncrypt);
    }

    [Fact]
    public void ParsePatchedConfig_reads_the_unpatched_default_that_bootloops_this_device()
    {
        var config = MagiskFlagsParser.ParsePatchedConfig(
            RealPatchedConfig.Replace("=true", "=false", StringComparison.Ordinal));

        Assert.NotNull(config);
        Assert.False(config.Value.KeepVerity);
        Assert.False(config.Value.KeepForceEncrypt);
    }

    [Fact]
    public void ParsePatchedConfig_rejects_a_config_without_the_verity_flags() =>
        Assert.Null(MagiskFlagsParser.ParsePatchedConfig("PREINITDEVICE=cache\nSHA1=abc"));
}
