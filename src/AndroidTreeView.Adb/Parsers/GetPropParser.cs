using System.Text.RegularExpressions;

namespace AndroidTreeView.Adb.Parsers;

/// <summary>
/// Parses <c>getprop</c> output lines of the form <c>[key]: [value]</c> into a dictionary.
/// Deterministic and stateless.
/// </summary>
public static partial class GetPropParser
{
    [GeneratedRegex(@"^\[([^\]]*)\]:\s*\[(.*)\]\s*$")]
    private static partial Regex LineRegex();

    /// <summary>Parses raw getprop text into a key/value map. Unrecognized lines are ignored.</summary>
    public static IReadOnlyDictionary<string, string> Parse(string? output)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(output))
        {
            return result;
        }

        foreach (var line in output.Split('\n'))
        {
            var match = LineRegex().Match(line.Trim('\r', ' ', '\t'));
            if (!match.Success)
            {
                continue;
            }

            var key = match.Groups[1].Value;
            if (key.Length == 0)
            {
                continue;
            }

            result[key] = match.Groups[2].Value;
        }

        return result;
    }
}
