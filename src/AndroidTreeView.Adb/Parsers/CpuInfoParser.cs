using AndroidTreeView.Models.Hardware;

namespace AndroidTreeView.Adb.Parsers;

/// <summary>
/// Parses <c>/proc/cpuinfo</c> into a <see cref="CpuInfo"/>. Deterministic and stateless.
/// </summary>
public static class CpuInfoParser
{
    public static CpuInfo Parse(string? output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return new CpuInfo();
        }

        string? modelName = null;
        string? hardware = null;
        string? architecture = null;
        IReadOnlyList<string> features = Array.Empty<string>();
        var coreCount = 0;

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            var separator = line.IndexOf(':');
            if (separator < 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (key.Equals("processor", StringComparison.OrdinalIgnoreCase))
            {
                coreCount++;
            }
            else if (key.Equals("model name", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
            {
                modelName ??= value;
            }
            else if (key.Equals("Hardware", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
            {
                hardware ??= value;
            }
            else if (key.Equals("CPU architecture", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
            {
                architecture ??= value;
            }
            else if (key.Equals("Features", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
            {
                features = SplitFeatures(value);
            }
        }

        return new CpuInfo
        {
            Model = modelName ?? hardware,
            Hardware = hardware,
            Architecture = architecture,
            CoreCount = coreCount > 0 ? coreCount : null,
            Features = features
        };
    }

    private static string[] SplitFeatures(string value) =>
        value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
}
