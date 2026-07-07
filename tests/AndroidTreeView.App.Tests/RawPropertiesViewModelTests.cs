using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AndroidTreeView.App.ViewModels;
using AndroidTreeView.Models;
using Xunit;

namespace AndroidTreeView.App.Tests;

/// <summary>
/// Behavior tests for <see cref="RawPropertiesViewModel"/>: loading via a fake <c>IDeviceService</c>
/// populates <c>FilteredProperties</c> (sorted by key), and <c>FilterText</c> matches key or value.
/// </summary>
public sealed class RawPropertiesViewModelTests
{
    private static DeviceProperties Properties() => new()
    {
        Values = new Dictionary<string, string>
        {
            ["ro.product.model"] = "Pixel 8",
            ["ro.build.version.release"] = "14",
            ["ro.product.manufacturer"] = "Google",
        },
    };

    [Fact]
    public async Task LoadAsync_populates_filtered_properties_sorted_by_key()
    {
        var service = new FakeDeviceService(Properties());
        var vm = new RawPropertiesViewModel(service);

        await vm.LoadAsync("serial-1", CancellationToken.None);

        Assert.Equal(1, service.PropertiesCallCount);
        Assert.Equal(3, vm.FilteredProperties.Count);
        Assert.False(vm.HasError);
        Assert.False(vm.IsLoading);

        var keys = vm.FilteredProperties.Select(p => p.Key).ToList();
        Assert.Equal(keys.OrderBy(k => k, System.StringComparer.OrdinalIgnoreCase).ToList(), keys);
    }

    [Fact]
    public async Task FilterText_filters_by_key()
    {
        var vm = new RawPropertiesViewModel(new FakeDeviceService(Properties()));
        await vm.LoadAsync("serial-1", CancellationToken.None);

        vm.FilterText = "manufacturer";

        var row = Assert.Single(vm.FilteredProperties);
        Assert.Equal("ro.product.manufacturer", row.Key);
        Assert.Equal("Google", row.Value);
    }

    [Fact]
    public async Task FilterText_filters_by_value()
    {
        var vm = new RawPropertiesViewModel(new FakeDeviceService(Properties()));
        await vm.LoadAsync("serial-1", CancellationToken.None);

        vm.FilterText = "Pixel";

        var row = Assert.Single(vm.FilteredProperties);
        Assert.Equal("ro.product.model", row.Key);
    }

    [Fact]
    public async Task FilterText_cleared_restores_all_rows()
    {
        var vm = new RawPropertiesViewModel(new FakeDeviceService(Properties()));
        await vm.LoadAsync("serial-1", CancellationToken.None);

        vm.FilterText = "Google";
        Assert.Single(vm.FilteredProperties);

        vm.FilterText = string.Empty;
        Assert.Equal(3, vm.FilteredProperties.Count);
    }
}
