using System.Collections.ObjectModel;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AndroidTreeView.App.ViewModels;

/// <summary>
/// Loads the raw <c>getprop</c> key/value snapshot for a device and exposes a filterable, sorted view.
/// </summary>
public sealed partial class RawPropertiesViewModel : DeviceCategoryViewModelBase
{
    private readonly IDeviceService _deviceService;

    [ObservableProperty]
    private DeviceProperties? _properties;

    [ObservableProperty]
    private string _filterText = string.Empty;

    public RawPropertiesViewModel(IDeviceService deviceService)
    {
        _deviceService = deviceService;
    }

    public override DeviceCategory Category => DeviceCategory.RawProperties;

    /// <summary>Properties matching the current filter (key or value contains the text), sorted by key.</summary>
    public ObservableCollection<PropertyRow> FilteredProperties { get; } = new();

    public override Task LoadAsync(string serial, CancellationToken ct)
    {
        Serial = serial;
        return RunAsync(async token =>
        {
            var properties = await _deviceService.GetPropertiesAsync(serial, token);
            Properties = properties;
            RebuildRows();
        }, ct);
    }

    partial void OnFilterTextChanged(string value) => RebuildRows();

    private void RebuildRows()
    {
        FilteredProperties.Clear();

        var properties = Properties;
        if (properties is null)
        {
            return;
        }

        var filter = FilterText;
        foreach (var pair in properties.Values.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            var value = pair.Value ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(filter)
                && !pair.Key.Contains(filter, StringComparison.OrdinalIgnoreCase)
                && !value.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            FilteredProperties.Add(new PropertyRow(pair.Key, value));
        }
    }
}

/// <summary>A single key/value pair shown in the raw properties table.</summary>
public sealed class PropertyRow
{
    public PropertyRow(string key, string value)
    {
        Key = key;
        Value = value;
    }

    public string Key { get; }

    public string Value { get; }
}
