using System.Collections.ObjectModel;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Options;
using AndroidTreeView.Models.Logs;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.App.ViewModels;

/// <summary>
/// Streams device logcat output. Streaming runs on a background task; each entry is marshalled to the
/// UI thread and appended to <see cref="Entries"/>, which is bounded by the configured max line count.
/// Filtering (tag/message text + minimum priority) is applied client-side over the received entries.
/// </summary>
public sealed partial class LogcatViewModel : DeviceCategoryViewModelBase, IDisposable
{
    private const int DefaultMaxLines = 5000;

    private readonly ILogcatService _logcat;
    private readonly ISettingsService _settings;
    private readonly ILocalizationService _localization;
    private readonly ILogger<LogcatViewModel> _logger;

    // Full unfiltered backlog (mutated on the UI thread only), used to rebuild the visible view.
    private readonly List<LogcatEntry> _allEntries = new();

    private CancellationTokenSource? _cts;
    private Task? _streamTask;

    /// <summary>Entries currently visible after applying the text and priority filters.</summary>
    public ObservableCollection<LogcatEntry> Entries { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private LogPriority _minPriority = LogPriority.Verbose;

    public LogcatViewModel(
        ILogcatService logcat,
        ISettingsService settings,
        ILocalizationService localization,
        ILogger<LogcatViewModel> logger)
    {
        _logcat = logcat;
        _settings = settings;
        _localization = localization;
        _logger = logger;
    }

    public override DeviceCategory Category => DeviceCategory.Logcat;

    /// <summary>Records the target serial. Logcat is not started automatically.</summary>
    public override Task LoadAsync(string serial, CancellationToken ct)
    {
        Serial = serial;
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        var serial = Serial;
        if (IsRunning || string.IsNullOrEmpty(serial))
        {
            return;
        }

        ErrorMessage = null;
        HasError = false;
        _cts = new CancellationTokenSource();
        IsRunning = true;
        _streamTask = StreamLoopAsync(serial, _cts.Token);
    }

    private bool CanStart() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        var cts = _cts;
        if (cts is null)
        {
            return;
        }

        try
        {
            await cts.CancelAsync().ConfigureAwait(true);
        }
        catch (ObjectDisposedException)
        {
            // Already torn down; nothing to cancel.
        }

        var task = _streamTask;
        if (task is not null)
        {
            try
            {
                await task.ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                // Expected on stop.
            }
        }

        cts.Dispose();
        _cts = null;
        _streamTask = null;
        IsRunning = false;
    }

    private bool CanStop() => IsRunning;

    [RelayCommand]
    private void Clear()
    {
        _allEntries.Clear();
        Entries.Clear();
    }

    private async Task StreamLoopAsync(string serial, CancellationToken ct)
    {
        var options = new LogcatOptions { MinPriority = MinPriority };
        try
        {
            await foreach (var entry in _logcat.StreamAsync(serial, options, ct).ConfigureAwait(false))
            {
                var received = entry;
                Dispatcher.UIThread.Post(() => AddEntry(received));
            }
        }
        catch (OperationCanceledException)
        {
            // Normal stop.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Logcat stream for {Serial} failed", serial);
            Dispatcher.UIThread.Post(() =>
            {
                ErrorMessage = _localization.Get("error.generic");
                HasError = true;
            });
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsRunning = false);
        }
    }

    private void AddEntry(LogcatEntry entry)
    {
        var max = GetMaxLines();

        _allEntries.Add(entry);
        if (_allEntries.Count > max)
        {
            _allEntries.RemoveRange(0, _allEntries.Count - max);
        }

        if (!PassesFilter(entry))
        {
            return;
        }

        Entries.Add(entry);
        while (Entries.Count > max)
        {
            Entries.RemoveAt(0);
        }
    }

    partial void OnFilterTextChanged(string value) => RebuildVisibleEntries();

    partial void OnMinPriorityChanged(LogPriority value) => RebuildVisibleEntries();

    private void RebuildVisibleEntries()
    {
        Entries.Clear();
        var max = GetMaxLines();
        foreach (var entry in _allEntries)
        {
            if (PassesFilter(entry))
            {
                Entries.Add(entry);
            }
        }

        while (Entries.Count > max)
        {
            Entries.RemoveAt(0);
        }
    }

    private bool PassesFilter(LogcatEntry entry)
    {
        if (entry.Priority != LogPriority.Unknown && entry.Priority < MinPriority)
        {
            return false;
        }

        var filter = FilterText;
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        var tagMatch = entry.Tag?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false;
        return tagMatch || entry.Message.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private int GetMaxLines()
    {
        var max = _settings.Current.LogcatMaxLines;
        return max > 0 ? max : DefaultMaxLines;
    }

    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed.
        }

        _cts?.Dispose();
        _cts = null;
    }
}
