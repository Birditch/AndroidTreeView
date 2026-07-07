namespace AndroidTreeView.Models.Hardware;

/// <summary>
/// Aggregated hardware summary combining CPU, memory, screen and board properties.
/// </summary>
public sealed class HardwareInfo
{
    public string? CpuModel { get; init; }
    public string? CpuArchitecture { get; init; }
    public int? CpuCoreCount { get; init; }

    /// <summary>Supported ABIs (<c>ro.product.cpu.abilist</c>).</summary>
    public IReadOnlyList<string> AbiList { get; init; } = Array.Empty<string>();

    public long? RamTotalBytes { get; init; }
    public long? RamAvailableBytes { get; init; }
    public string? ScreenResolution { get; init; }
    public int? ScreenDensityDpi { get; init; }
    public string? Gpu { get; init; }

    /// <summary><c>ro.board.platform</c>.</summary>
    public string? HardwarePlatform { get; init; }

    /// <summary><c>ro.product.board</c>.</summary>
    public string? BoardName { get; init; }
}
