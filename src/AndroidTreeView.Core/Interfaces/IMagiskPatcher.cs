using AndroidTreeView.Models.Rooting;

namespace AndroidTreeView.Core.Interfaces;

/// <summary>Patches an extracted boot image on the explicitly selected ADB device.</summary>
public interface IMagiskPatcher
{
    Task<string> PatchAsync(
        string serial,
        BootImageInfo bootImage,
        CancellationToken ct = default);
}
