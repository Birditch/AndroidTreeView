using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AndroidTreeView.Core.Interfaces;
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
    private int _deviceCount;

    [ObservableProperty]
    private DeviceCardViewModel? _selectedDevice;

    public DevicesViewModel(ILocalizationService localization)
    {
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
    }

    /// <summary>Cards currently visible in the grid (filtered subset of the full device list).</summary>
    public ObservableCollection<DeviceCardViewModel> Devices { get; } = new();

    /// <summary>Raised when a card is activated (clicked) so the shell can open its detail page.</summary>
    public event EventHandler<DeviceCardViewModel>? DeviceActivated;

    /// <summary>Raised when the user requests a manual refresh; the shell owns the monitor and reacts.</summary>
    public event EventHandler? RefreshRequested;

    /// <summary>
    /// Reconciles the current device list into the existing cards without recreating unchanged ones or
    /// losing the current selection.
    /// </summary>
    public void Reconcile(IReadOnlyList<AdbDevice> devices, bool adbAvailable)
    {
        IsAdbAvailable = adbAvailable;
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

            _all.RemoveAt(i);
        }

        // Add new cards and update existing ones, keeping the incoming order.
        var ordered = new List<DeviceCardViewModel>(incoming.Count);
        foreach (var device in incoming)
        {
            var card = _all.FirstOrDefault(c => string.Equals(c.Serial, device.Serial, StringComparison.Ordinal))
                       ?? new DeviceCardViewModel(_localization, ActivateDevice);
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

        ApplyFilter();
    }

    /// <summary>Marks <paramref name="card"/> as selected and raises <see cref="DeviceActivated"/>.</summary>
    public void ActivateDevice(DeviceCardViewModel card)
    {
        if (card is null)
        {
            return;
        }

        SelectedDevice = card;
        DeviceActivated?.Invoke(this, card);
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    private void Refresh() => RefreshRequested?.Invoke(this, EventArgs.Empty);

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

        // Rebuild the visible collection in order. Device lists are small, so a clear/re-add is cheap.
        Devices.Clear();
        foreach (var card in target)
        {
            Devices.Add(card);
        }
    }

    private static bool MatchesQuery(string? value, string query) =>
        !string.IsNullOrEmpty(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);
}
