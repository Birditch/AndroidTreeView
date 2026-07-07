using System.Globalization;
using AndroidTreeView.Models.Hardware;

namespace AndroidTreeView.Adb.Parsers;

/// <summary>
/// Parses <c>/proc/meminfo</c> into a <see cref="MemoryInfo"/> (kB values converted to bytes).
/// Deterministic and stateless.
/// </summary>
public static class MemInfoParser
{
    private const long KilobyteToBytes = 1024L;

    public static MemoryInfo Parse(string? output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return new MemoryInfo();
        }

        long? total = null;
        long? available = null;
        long? free = null;

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            switch (key.ToLowerInvariant())
            {
                case "memtotal":
                    total = ParseKilobytes(value);
                    break;
                case "memavailable":
                    available = ParseKilobytes(value);
                    break;
                case "memfree":
                    free = ParseKilobytes(value);
                    break;
            }
        }

        return new MemoryInfo
        {
            TotalBytes = total,
            AvailableBytes = available,
            FreeBytes = free
        };
    }

    private static long? ParseKilobytes(string value)
    {
        // Values look like "3809036 kB"; take the leading number.
        var token = value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (token.Length == 0)
        {
            return null;
        }

        return long.TryParse(token[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var kb)
            ? kb * KilobyteToBytes
            : null;
    }
}
