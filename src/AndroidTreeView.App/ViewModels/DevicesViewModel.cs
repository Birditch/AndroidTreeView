using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using AndroidTreeView.App.Services;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Services;
using AndroidTreeView.Models.Devices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTreeView.App.ViewModels;

/// <summary>
/// Backs the primary device card grid. Reconciles the incoming <see cref="AdbDevice"/> list from the
/// monitor into <see cref="DeviceCardViewModel"/> instances (add / update / remove by serial), assigns
/// 1-based indices, preserves selection, and exposes a text-filtered view over the cards.
/// </summary>
public sealed partial class DevicesViewModel : ViewModelBase
{
    private readonly ILocalizationService _localization;
    private readonly IDeviceActionsService _actions;
    private readonly DeviceFileTransferService _fileTransfer;
    private readonly IFilePickerService _filePicker;
    private readonly IFastbootService _fastboot;
    private readonly IDialogService _dialog;

    // Full, ordered backlog of cards (device numbering is stable across filtering).
    private readonly List<DeviceCardViewModel> _all = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _hasDevices;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _isAdbAvailable;

    [ObservableProperty]
    private bool _isAdbMissing;

    [ObservableProperty]
    private int _deviceCount;

    /// <summary>Localized "last refresh: HH:mm:ss" for the whole list, shown once next to the title.</summary>
    [ObservableProperty]
    private string _lastRefreshText = string.Empty;

    [ObservableProperty]
    private DeviceCardViewModel? _selectedDevice;

    /// <summary>Number of cards checked for a batch operation.</summary>
    [ObservableProperty]
    private int _selectedCount;

    /// <summary>Whether at least one card is checked (shows the batch action bar).</summary>
    [ObservableProperty]
    private bool _hasSelection;

    /// <summary>Localized "N selected" text for the batch bar.</summary>
    [ObservableProperty]
    private string _selectionText = string.Empty;

    public DevicesViewModel(
        ILocalizationService localization,
        IDeviceActionsService actions,
        DeviceFileTransferService fileTransfer,
        IFilePickerService filePicker,
        IFastbootService fastboot,
        IDialogService dialog)
    {
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _fileTransfer = fileTransfer ?? throw new ArgumentNullException(nameof(fileTransfer));
        _filePicker = filePicker ?? throw new ArgumentNullException(nameof(filePicker));
        _fastboot = fastboot ?? throw new ArgumentNullException(nameof(fastboot));
        _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
    }

    /// <summary>Cards currently visible in the grid (filtered subset of the full device list).</summary>
    public ObservableCollection<DeviceCardViewModel> Devices { get; } = new();

    /// <summary>Raised when a card is activated (clicked) so the shell can open its detail page.</summary>
    public event EventHandler<DeviceCardViewModel>? DeviceActivated;

    /// <summary>Raised when the user requests a manual refresh; the shell owns the monitor and reacts.</summary>
    public event EventHandler? RefreshRequested;

    /// <summary>Raised when the user asks to set up ADB (from the "ADB not found" state).</summary>
    public event EventHandler? SetupRequested;

    /// <summary>Raised when the user opens screen mirror for a device; the shell shows the window.</summary>
    public event EventHandler<DeviceCardViewModel>? ScreenRequested;

    /// <summary>Raised when the user opens a CLI terminal for a device; the shell launches it.</summary>
    public event EventHandler<DeviceCardViewModel>? CliRequested;

    /// <summary>
    /// Reconciles the current device list into the existing cards without recreating unchanged ones or
    /// losing the current selection.
    /// </summary>
    public void Reconcile(IReadOnlyList<AdbDevice> devices, bool adbAvailable)
    {
        IsAdbAvailable = adbAvailable;
        IsAdbMissing = !adbAvailable;
        var incoming = devices ?? (IReadOnlyList<AdbDevice>)Array.Empty<AdbDevice>();

        var incomingSerials = new HashSet<string>(incoming.Select(d => d.Serial), StringComparer.Ordinal);

        // Drop cards for devices that are gone; clear selection if the selected device vanished.
        for (var i = _all.Count - 1; i >= 0; i--)
        {
            if (incomingSerials.Contains(_all[i].Serial))
            {
                continue;
            }

            if (ReferenceEquals(SelectedDevice, _all[i]))
            {
                SelectedDevice = null;
            }

            _all[i].PropertyChanged -= OnCardPropertyChanged;
            _all[i].ScreenRequested -= OnCardScreenRequested;
            _all[i].CliRequested -= OnCardCliRequested;
            _all.RemoveAt(i);
        }

        // Add new cards and update existing ones, keeping the incoming order.
        var ordered = new List<DeviceCardViewModel>(incoming.Count);
        foreach (var device in incoming)
        {
            var card = _all.FirstOrDefault(c => string.Equals(c.Serial, device.Serial, StringComparison.Ordinal));
            if (card is null)
            {
                card = new DeviceCardViewModel(_localization, _actions, _fastboot, _dialog, ActivateDevice);
                card.PropertyChanged += OnCardPropertyChanged;
                card.ScreenRequested += OnCardScreenRequested;
                card.CliRequested += OnCardCliRequested;
            }

            card.UpdateFrom(device, battery: null, root: null);
            ordered.Add(card);
        }

        _all.Clear();
        _all.AddRange(ordered);

        // Stable 1-based device numbering (#1, #2, …) over the full list.
        for (var i = 0; i < _all.Count; i++)
        {
            _all[i].Index = i + 1;
        }

        DeviceCount = _all.Count;
        HasDevices = _all.Count > 0;
        IsEmpty = adbAvailable && _all.Count == 0;

        LastRefreshText = string.Format(
            _localization.CurrentCulture,
            "{0}: {1}",
            _localization.Get("card.lastrefresh"),
            DateTimeOffset.Now.LocalDateTime.ToString("HH:mm:ss", _localization.CurrentCulture));

        ApplyFilter();
        RecomputeSelection();
    }

    /// <summary>Marks <paramref name="card"/> as selected and raises <see cref="DeviceActivated"/>.</summary>
    public void ActivateDevice(DeviceCardViewModel card)
    {
        if (card is null || !card.CanOpenDetail)
        {
            return;
        }

        SelectedDevice = card;
        DeviceActivated?.Invoke(this, card);
    }

    /// <summary>Finds a card by serial across the full device list (including filtered-out cards).</summary>
    public DeviceCardViewModel? FindCard(string serial) =>
        _all.FirstOrDefault(c => string.Equals(c.Serial, serial, StringComparison.Ordinal));

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    private void Refresh() => RefreshRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenSetup() => SetupRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var card in _all)
        {
            card.IsSelected = true;
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var card in _all)
        {
            card.IsSelected = false;
        }
    }

    [RelayCommand]
    private async Task BatchPickTransferFilesAsync()
    {
        var paths = await _filePicker.PickTransferFilesAsync().ConfigureAwait(true);
        await ProcessPickedFilesAsync(paths, "batch.files.result").ConfigureAwait(true);
    }

    public Task HandleDroppedFilesAsync(IReadOnlyList<string> paths) =>
        ProcessPickedFilesAsync(paths, "batch.drop.result");

    private Task ProcessPickedFilesAsync(IReadOnlyList<string> paths, string resultKey)
    {
        var targetSerials = SelectedOnlineCards().Select(card => card.Serial).ToArray();
        if (targetSerials.Length == 0)
        {
            Notifier.Notify(_localization.Get("batch.files.nodevices"), NotifierLevel.Warning);
            return Task.CompletedTask;
        }

        return ProcessFilesOnSelectedAsync(targetSerials, paths, resultKey);
    }

    private async Task ProcessFilesOnSelectedAsync(
        IReadOnlyList<string> targetSerials,
        IReadOnlyList<string> paths,
        string resultKey)
    {
        var result = await _fileTransfer.ProcessAsync(targetSerials, paths)
            .ConfigureAwait(true);
        if (result.ValidFileCount == 0)
        {
            Notifier.Notify(_localization.Get("batch.files.none"), NotifierLevel.Warning);
            return;
        }

        var level = result.TotalFailed == 0
            ? NotifierLevel.Success
            : result.TotalSucceeded == 0 ? NotifierLevel.Error : NotifierLevel.Warning;

        Notifier.Notify(
            _localization.Format(
                resultKey,
                result.InstallSucceeded,
                result.InstallFailed,
                result.TransferSucceeded,
                result.TransferFailed),
            level);
    }

    private void OnCardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeviceCardViewModel.IsSelected))
        {
            RecomputeSelection();
        }
    }

    private void OnCardScreenRequested(object? sender, EventArgs e)
    {
        if (sender is DeviceCardViewModel card)
        {
            ScreenRequested?.Invoke(this, card);
        }
    }

    private void OnCardCliRequested(object? sender, EventArgs e)
    {
        if (sender is DeviceCardViewModel card)
        {
            CliRequested?.Invoke(this, card);
        }
    }

    private void RecomputeSelection()
    {
        SelectedCount = _all.Count(c => c.IsSelected);
        HasSelection = SelectedCount > 0;
        SelectionText = _localization.Format("batch.selected", SelectedCount);
    }

    private void ApplyFilter()
    {
        var query = SearchText?.Trim();
        IEnumerable<DeviceCardViewModel> filtered = _all;

        if (!string.IsNullOrEmpty(query))
        {
            filtered = _all.Where(c =>
                MatchesQuery(c.DisplayName, query) ||
                MatchesQuery(c.Model, query) ||
                MatchesQuery(c.Serial, query));
        }

        var target = filtered.ToList();

        // Sync the visible collection to `target` IN PLACE. A full Clear()+re-add would recreate every
        // ItemsControl container on each 1 Hz refresh — which closes any open context menu and drops
        // hover/selection. So only apply the real add/remove/move diffs: no change => no CollectionChanged.
        for (var i = Devices.Count - 1; i >= 0; i--)
        {
            if (!target.Contains(Devices[i]))
            {
                Devices.RemoveAt(i);
            }
        }

        for (var i = 0; i < target.Count; i++)
        {
            var card = target[i];
            var currentIndex = Devices.IndexOf(card);
            if (currentIndex < 0)
            {
                Devices.Insert(i, card);
            }
            else if (currentIndex != i)
            {
                Devices.Move(currentIndex, i);
            }
        }
    }

    private static bool MatchesQuery(string? value, string query) =>
        !string.IsNullOrEmpty(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<DeviceCardViewModel> SelectedOnlineCards() =>
        _all.Where(card => card.IsSelected && card.IsOnline).ToArray();

}
