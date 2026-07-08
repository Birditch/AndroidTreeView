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
            CpuModel        = ResolveCpuModel(props, cpu),
            CpuArchitecture = ResolveCpuArchitecture(props, cpu, abiList),
            CpuCoreCount    = cpu.CoreCount,
            AbiList         = abiList,
            RamTotalBytes   = memory.TotalBytes,
            RamAvailableBytes = memory.AvailableBytes,
            ScreenResolution = screen.Resolution,
            ScreenDensityDpi = screen.DensityDpi,
            Gpu              = null,
            HardwarePlatform = Get(props, PropKeys.BoardPlatform),
            BoardName        = Get(props, PropKeys.ProductBoard)
        };
    }

    /// <summary>
    /// Resolves the CPU model name with priority:
    /// ro.soc.manufacturer + ro.soc.model → ro.board.platform → ro.hardware → cpuinfo.
    /// </summary>
    private static string? ResolveCpuModel(IReadOnlyDictionary<string, string> props, CpuInfo cpu)
    {
        var socMfr   = Get(props, PropKeys.SocManufacturer);
        var socModel = Get(props, PropKeys.SocModel);
        if (socMfr != null || socModel != null)
            return socMfr != null && socModel != null ? $"{socMfr} {socModel}" : (socMfr ?? socModel);

        return Get(props, PropKeys.BoardPlatform)
            ?? Get(props, PropKeys.Hardware)
            ?? cpu.Model;
    }

    /// <summary>
    /// Resolves the CPU architecture with priority:
    /// ro.product.cpu.abi → mapped architecture number → first ABI list entry.
    /// Never returns a bare numeric string such as "8".
    /// </summary>
    private static string? ResolveCpuArchitecture(
        IReadOnlyDictionary<string, string> props,
        CpuInfo cpu,
        IReadOnlyList<string> abiList)
    {
        var cpuAbi = Get(props, PropKeys.CpuAbi);
        if (cpuAbi != null)
            return cpuAbi;

        if (cpu.Architecture != null)
            return MapArchNumber(cpu.Architecture);

        return abiList.Count > 0 ? abiList[0] : null;
    }

    private static string MapArchNumber(string architecture) => architecture.Trim() switch
    {
        "8" => "ARMv8-A",
        "7" => "ARMv7-A",
        _   => architecture
    };

    private static IReadOnlyList<string> SplitAbiList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string? Get(IReadOnlyDictionary<string, string> props, string key) =>
        props.TryGetValue(key, out var value) && value.Length > 0 ? value : null;
}
