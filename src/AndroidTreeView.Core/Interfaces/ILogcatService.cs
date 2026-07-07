using AndroidTreeView.Core.Options;
using AndroidTreeView.Models.Logs;

namespace AndroidTreeView.Core.Interfaces;

/// <summary>
/// Streams and clears logcat output for a device.
/// </summary>
public interface ILogcatService
{
    /// <summary>Streams parsed logcat entries until cancellation.</summary>
    IAsyncEnumerable<LogcatEntry> StreamAsync(string serial, LogcatOptions options, CancellationToken ct = default);

    /// <summary>Clears the device logcat buffer (<c>logcat -c</c>).</summary>
    Task ClearAsync(string serial, CancellationToken ct = default);
}
