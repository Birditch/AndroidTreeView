using System.Globalization;
using System.Text.RegularExpressions;

namespace AndroidTreeView.Adb.Parsers;

/// <summary>
/// Parses <c>wm size</c> and <c>wm density</c> output. Deterministic and stateless.
/// </summary>
public static partial class ScreenParser
{
    [GeneratedRegex(@"(?:Physical|Override)\s+size:\s*(\d+x\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex SizeRegex();

    [GeneratedRegex(@"(?:Physical|Override)\s+density:\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex DensityRegex();

    /// <summary>
    /// Returns the resolution string (e.g. "1080x2400"). An override size, when present,
    /// takes precedence over the physical size because it reflects the effective display.
    /// </summary>
    public static string? ParseSize(string? output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return null;
        }

        string? physical = null;
        string? overridden = null;

        foreach (Match match in SizeRegex().Matches(output))
        {
            var value = match.Groups[1].Value;
            if (match.Value.Contains("Override", StringComparison.OrdinalIgnoreCase))
            {
                overridden = value;
            }
            else
            {
                physical = value;
            }
        }

        return overridden ?? physical;
    }

    /// <summary>Returns the density in DPI, preferring an override density when present.</summary>
    public static int? ParseDensity(string? output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return null;
        }

        int? physical = null;
        int? overridden = null;

        foreach (Match match in DensityRegex().Matches(output))
        {
            if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            if (match.Value.Contains("Override", StringComparison.OrdinalIgnoreCase))
            {
                overridden = value;
            }
            else
            {
                physical = value;
            }
        }

        return overridden ?? physical;
    }
}
