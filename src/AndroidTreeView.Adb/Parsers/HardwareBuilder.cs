using AndroidTreeView.Adb.Commands;
using AndroidTreeView.Models.Hardware;

namespace AndroidTreeView.Adb.Parsers;

/// <summary>
/// Combines <c>getprop</c> values with parsed CPU, memory and screen data into a
/// <see cref="HardwareInfo"/>. Deterministic and stateless.
/// </summary>
public static class HardwareBuilder
{
    public static HardwareInfo Build(
        IReadOnlyDictionary<string, string> props,
        CpuInfo cpu,
        MemoryInfo memory,
        ScreenInfo screen)
    {
        ArgumentNullException.ThrowIfNull(props);
        ArgumentNullException.ThrowIfNull(cpu);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(screen);

        var abiList = SplitAbiList(Get(props, PropKeys.AbiList));

        return new HardwareInfo
        {
            CpuModel = cpu.Model,
            CpuArchitecture = cpu.Architecture ?? (abiList.Count > 0 ? abiList[0] : null),
            CpuCoreCount = cpu.CoreCount,
            AbiList = abiList,
            RamTotalBytes = memory.TotalBytes,
            RamAvailableBytes = memory.AvailableBytes,
            ScreenResolution = screen.Resolution,
            ScreenDensityDpi = screen.DensityDpi,
            Gpu = null,
            HardwarePlatform = Get(props, PropKeys.BoardPlatform),
            BoardName = Get(props, PropKeys.ProductBoard)
        };
    }

    private static IReadOnlyList<string> SplitAbiList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string? Get(IReadOnlyDictionary<string, string> props, string key) =>
        props.TryGetValue(key, out var value) && value.Length > 0 ? value : null;
}
