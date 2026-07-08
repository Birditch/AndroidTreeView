using System.Collections.Generic;
using System.Linq;
using AndroidTreeView.App.ViewModels;
using AndroidTreeView.Models.Devices;
using Xunit;

namespace AndroidTreeView.App.Tests;

/// <summary>
/// Behavior tests for <see cref="DevicesViewModel.Reconcile"/> and search filtering, using a
/// deterministic <see cref="FakeLocalizationService"/>.
/// </summary>
public sealed class DevicesViewModelTests
{
    private static AdbDevice Device(string serial, string? model = null,
        DeviceConnectionState state = DeviceConnectionState.Online) =>
        new() { Serial = serial, Model = model, State = state };

    private static DevicesViewModel CreateViewModel() =>
        new(new FakeLocalizationService(), new FakeDeviceActionsService(), new FakeFastbootService(), new FakeDialogService());

    [Fact]
    public void Reconcile_adds_cards_and_assigns_one_based_index()
    {
        var vm = CreateViewModel();

        vm.Reconcile(new[] { Device("A", "Pixel"), Device("B", "Galaxy") }, adbAvailable: true);

        Assert.Equal(2, vm.Devices.Count);
        Assert.Equal(1, vm.Devices[0].Index);
        Assert.Equal(2, vm.Devices[1].Index);
        Assert.Equal("A", vm.Devices[0].Serial);
        Assert.Equal("B", vm.Devices[1].Serial);
        Assert.True(vm.HasDevices);
        Assert.False(vm.IsEmpty);
        Assert.Equal(2, vm.DeviceCount);
        Assert.True(vm.IsAdbAvailable);
    }

    [Fact]
    public void Reconcile_empty_with_adb_available_sets_isEmpty()
    {
        var vm = CreateViewModel();

        vm.Reconcile(new List<AdbDevice>(), adbAvailable: true);

        Assert.Empty(vm.Devices);
        Assert.False(vm.HasDevices);
        Assert.True(vm.IsEmpty);
        Assert.Equal(0, vm.DeviceCount);
    }

    [Fact]
    public void Reconcile_empty_without_adb_is_not_flagged_empty()
    {
        var vm = CreateViewModel();

        vm.Reconcile(new List<AdbDevice>(), adbAvailable: false);

        Assert.False(vm.HasDevices);
        Assert.False(vm.IsEmpty);
        Assert.False(vm.IsAdbAvailable);
    }

    [Fact]
    public void Reconcile_updates_existing_card_by_serial_without_replacing_instance()
    {
        var vm = CreateViewModel();
        vm.Reconcile(new[] { Device("A", "Pixel") }, adbAvailable: true);
        var original = vm.Devices.Single();

        vm.Reconcile(new[] { Device("A", "Pixel Pro", DeviceConnectionState.Offline) }, adbAvailable: true);

        var updated = vm.Devices.Single();
        Assert.Same(original, updated);
        Assert.Equal("Pixel Pro", updated.Model);
        Assert.Equal(DeviceBadgeKind.Offline, updated.StatusKind);
    }

    [Fact]
    public void Reconcile_removes_missing_devices()
    {
        var vm = CreateViewModel();
        vm.Reconcile(new[] { Device("A"), Device("B"), Device("C") }, adbAvailable: true);

        vm.Reconcile(new[] { Device("A"), Device("C") }, adbAvailable: true);

        Assert.Equal(2, vm.Devices.Count);
        Assert.DoesNotContain(vm.Devices, c => c.Serial == "B");
        Assert.Equal(1, vm.Devices[0].Index);
        Assert.Equal(2, vm.Devices[1].Index);
    }

    [Fact]
    public void Reconcile_keeps_selection_when_selected_device_remains()
    {
        var vm = CreateViewModel();
        vm.Reconcile(new[] { Device("A"), Device("B") }, adbAvailable: true);
        var selected = vm.Devices.Single(c => c.Serial == "A");
        vm.SelectedDevice = selected;

        vm.Reconcile(new[] { Device("A"), Device("B") }, adbAvailable: true);

        Assert.Same(selected, vm.SelectedDevice);
    }

    [Fact]
    public void Reconcile_clears_selection_when_selected_device_disappears()
    {
        var vm = CreateViewModel();
        vm.Reconcile(new[] { Device("A"), Device("B") }, adbAvailable: true);
        vm.SelectedDevice = vm.Devices.Single(c => c.Serial == "A");

        vm.Reconcile(new[] { Device("B") }, adbAvailable: true);

        Assert.Null(vm.SelectedDevice);
    }

    [Fact]
    public void SearchText_filters_by_serial_model_and_name()
    {
        var vm = CreateViewModel();
        vm.Reconcile(
            new[] { Device("SER-ONE", "Pixel"), Device("SER-TWO", "Galaxy"), Device("XYZ", "Nothing") },
            adbAvailable: true);

        vm.SearchText = "galaxy";
        Assert.Single(vm.Devices);
        Assert.Equal("SER-TWO", vm.Devices[0].Serial);

        vm.SearchText = "SER-";
        Assert.Equal(2, vm.Devices.Count);

        vm.SearchText = string.Empty;
        Assert.Equal(3, vm.Devices.Count);
    }

    [Fact]
    public void ActivateDevice_raises_event_and_sets_selection()
    {
        var vm = CreateViewModel();
        vm.Reconcile(new[] { Device("A") }, adbAvailable: true);
        var card = vm.Devices.Single();

        DeviceCardViewModel? activated = null;
        vm.DeviceActivated += (_, c) => activated = c;

        vm.ActivateDevice(card);

        Assert.Same(card, activated);
        Assert.Same(card, vm.SelectedDevice);
    }

    [Fact]
    public void ActivateDevice_ignores_fastboot_cards()
    {
        var vm = CreateViewModel();
        vm.Reconcile(new[] { Device("F", state: DeviceConnectionState.Bootloader) }, adbAvailable: true);
        var card = vm.Devices.Single();

        DeviceCardViewModel? activated = null;
        vm.DeviceActivated += (_, c) => activated = c;

        vm.ActivateDevice(card);

        Assert.Null(activated);
        Assert.Null(vm.SelectedDevice);
    }
}
