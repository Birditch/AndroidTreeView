namespace AndroidTreeView.Adb.Parsers;

/// <summary>Normalizes Android ABI output to the directory names used by official Magisk APKs.</summary>
public static class CpuAbiParser
{
    private static readonly HashSet<string> SupportedAbis = new(StringComparer.OrdinalIgnoreCase)
    {
        "arm64-v8a",
        "armeabi-v7a",
        "x86",
        "x86_64"
    };

    public static string? Parse(string? output)
    {
        var first = (output ?? string.Empty)
            .Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return first is not null && SupportedAbis.Contains(first) ? first.ToLowerInvariant() : null;
    }
}
