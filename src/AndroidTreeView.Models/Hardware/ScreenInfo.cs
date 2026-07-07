namespace AndroidTreeView.Models.Hardware;

/// <summary>
/// Display metrics parsed from <c>wm size</c> / <c>wm density</c>.
/// </summary>
public sealed class ScreenInfo
{
    /// <summary>Physical resolution string, e.g. "1080x2400".</summary>
    public string? Resolution { get; init; }

    public int? DensityDpi { get; init; }
}
