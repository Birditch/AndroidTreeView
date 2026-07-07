using System.Runtime.CompilerServices;
using AndroidTreeView.Adb.Commands;
using AndroidTreeView.Adb.Parsers;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Options;
using AndroidTreeView.Core.Services;
using AndroidTreeView.Models.Logs;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.Adb.Services;

/// <summary>
/// Streams and clears logcat for a device. Streaming uses <c>logcat -v threadtime</c> and parses
/// each line into a <see cref="LogcatEntry"/>. Honors cancellation via the executor's stream.
/// </summary>
public sealed class LogcatService : ILogcatService
{
    private readonly IAdbCommandExecutor _executor;
    private readonly ILogger<LogcatService> _logger;

    public LogcatService(IAdbCommandExecutor executor, ILogger<LogcatService> logger)
    {
        _executor = executor;
        _logger = logger;
    }

    public async IAsyncEnumerable<LogcatEntry> StreamAsync(
        string serial,
        LogcatOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(serial);
        ArgumentNullException.ThrowIfNull(options);

        if (options.ClearBeforeStart)
        {
            await ClearAsync(serial, ct).ConfigureAwait(false);
        }

        var request = new AdbCommandRequest
        {
            Serial = serial,
            Arguments = AdbArgs.Logcat(options.MinPriority, options.TagFilter),
            RunInShell = false
        };

        _logger.LogDebug("Starting logcat stream for {Serial}.", serial);

        await foreach (var line in _executor.StreamAsync(request, ct).ConfigureAwait(false))
        {
            yield return LogcatParser.Parse(line);
        }
    }

    public async Task ClearAsync(string serial, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(serial);

        var request = new AdbCommandRequest
        {
            Serial = serial,
            Arguments = AdbArgs.LogcatClear,
            RunInShell = false
        };

        var result = await _executor.ExecuteAsync(request, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            _logger.LogDebug("logcat -c on {Serial} exited {Code}.", serial, result.ExitCode);
        }
    }
}
