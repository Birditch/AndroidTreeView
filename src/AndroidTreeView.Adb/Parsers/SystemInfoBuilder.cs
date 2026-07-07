using System.Globalization;
using System.Text.RegularExpressions;
using AndroidTreeView.Adb.Commands;
using AndroidTreeView.Models.System;

namespace AndroidTreeView.Adb.Parsers;

/// <summary>
/// Builds a <see cref="SystemInfo"/> from <c>getprop</c> values plus parsed kernel, SELinux
/// and uptime data. Deterministic and stateless.
/// </summary>
/// <remarks>
/// This file lives in the <c>AndroidTreeView.Adb.Parsers</c> namespace but references the
/// <c>AndroidTreeView.Models.System</c> namespace; BCL types are therefore used unqualified
/// (never with a <c>System.</c> prefix) to avoid the namespace collision.
/// </remarks>
public static partial class SystemInfoBuilder
{
    [GeneratedRegex(@"Linux version (\S+)")]
    private static partial Regex KernelRegex();

    [GeneratedRegex(@"\bup\b")]
    private static partial Regex UpMarkerRegex();

    [GeneratedRegex(@"(\d+)\s+days?")]
    private static partial Regex DaysRegex();

    [GeneratedRegex(@"(\d+)\s+min")]
    private static partial Regex MinutesRegex();

    [GeneratedRegex(@"(\d+):(\d+)")]
    private static partial Regex HourMinuteRegex();

    public static SystemInfo Build(
        IReadOnlyDictionary<string, string> props,
        string? kernelVersion,
        string? selinux,
        TimeSpan? uptime)
    {
        ArgumentNullException.ThrowIfNull(props);

        return new SystemInfo
        {
            KernelVersion = string.IsNullOrWhiteSpace(kernelVersion) ? null : kernelVersion.Trim(),
            SelinuxStatus = string.IsNullOrWhiteSpace(selinux) ? null : selinux.Trim(),
            Uptime = uptime,
            Bootloader = Get(props, PropKeys.Bootloader),
            VerifiedBootState = Get(props, PropKeys.VerifiedBootState),
            BuildTags = Get(props, PropKeys.BuildTags),
            BuildType = Get(props, PropKeys.BuildType),
            Locale = GetFirst(props, PropKeys.LocaleKeys),
            Timezone = Get(props, PropKeys.Timezone),
            SdkVersion = GetInt(props, PropKeys.Sdk)
        };
    }

    /// <summary>Extracts the kernel version token from <c>/proc/version</c> output.</summary>
    public static string? ParseKernel(string? procVersion)
    {
        if (string.IsNullOrWhiteSpace(procVersion))
        {
            return null;
        }

        var match = KernelRegex().Match(procVersion);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Parses device uptime from either the <c>uptime</c> command
    /// ("... up 3 days, 2:45, ...") or a raw <c>/proc/uptime</c> seconds value.
    /// </summary>
    public static TimeSpan? ParseUptime(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var text = output.Trim();
        var marker = UpMarkerRegex().Match(text);
        if (!marker.Success)
        {
            return ParseSeconds(text);
        }

        var segment = Isolate(text[(marker.Index + 2)..]);
        return ComposeUptime(segment);
    }

    private static TimeSpan? ComposeUptime(string segment)
    {
        var total = TimeSpan.Zero;
        var matched = false;

        var days = DaysRegex().Match(segment);
        if (days.Success && int.TryParse(days.Groups[1].Value, out var dayCount))
        {
            total += TimeSpan.FromDays(dayCount);
            matched = true;
        }

        var hourMinute = HourMinuteRegex().Match(segment);
        if (hourMinute.Success &&
            int.TryParse(hourMinute.Groups[1].Value, out var hours) &&
            int.TryParse(hourMinute.Groups[2].Value, out var minutes))
        {
            total += new TimeSpan(hours, minutes, 0);
            matched = true;
        }
        else
        {
            var minsOnly = MinutesRegex().Match(segment);
            if (minsOnly.Success && int.TryParse(minsOnly.Groups[1].Value, out var mins))
            {
                total += TimeSpan.FromMinutes(mins);
                matched = true;
            }
        }

        return matched ? total : null;
    }

    private static string Isolate(string segment)
    {
        // Drop the trailing "N users, load average: ..." portion so its numbers are not misread.
        var cut = segment.Length;
        foreach (var marker in new[] { "user", "load average" })
        {
            var index = segment.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && index < cut)
            {
                cut = index;
            }
        }

        return segment[..cut];
    }

    private static TimeSpan? ParseSeconds(string text)
    {
        var token = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (token is null)
        {
            return null;
        }

        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    private static string? Get(IReadOnlyDictionary<string, string> props, string key) =>
        props.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

    private static string? GetFirst(IReadOnlyDictionary<string, string> props, string[] keys)
    {
        foreach (var key in keys)
        {
            var value = Get(props, key);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static int? GetInt(IReadOnlyDictionary<string, string> props, string key) =>
        props.TryGetValue(key, out var value) &&
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
