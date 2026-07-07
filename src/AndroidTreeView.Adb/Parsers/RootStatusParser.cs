using AndroidTreeView.Models.Devices;

namespace AndroidTreeView.Adb.Parsers;

/// <summary>
/// Combines the outputs of several read-only probes into a <see cref="RootStatus"/>.
/// Deterministic and stateless. Never runs anything; it only interprets command output.
/// </summary>
public static class RootStatusParser
{
    public static RootStatus Parse(
        string? idOutput,
        string? whichSuOutput,
        string? suIdOutput,
        string? getenforceOutput,
        string? magiskProp)
    {
        var currentUserId = Normalize(idOutput);
        var suBinaryPath = ExtractSuPath(whichSuOutput);
        var suBinaryExists = suBinaryPath is not null;
        var rootUserId = HasRootUid(suIdOutput) ? Normalize(suIdOutput) : null;
        var suGrantsRoot = rootUserId is not null;
        var magiskVersion = Normalize(magiskProp);
        var currentIsRoot = HasRootUid(idOutput);

        return new RootStatus
        {
            SuBinaryExists = suBinaryExists,
            SuGrantsRoot = suGrantsRoot,
            CurrentUserId = currentUserId,
            RootUserId = rootUserId,
            MagiskVersion = magiskVersion,
            SelinuxMode = Normalize(getenforceOutput),
            Level = DetermineLevel(currentIsRoot, suGrantsRoot, suBinaryExists, magiskVersion)
        };
    }

    private static RootDetectionLevel DetermineLevel(
        bool currentIsRoot,
        bool suGrantsRoot,
        bool suBinaryExists,
        string? magiskVersion)
    {
        if (currentIsRoot || suGrantsRoot)
        {
            return RootDetectionLevel.Confirmed;
        }

        if (suBinaryExists || !string.IsNullOrEmpty(magiskVersion))
        {
            return RootDetectionLevel.Likely;
        }

        return RootDetectionLevel.NotRooted;
    }

    private static bool HasRootUid(string? output) =>
        !string.IsNullOrEmpty(output) &&
        output.Contains("uid=0", StringComparison.Ordinal);

    private static string? ExtractSuPath(string? output)
    {
        var text = Normalize(output);
        if (text is null)
        {
            return null;
        }

        // Shell "not found" / "which: no su" style responses do not indicate a binary.
        if (text.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("no su", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var firstLine = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(firstLine))
        {
            return null;
        }

        return firstLine.EndsWith("su", StringComparison.Ordinal) ? firstLine : null;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
