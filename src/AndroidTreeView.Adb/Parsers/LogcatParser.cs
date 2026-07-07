using System.Globalization;
using System.Text.RegularExpressions;
using AndroidTreeView.Models.Logs;

namespace AndroidTreeView.Adb.Parsers;

/// <summary>
/// Parses a single <c>logcat -v threadtime</c> line into a <see cref="LogcatEntry"/>.
/// Deterministic and stateless. Unparseable lines are preserved as raw messages.
/// </summary>
public static partial class LogcatParser
{
    // Example: "06-25 14:23:01.123  1234  5678 I ActivityManager: Start proc ..."
    [GeneratedRegex(@"^(\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}\.\d+)\s+(\d+)\s+(\d+)\s+([VDIWEFS])\s+(.*?):\s?(.*)$")]
    private static partial Regex ThreadTimeRegex();

    public static LogcatEntry Parse(string line)
    {
        var text = line ?? string.Empty;
        var match = ThreadTimeRegex().Match(text);
        if (!match.Success)
        {
            return new LogcatEntry
            {
                Priority = LogPriority.Unknown,
                Message = text
            };
        }

        return new LogcatEntry
        {
            Timestamp = match.Groups[1].Value,
            Pid = ParseInt(match.Groups[2].Value),
            Tid = ParseInt(match.Groups[3].Value),
            Priority = MapPriority(match.Groups[4].Value[0]),
            Tag = match.Groups[5].Value.Trim(),
            Message = match.Groups[6].Value
        };
    }

    private static LogPriority MapPriority(char code) => code switch
    {
        'V' => LogPriority.Verbose,
        'D' => LogPriority.Debug,
        'I' => LogPriority.Info,
        'W' => LogPriority.Warn,
        'E' => LogPriority.Error,
        'F' => LogPriority.Fatal,
        'S' => LogPriority.Silent,
        _ => LogPriority.Unknown
    };

    private static int? ParseInt(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
}
