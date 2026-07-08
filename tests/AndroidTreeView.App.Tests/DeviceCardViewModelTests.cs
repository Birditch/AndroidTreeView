using System;
using AndroidTreeView.App.ViewModels;
using AndroidTreeView.Models.Battery;
using AndroidTreeView.Models.Devices;
using Xunit;

namespace AndroidTreeView.App.Tests;

/// <summary>
/// Behavior tests for <see cref="DeviceCardViewModel.UpdateFrom"/> mapping: cycle-count availability,
/// low-battery / unauthorized flags, charging text, and status badge kind. Uses the echoing
/// <see cref="FakeLocalizationService"/> so assertions pin the exact localization keys.
/// </summary>
public sealed class DeviceCardViewModelTests
{
    private static DeviceCardViewModel CreateCard(Action<DeviceCardViewModel>? onOpen = null) =>
        new(
            new FakeLocalizationService(),
            new FakeDeviceActionsService(),
            new FakeFastbootService(),
            new FakeDialogService(),
            onOpen ?? (_ => { }));

    private static AdbDevice Device(
        string serial = "SERIAL",
        string? model = "Model",
        DeviceConnectionState state = DeviceConnectionState.Online) =>
        new() { Serial = serial, Model = model, State = state };

    [Fact]
    public void UpdateFrom_null_cycle_count_yields_unavailable_text()
    {
        var card = CreateCard();
        var battery = new BatteryInfo { LevelPercent = 55, CycleCount = null };

        card.UpdateFrom(Device(), battery, root: null);

        Assert.False(card.CycleCountAvailable);
        Assert.Null(card.CycleCount);
        Assert.Equal("card.cycle.unavailable", card.CycleCountText);
    }

    [Fact]
    public void UpdateFrom_present_cycle_count_is_formatted()
    {
        var card = CreateCard();
        var battery = new BatteryInfo { LevelPercent = 80, CycleCount = 350 };

        card.UpdateFrom(Device(), battery, root: null);

        Assert.True(card.CycleCountAvailable);
        Assert.Equal(350, card.CycleCount);
        Assert.Equal("card.cycle: 350", card.CycleCountText);
    }

    [Fact]
    public void UpdateFrom_first_map_without_battery_reports_cycle_unavailable()
    {
        var card = CreateCard();

        card.UpdateFrom(Device(), battery: null, root: null);

        Assert.False(card.CycleCountAvailable);
        Assert.Equal("card.cycle.unavailable", card.CycleCountText);
    }

    [Fact]
    public void UpdateFrom_low_battery_when_discharging_below_threshold()
    {
        var card = CreateCard();
        var battery = new BatteryInfo
        {
            LevelPercent = 15,
            Status = BatteryStatus.Discharging,
            Plugged = BatteryPluggedType.None,
        };

        card.UpdateFrom(Device(), battery, root: null);

        Assert.True(card.IsLowBattery);
        Assert.False(card.IsCharging);
        Assert.Equal("common.no", card.ChargingText);
    }

    [Fact]
    public void UpdateFrom_not_low_battery_when_charging()
    {
        var card = CreateCard();
        var battery = new BatteryInfo
        {
            LevelPercent = 15,
            Status = BatteryStatus.Charging,
            Plugged = BatteryPluggedType.Ac,
        };

        card.UpdateFrom(Device(), battery, root: null);

        Assert.False(card.IsLowBattery);
        Assert.True(card.IsCharging);
        Assert.Equal("common.yes", card.ChargingText);
    }

    [Fact]
    public void UpdateFrom_online_maps_to_online_badge()
    {
        var card = CreateCard();

        card.UpdateFrom(Device(state: DeviceConnectionState.Online), battery: null, root: null);

        Assert.Equal(DeviceBadgeKind.Online, card.StatusKind);
        Assert.Equal("status.online", card.StatusText);
        Assert.True(card.IsOnline);
        Assert.False(card.IsUnauthorized);
    }

    [Fact]
    public void UpdateFrom_offline_maps_to_offline_badge()
    {
        var card = CreateCard();

        card.UpdateFrom(Device(state: DeviceConnectionState.Offline), battery: null, root: null);

        Assert.Equal(DeviceBadgeKind.Offline, card.StatusKind);
        Assert.Equal("status.offline", card.StatusText);
        Assert.False(card.IsOnline);
    }

    [Fact]
    public void UpdateFrom_unauthorized_maps_to_unauthorized_badge_and_flag()
    {
        var card = CreateCard();

        card.UpdateFrom(Device(state: DeviceConnectionState.Unauthorized), battery: null, root: null);

        Assert.Equal(DeviceBadgeKind.Unauthorized, card.StatusKind);
        Assert.Equal("status.unauthorized", card.StatusText);
        Assert.True(card.IsUnauthorized);
    }

    [Fact]
    public void UpdateFrom_rooted_device_sets_root_text()
    {
        var card = CreateCard();
        var root = new RootStatus { Level = RootDetectionLevel.Confirmed };

        card.UpdateFrom(Device(), battery: null, root: root);

        Assert.Equal(true, card.IsRooted);
        Assert.Equal("status.rooted", card.RootText);
    }

    [Fact]
    public void OpenCommand_opens_online_device_detail()
    {
        DeviceCardViewModel? opened = null;
        var card = CreateCard(c => opened = c);
        card.UpdateFrom(Device(state: DeviceConnectionState.Online), battery: null, root: null);

        Assert.True(card.CanOpenDetail);
        Assert.True(card.OpenCommand.CanExecute(null));

        card.OpenCommand.Execute(null);

        Assert.Same(card, opened);
    }

    [Fact]
    public void OpenCommand_is_disabled_for_fastboot_device()
    {
        var openCount = 0;
        var card = CreateCard(_ => openCount++);
        card.UpdateFrom(Device(state: DeviceConnectionState.Bootloader), battery: null, root: null);

        Assert.True(card.IsFastboot);
        Assert.False(card.CanOpenDetail);
        Assert.False(card.OpenCommand.CanExecute(null));
        Assert.Equal(0, openCount);
    }
}
