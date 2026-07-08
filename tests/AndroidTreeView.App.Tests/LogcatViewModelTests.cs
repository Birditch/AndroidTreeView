using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AndroidTreeView.App.ViewModels;
using AndroidTreeView.Core.Options;
using AndroidTreeView.Models.Logs;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AndroidTreeView.App.Tests;

/// <summary>
/// Behavior tests for <see cref="LogcatViewModel"/>. These run on the Avalonia headless UI thread
/// (<c>[AvaloniaFact]</c>) because the view model marshals streamed entries via
/// <c>Dispatcher.UIThread.Post</c>; the dispatcher jobs are drained explicitly to keep tests deterministic.
/// </summary>
public sealed class LogcatViewModelTests
{
    private static LogcatViewModel CreateViewModel(
        IReadOnlyList<LogcatEntry> entries, int maxLines, bool streamForever = false)
    {
        var settings = new FakeSettingsService(new AppSettings { LogcatMaxLines = maxLines });
        return new LogcatViewModel(
            new FakeLogcatService(entries, streamForever),
            settings,
            new FakeLocalizationService(),
            NullLogger<LogcatViewModel>.Instance);
    }

    private static List<LogcatEntry> Entries(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new LogcatEntry { Message = $"m{i}", Priority = LogPriority.Info })
            .ToList();

    private static void DrainUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
            {
                Dispatcher.UIThread.RunJobs();
                return;
            }

            Thread.Sleep(10);
        }

        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Start_populates_entries_and_trims_to_max_lines()
    {
        var vm = CreateViewModel(Entries(10), maxLines: 3);
        _ = vm.LoadAsync("serial", CancellationToken.None);

        vm.StartCommand.Execute(null);
        DrainUntil(() => !vm.IsRunning);

        Assert.False(vm.IsRunning);
        Assert.Equal(3, vm.Entries.Count);
        Assert.Equal("m8", vm.Entries[0].Message);
        Assert.Equal("m10", vm.Entries[2].Message);
    }

    [AvaloniaFact]
    public void Clear_empties_entries()
    {
        var vm = CreateViewModel(Entries(5), maxLines: 100);
        _ = vm.LoadAsync("serial", CancellationToken.None);

        vm.StartCommand.Execute(null);
        DrainUntil(() => !vm.IsRunning);
        Assert.Equal(5, vm.Entries.Count);

        vm.ClearCommand.Execute(null);

        Assert.Empty(vm.Entries);
    }

    [AvaloniaFact]
    public async Task Stop_cancels_stream_without_throwing()
    {
        var vm = CreateViewModel(Entries(2), maxLines: 100, streamForever: true);
        _ = vm.LoadAsync("serial", CancellationToken.None);

        vm.StartCommand.Execute(null);

        // Let the finite prefix flow through the dispatcher; the stream then keeps running.
        for (var i = 0; i < 20 && vm.Entries.Count < 2; i++)
        {
            await Task.Delay(20);
        }

        Assert.True(vm.IsRunning);

        // Stopping must cancel cleanly and never throw out to the UI.
        await vm.StopCommand.ExecuteAsync(null);

        Assert.False(vm.IsRunning);
        Assert.False(vm.HasError);
    }
}
