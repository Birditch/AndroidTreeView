using System.Collections.Generic;
using System.IO;
using System.Linq;
using AndroidTreeView.App.ViewModels;
using AndroidTreeView.Core.Services;
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

    private static DevicesViewModel CreateViewModel(
        FakeScreenCaptureService? screenCapture = null,
        FakeFilePickerService? filePicker = null) =>
        new(
            new FakeLocalizationService(),
            new FakeDeviceActionsService(),
            new DeviceFileTransferService(screenCapture ?? new FakeScreenCaptureService()),
            filePicker ?? new FakeFilePickerService(),
            new FakeFastbootService(),
            new FakeDialogService());

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

    [Fact]
    public async Task HandleDroppedFiles_installs_apks_and_transfers_other_files_to_download()
    {
        var installer = new FakeScreenCaptureService();
        var vm = CreateViewModel(installer);
        vm.Reconcile(new[] { Device("A"), Device("B") }, adbAvailable: true);
        foreach (var device in vm.Devices)
        {
            device.IsSelected = true;
        }

        var apk = TempFile(".apk");
        var text = TempFile(".txt");
        try
        {
            await vm.HandleDroppedFilesAsync([apk, text]);

            Assert.Equal(new[] { "A", "B" }, installer.InstalledSerials.OrderBy(s => s));
            Assert.Equal(2, installer.PushedFiles.Count);
            Assert.All(installer.PushedFiles, push =>
            {
                Assert.Equal(text, push.Path);
                Assert.Equal("/sdcard/Download/", push.RemoteDirectory);
            });
        }
        finally
        {
            File.Delete(apk);
            File.Delete(text);
        }
    }

    [Fact]
    public async Task BatchPickTransferFiles_installs_apks_and_transfers_files_to_download()
    {
        var installer = new FakeScreenCaptureService();
        var picker = new FakeFilePickerService();
        var vm = CreateViewModel(installer, picker);
        vm.Reconcile(new[] { Device("A"), Device("B", state: DeviceConnectionState.Offline) }, adbAvailable: true);
        foreach (var device in vm.Devices)
        {
            device.IsSelected = true;
        }

        var apk1 = TempFile(".apk");
        var apk2 = TempFile(".apk");
        var file1 = TempFile(".txt");
        var file2 = TempFile(".bin");
        picker.TransferFiles = [apk1, file1, apk2, file2];
        try
        {
            await vm.BatchPickTransferFilesCommand.ExecuteAsync(null);

            Assert.Equal(new[] { "A", "A" }, installer.InstalledSerials);
            Assert.Equal(new[] { file1, file2 }, installer.PushedFiles.Select(push => push.Path));
            Assert.All(installer.PushedFiles, push => Assert.Equal("/sdcard/Download/", push.RemoteDirectory));
            Assert.True(vm.IsTransferProgressVisible);
            Assert.False(vm.IsTransferInProgress);
            Assert.Equal(100, vm.TransferProgressValue);
            Assert.Equal("100%", vm.TransferProgressPercentText);
            Assert.Equal("batch.progress.bytes", vm.TransferProgressBytesText);
            Assert.Equal("batch.progress.count", vm.TransferProgressText);
            Assert.Equal("batch.files.result", vm.TransferProgressDetail);
        }
        finally
        {
            File.Delete(apk1);
            File.Delete(apk2);
            File.Delete(file1);
            File.Delete(file2);
        }
    }

    private static string TempFile(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), "AndroidTreeView-" + Path.GetRandomFileName() + extension);
        File.WriteAllText(path, "payload");
        return path;
    }
}
