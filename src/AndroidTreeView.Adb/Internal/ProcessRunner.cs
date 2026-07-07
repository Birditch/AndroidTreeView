using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace AndroidTreeView.Adb.Internal;

/// <summary>Buffered result of a completed process run.</summary>
internal sealed record ProcessRunResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    TimeSpan Duration);

/// <summary>
/// Async wrapper over <see cref="Process"/>. Redirects and drains stdout/stderr concurrently
/// (so full pipe buffers never deadlock), applies a timeout via a linked
/// <see cref="CancellationTokenSource"/>, and kills the whole process tree on cancel/timeout.
/// No blocking calls (<c>.Result</c>/<c>.Wait()</c>) are used.
/// </summary>
internal static class ProcessRunner
{
    /// <summary>
    /// Runs a process to completion, capturing stdout and stderr. On timeout the whole process
    /// tree is killed and <see cref="ProcessRunResult.TimedOut"/> is set. On external cancellation
    /// an <see cref="OperationCanceledException"/> is thrown after the tree is killed.
    /// </summary>
    public static async Task<ProcessRunResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = BuildStartInfo(fileName, arguments) };

        process.Start();

        // Begin draining both pipes immediately and concurrently.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested;
            KillTree(process);
            await SafeWaitForExitAsync(process).ConfigureAwait(false);

            if (!timedOut)
            {
                // A genuine external cancellation: surface it after cleanup.
                ct.ThrowIfCancellationRequested();
            }
        }

        var stdout = await SafeReadAsync(stdoutTask).ConfigureAwait(false);
        var stderr = await SafeReadAsync(stderrTask).ConfigureAwait(false);
        stopwatch.Stop();

        var exitCode = timedOut ? -1 : SafeExitCode(process);
        return new ProcessRunResult(exitCode, stdout, stderr, timedOut, stopwatch.Elapsed);
    }

    /// <summary>
    /// Runs a process and yields its stdout lines as they arrive via a <see cref="Channel{T}"/>.
    /// Used for long-lived streams such as logcat. The process tree is killed on cancellation
    /// or when enumeration ends.
    /// </summary>
    public static async IAsyncEnumerable<string> StreamAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        using var process = new Process { StartInfo = BuildStartInfo(fileName, arguments) };
        process.EnableRaisingEvents = true;
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                channel.Writer.TryWrite(e.Data);
            }
        };
        process.Exited += (_, _) => channel.Writer.TryComplete();

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            while (await channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out var line))
                {
                    yield return line;
                }
            }
        }
        finally
        {
            channel.Writer.TryComplete();
            KillTree(process);
            await SafeWaitForExitAsync(process).ConfigureAwait(false);
        }
    }

    private static ProcessStartInfo BuildStartInfo(string fileName, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited / never started.
        }
        catch (NotSupportedException)
        {
            // Platform without tree-kill support: best-effort only.
        }
    }

    private static async Task SafeWaitForExitAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Nothing to wait for.
        }
    }

    private static async Task<string> SafeReadAsync(Task<string> readTask)
    {
        try
        {
            return await readTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static int SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }
}
