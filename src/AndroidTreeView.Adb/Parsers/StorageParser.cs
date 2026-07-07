using System.Globalization;
using AndroidTreeView.Models.Storage;

namespace AndroidTreeView.Adb.Parsers;

/// <summary>
/// Parses <c>df</c> output into a <see cref="StorageInfo"/>. Tolerates both the POSIX 1K-block
/// format and the human-readable <c>df -h</c> format (K/M/G/T suffixes). Deterministic and stateless.
/// </summary>
public static class StorageParser
{
    private static readonly char[] Whitespace = { ' ', '\t' };

    // Mount points that represent the storage a user typically cares about.
    private static readonly HashSet<string> KeyMounts = new(StringComparer.Ordinal)
    {
        "/data", "/system", "/cache"
    };

    public static StorageInfo Parse(string? output)
    {
        var partitions = new List<StoragePartition>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return new StorageInfo { Partitions = partitions };
        }

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (!IsDataRow(line))
            {
                continue;
            }

            var partition = ParseRow(line);
            if (partition is not null)
            {
                partitions.Add(partition);
            }
        }

        return new StorageInfo { Partitions = partitions };
    }

    /// <summary>
    /// True when a mount point is one of the significant partitions the UI highlights
    /// (<c>/data</c>, <c>/system</c>, <c>/cache</c>, or external storage).
    /// </summary>
    public static bool IsSignificantMount(string? mountPoint)
    {
        if (string.IsNullOrEmpty(mountPoint))
        {
            return false;
        }

        return KeyMounts.Contains(mountPoint) ||
               mountPoint.StartsWith("/storage", StringComparison.Ordinal) ||
               mountPoint.StartsWith("/sdcard", StringComparison.Ordinal) ||
               mountPoint.StartsWith("/mnt/media_rw", StringComparison.Ordinal);
    }

    private static bool IsDataRow(string line)
    {
        if (line.Length == 0)
        {
            return false;
        }

        return !line.StartsWith("Filesystem", StringComparison.OrdinalIgnoreCase);
    }

    private static StoragePartition? ParseRow(string line)
    {
        var parts = line.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 6)
        {
            return null;
        }

        var name = parts[0];
        var total = ParseSize(parts[1]);
        var used = ParseSize(parts[2]);
        var available = ParseSize(parts[3]);
        var mount = parts[^1];

        var percentIndex = Array.FindIndex(parts, p => p.EndsWith('%'));
        var usePercent = percentIndex >= 0
            ? ParsePercent(parts[percentIndex])
            : ComputePercent(used, total);

        return new StoragePartition
        {
            Name = name,
            MountPoint = mount,
            TotalBytes = total,
            UsedBytes = used,
            AvailableBytes = available,
            UsePercent = usePercent
        };
    }

    /// <summary>
    /// Parses a size token. A bare integer is treated as a POSIX 1K block count; a token with a
    /// K/M/G/T/P suffix is treated as human-readable and converted to bytes.
    /// </summary>
    public static long? ParseSize(string token)
    {
        token = token.Trim();
        if (token.Length == 0 || token == "-")
        {
            return null;
        }

        var last = token[^1];
        if (char.IsLetter(last))
        {
            var multiplier = SuffixMultiplier(last);
            if (multiplier is null)
            {
                return null;
            }

            var numberPart = token[..^1];
            return double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? (long)(number * multiplier.Value)
                : null;
        }

        return long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var blocks)
            ? blocks * 1024L
            : null;
    }

    private static long? SuffixMultiplier(char suffix) => char.ToUpperInvariant(suffix) switch
    {
        'B' => 1L,
        'K' => 1024L,
        'M' => 1024L * 1024,
        'G' => 1024L * 1024 * 1024,
        'T' => 1024L * 1024 * 1024 * 1024,
        'P' => 1024L * 1024 * 1024 * 1024 * 1024,
        _ => null
    };

    private static double? ParsePercent(string token)
    {
        var trimmed = token.TrimEnd('%');
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static double? ComputePercent(long? used, long? total)
    {
        if (used is null || total is null || total.Value <= 0)
        {
            return null;
        }

        return Math.Round(used.Value * 100.0 / total.Value, 1);
    }
}
