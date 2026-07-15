using AndroidTreeView.Models.Rooting;

namespace AndroidTreeView.Core.Interfaces;

/// <summary>Validates a firmware package and extracts its evidence-backed boot target.</summary>
public interface IBootImageExtractor
{
    Task<BootImageInfo> ExtractAsync(
        string packagePath,
        RootDeviceIdentity device,
        CancellationToken ct = default);
}
