using System.ComponentModel;
using System.Runtime.CompilerServices;
using AndroidTreeView.Adb.Internal;
using AndroidTreeView.Core.Exceptions;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Options;
using AndroidTreeView.Core.Services;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.Adb.Services;

/// <summary>
/// Executes <see cref="AdbCommandRequest"/> instances against the resolved adb executable.
/// Ordinary non-zero exits are returned as results; only a genuinely missing adb raises
/// <see cref="AdbNotFoundException"/>.
/// </summary>
public sealed class AdbCommandExecutor : IAdbCommandExecutor
{
    private readonly IAdbEnvironment _environment;
    private readonly AdbOptions _options;
    private readonly ILogger<AdbCommandExecutor> _logger;

    public AdbCommandExecutor(
        IAdbEnvironment environment,
        AdbOptions options,
        ILogger<AdbCommandExecutor> logger)
    {
        _environment = environment;
        _options = options;
        _logger = logger;
    }

    public async Task<AdbCommandResult> ExecuteAsync(AdbCommandRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var executable = _environment.ExecutablePath;
        var argv = BuildArguments(request);
        var timeout = request.Timeout ?? _options.DefaultCommandTimeout;

        _logger.LogDebug("Executing adb {Arguments}", string.Join(' ', argv));

        try
        {
            var run = await ProcessRunner.RunAsync(executable, argv, timeout, ct).ConfigureAwait(false);
            return new AdbCommandResult
            {
                ExitCode = run.ExitCode,
                StandardOutput = run.StandardOutput,
                StandardError = run.StandardError,
                TimedOut = run.TimedOut,
                Duration = run.Duration
            };
        }
        catch (Win32Exception ex)
        {
            // The executable disappeared or is not runnable after resolution.
            throw new AdbNotFoundException(
                $"The adb executable at '{executable}' could not be started.", ex);
        }
    }

    public async IAsyncEnumerable<string> StreamAsync(
        AdbCommandRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var executable = _environment.ExecutablePath;
        var argv = BuildArguments(request);

        _logger.LogDebug("Streaming adb {Arguments}", string.Join(' ', argv));

        await foreach (var line in ProcessRunner.StreamAsync(executable, argv, ct).ConfigureAwait(false))
        {
            yield return line;
        }
    }

    private static IReadOnlyList<string> BuildArguments(AdbCommandRequest request)
    {
        var argv = new List<string>();

        if (!string.IsNullOrEmpty(request.Serial))
        {
            argv.Add("-s");
            argv.Add(request.Serial);
        }

        if (request.RunInShell)
        {
            argv.Add("shell");
        }

        argv.AddRange(request.Arguments);
        return argv;
    }
}
