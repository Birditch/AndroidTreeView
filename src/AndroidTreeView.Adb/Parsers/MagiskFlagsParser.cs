namespace AndroidTreeView.Adb.Parsers;

/// <summary>
/// The patch flags that official Magisk derives on the device itself (via <c>app_functions.sh</c>'s
/// <c>app_init</c>) and then passes to <c>boot_patch.sh</c> as environment variables.
/// </summary>
public sealed record MagiskPatchFlags
{
    public required bool KeepVerity { get; init; }
    public required bool KeepForceEncrypt { get; init; }
    public required bool PatchVbmetaFlag { get; init; }
    public required bool RecoveryMode { get; init; }
    public required bool LegacySar { get; init; }
}

/// <summary>
/// Parses the <c>KEY=VALUE</c> lines that <c>app_init</c> prints via its <c>printvar</c> helper.
/// </summary>
/// <remarks>
/// Every flag is required. <c>boot_patch.sh</c> silently defaults a missing flag to <c>false</c>, and on a
/// system-as-root device <c>KEEPVERITY=false</c> strips <c>logical</c> and <c>first_stage_mount</c> off the
/// <c>/system</c> fstab entry, which leaves first-stage init unable to mount the root filesystem. A missing or
/// unparsable flag must therefore abort the patch rather than fall back to a default.
/// </remarks>
public static class MagiskFlagsParser
{
    /// <summary>
    /// Parses the flags recorded inside a patched ramdisk's <c>.backup/.magisk</c>. That config only carries
    /// the flags Magisk needs at runtime — <c>PATCHVBMETAFLAG</c> and <c>LEGACYSAR</c> are patch-time only and
    /// are absent — so it cannot be read with <see cref="Parse"/>.
    /// </summary>
    public static (bool KeepVerity, bool KeepForceEncrypt)? ParsePatchedConfig(string? output)
    {
        var values = ReadPairs(output);
        var keepVerity = ReadBool(values, "KEEPVERITY");
        var keepForceEncrypt = ReadBool(values, "KEEPFORCEENCRYPT");
        return keepVerity is null || keepForceEncrypt is null
            ? null
            : (keepVerity.Value, keepForceEncrypt.Value);
    }

    public static MagiskPatchFlags? Parse(string? output)
    {
        var values = ReadPairs(output);
        var keepVerity = ReadBool(values, "KEEPVERITY");
        var keepForceEncrypt = ReadBool(values, "KEEPFORCEENCRYPT");
        var patchVbmetaFlag = ReadBool(values, "PATCHVBMETAFLAG");
        var recoveryMode = ReadBool(values, "RECOVERYMODE");
        var legacySar = ReadBool(values, "LEGACYSAR");
        if (keepVerity is null || keepForceEncrypt is null || patchVbmetaFlag is null
            || recoveryMode is null || legacySar is null)
        {
            return null;
        }

        return new MagiskPatchFlags
        {
            KeepVerity = keepVerity.Value,
            KeepForceEncrypt = keepForceEncrypt.Value,
            PatchVbmetaFlag = patchVbmetaFlag.Value,
            RecoveryMode = recoveryMode.Value,
            LegacySar = legacySar.Value
        };
    }

    private static Dictionary<string, string> ReadPairs(string? output)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(output))
        {
            return values;
        }

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            var separator = trimmed.IndexOf('=');
            if (separator > 0)
            {
                values[trimmed[..separator]] = trimmed[(separator + 1)..].Trim();
            }
        }

        return values;
    }

    private static bool? ReadBool(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value)
            ? value switch
            {
                "true" => true,
                "false" => false,
                _ => null
            }
            : null;
}
