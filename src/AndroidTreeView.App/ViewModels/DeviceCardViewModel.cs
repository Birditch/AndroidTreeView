using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using AndroidTreeView.App.Services;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Models.Battery;
using AndroidTreeView.Models.Devices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTreeView.App.ViewModels;

/// <summary>
/// A single glass device card in the grid. Maps an <see cref="AdbDevice"/> (plus optional battery / root
/// enrichment) into localized, display-ready properties. Battery and root values are preserved across
/// refreshes when not re-supplied, and cycle count is never fabricated.
/// </summary>
public sealed partial class DeviceCardViewModel : ViewModelBase
{
    private const int LowBatteryThresholdPercent = 20;

    private readonly ILocalizationService _localization;
    private readonly IDeviceActionsService _actions;
    private readonly IFastbootService _fastboot;
    private readonly IDialogService _dialog;
    private readonly Action<DeviceCardViewModel> _onOpen;

    // True once getprop enrichment (ApplyOverview) has supplied the manufacturer/model/name, after which
    // a plain `adb devices -l` refresh must not overwrite them.
    private bool _identityEnriched;
    private bool _magiskStateKnown;

    [ObservableProperty]
    private string _serial = string.Empty;

    [ObservableProperty]
    private int _index;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _manufacturer = string.Empty;

    [ObservableProperty]
    private string _model = string.Empty;

    [ObservableProperty]
    private string _androidVersion = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFastboot))]
    [NotifyPropertyChangedFor(nameof(CanOpenDetail))]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveFrpCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenScreenCommand))]
    private DeviceConnectionState _state;

    [ObservableProperty]
    private DeviceBadgeKind _statusKind;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private int? _batteryPercent;

    [ObservableProperty]
    private string _batteryText = string.Empty;

    [ObservableProperty]
    private bool _isCharging;

    [ObservableProperty]
    private string _chargingText = string.Empty;

    [ObservableProperty]
    private double? _batteryTemperatureCelsius;

    [ObservableProperty]
    private string _temperatureText = string.Empty;

    [ObservableProperty]
    private int? _cycleCount;

    [ObservableProperty]
    private bool _cycleCountAvailable;

    [ObservableProperty]
    private string _cycleCountText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotRooted))]
    private bool? _isRooted;

    [ObservableProperty]
    private string _rootText = string.Empty;

    public bool IsNotRooted => IsRooted == false;

    [ObservableProperty]
    private string _oemUnlockSupportedText = string.Empty;

    [ObservableProperty]
    private string _oemUnlockAllowedText = string.Empty;

    [ObservableProperty]
    private string _bootloaderLockText = string.Empty;

    [ObservableProperty]
    private string _deviceStateText = string.Empty;

    [ObservableProperty]
    private string _verifiedBootText = string.Empty;

    [ObservableProperty]
    private DateTimeOffset? _lastRefresh;

    [ObservableProperty]
    private string _lastRefreshText = string.Empty;

    [ObservableProperty]
    private bool _isLowBattery;

    [ObservableProperty]
    private bool _isUnauthorized;

    [ObservableProperty]
    private bool _isOnline;

    [ObservableProperty]
    private bool _magiskInstalled;

    /// <summary>True when the device is in fastboot / bootloader mode (no OS: no mirror, CLI + power only).</summary>
    public bool IsFastboot => State == DeviceConnectionState.Bootloader;

    /// <summary>Whether the full ADB-backed detail page can be opened for this device.</summary>
    public bool CanOpenDetail => !IsFastboot;

    /// <summary>Curated fastboot getvar facts (label/value), shown in place of the normal battery/Android
    /// facts for a bootloader-mode device. The full dump is available via the CLI.</summary>
    public ObservableCollection<FastbootFact> FastbootFacts { get; } = new();

    /// <summary>Whether this card is checked for a batch operation.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Whether a screen-mirror session is currently open for this device (drives the "投屏中" tag
    /// and greys out the mirror action so a device can only be mirrored once at a time).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenScreenCommand))]
    private bool _isMirroring;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveFrpCommand))]
    private bool _frpRemoved;

    public DeviceCardViewModel(
        ILocalizationService localization,
        IDeviceActionsService actions,
        IFastbootService fastboot,
        IDialogService dialog,
        Action<DeviceCardViewModel> onOpen)
    {
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _fastboot = fastboot ?? throw new ArgumentNullException(nameof(fastboot));
        _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
        _onOpen = onOpen ?? throw new ArgumentNullException(nameof(onOpen));
    }

    /// <summary>Opens this device's detail page (delegates to the owning <see cref="DevicesViewModel"/>).</summary>
    [RelayCommand(CanExecute = nameof(CanOpenDetail))]
    private void Open()
    {
        if (IsFastboot)
        {
            return;
        }

        _onOpen(this);
    }

    /// <summary>Raised when the user chooses "screen mirror"; the shell opens the mirror window.</summary>
    public event EventHandler? ScreenRequested;

    /// <summary>Raised when the user chooses "CLI"; the shell opens a terminal scoped to this device.</summary>
    public event EventHandler? CliRequested;

    [RelayCommand(CanExecute = nameof(CanOpenScreen))]
    private void OpenScreen() => ScreenRequested?.Invoke(this, EventArgs.Empty);

    // One mirror per device; impossible in fastboot (no Android screen — use the CLI instead).
    private bool CanOpenScreen => !IsMirroring && !IsFastboot;

    /// <summary>Opens a native CLI terminal (adb for online devices, fastboot for bootloader) — available on every card.</summary>
    [RelayCommand]
    private void OpenCli() => CliRequested?.Invoke(this, EventArgs.Empty);

    // ---- Right-click actions (universal, non-destructive, no-root) --------------------------------

    // Power actions work in both modes: adb when the device is online, fastboot when it's in bootloader.
    [RelayCommand]
    private Task RebootSystem() => RunConfirmedAsync("menu.reboot", "confirm.reboot.msg",
        ct => IsFastboot ? _fastboot.RebootAsync(Serial, FastbootTarget.System, ct) : _actions.RebootAsync(Serial, RebootTarget.System, ct));

    [RelayCommand]
    private Task RebootRecovery() => RunConfirmedAsync("menu.recovery", "confirm.recovery.msg",
        ct => IsFastboot ? _fastboot.RebootAsync(Serial, FastbootTarget.Recovery, ct) : _actions.RebootAsync(Serial, RebootTarget.Recovery, ct));

    [RelayCommand]
    private Task EnterFastboot() => RunConfirmedAsync("menu.fastboot", "confirm.fastboot.msg",
        ct => IsFastboot ? _fastboot.RebootAsync(Serial, FastbootTarget.Bootloader, ct) : _actions.RebootAsync(Serial, RebootTarget.Bootloader, ct));

    [RelayCommand]
    private Task PowerOff() => RunConfirmedAsync("menu.poweroff", "confirm.poweroff.msg",
        ct => IsFastboot ? _fastboot.PowerOffAsync(Serial, ct) : _actions.PowerOffAsync(Serial, ct));

    [RelayCommand(CanExecute = nameof(CanRemoveFrp))]
    private async Task RemoveFrp()
    {
        await RunConfirmedAsync("menu.frp", "confirm.frp.msg", ct => _actions.RemoveFrpAsync(Serial, ct)).ConfigureAwait(true);
        await RefreshActionStateAsync().ConfigureAwait(true);
    }

    private bool CanRemoveFrp => !FrpRemoved && !IsFastboot;

    /// <summary>Refreshes the toggle-state flags used to grey out already-applied ADB actions.</summary>
    public async Task RefreshActionStateAsync(CancellationToken ct = default)
    {
        try
        {
            FrpRemoved = await _actions.IsFrpRemovedAsync(Serial, ct).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // Leave the flags unchanged if the state query fails.
        }
    }

    // Shows a centered confirmation (with the consequence text) before running the action; cancelling is
    // a silent no-op. Every right-click ADB action goes through this — only 投屏 (mirror) skips it.
    private async Task RunConfirmedAsync(string labelKey, string confirmMsgKey, Func<CancellationToken, Task> action)
    {
        var confirmed = await _dialog.ConfirmAsync(
            _localization.Get("confirm.title"),
            _localization.Get(confirmMsgKey),
            _localization.Get("common.confirm"),
            _localization.Get("common.cancel")).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        await RunActionAsync(labelKey, action).ConfigureAwait(true);
    }

    // One-shot menu actions: run the command and show a color-coded toast so the result is always visible;
    // device-level failures (e.g. permission) surface as a red toast rather than crashing the UI.
    private async Task RunActionAsync(string labelKey, Func<CancellationToken, Task> action)
    {
        var label = _localization.Get(labelKey);
        try
        {
            await action(CancellationToken.None).ConfigureAwait(true);
            Notifier.Notify(_localization.Format("action.done", label), NotifierLevel.Success);
        }
        catch (Exception)
        {
            Notifier.Notify(_localization.Format("action.failed", label), NotifierLevel.Error);
        }
    }

    /// <summary>
    /// Refreshes the card from the latest device-list snapshot (<c>adb devices -l</c>). Rich fields
    /// (manufacturer / marketing model / Android version / battery / root) are filled in separately by
    /// <see cref="ApplyOverview"/> / <see cref="ApplyBattery"/> / <see cref="ApplyRoot"/> and are NOT
    /// overwritten here, so a 1 Hz list refresh never wipes enriched data.
    /// </summary>
    public void UpdateFrom(AdbDevice device, BatteryInfo? battery, RootStatus? root)
    {
        ArgumentNullException.ThrowIfNull(device);

        var firstMap = LastRefresh is null;

        Serial = device.Serial;
        State = device.State;
        IsOnline = device.IsOnline;
        IsUnauthorized = device.State == DeviceConnectionState.Unauthorized;
        StatusKind = MapKind(device.State);
        StatusText = MapStatusText(device);

        // Follow the -l descriptor on every list refresh UNTIL getprop enrichment (ApplyOverview) supplies
        // richer identity, after which the list refresh must not clobber it.
        if (!_identityEnriched)
        {
            Model = device.Model ?? string.Empty;
            DisplayName = device.DisplayName;
        }

        if (battery is not null)
        {
            ApplyBattery(battery);
        }
        else if (firstMap)
        {
            BatteryText = _localization.Get("common.unavailable");
            ChargingText = _localization.Get("common.unavailable");
            TemperatureText = _localization.Get("common.unavailable");
            CycleCountAvailable = false;
            CycleCountText = _localization.Get("card.cycle.unavailable");
        }

        if (root is not null)
        {
            ApplyRoot(root);
        }
        else if (firstMap)
        {
            RootText = _localization.Get("common.unavailable");
        }

        TouchRefresh();
    }

    /// <summary>Applies rich getprop identity (manufacturer, marketing model, Android version).</summary>
    public void ApplyOverview(DeviceOverview overview)
    {
        if (overview is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(overview.Manufacturer))
        {
            Manufacturer = overview.Manufacturer!;
        }

        if (!string.IsNullOrWhiteSpace(overview.Model))
        {
            Model = overview.Model!;
        }

        if (!string.IsNullOrWhiteSpace(overview.AndroidVersion))
        {
            AndroidVersion = overview.AndroidVersion!;
        }

        OemUnlockSupportedText = UnlockStateFormatter.FormatNullableBool(overview.OemUnlockSupported, _localization);
        OemUnlockAllowedText = UnlockStateFormatter.FormatOemUnlockAllowed(
            overview.OemUnlockAllowed,
            overview.BootloaderLockState,
            overview.DeviceState,
            overview.VerifiedBootState,
            _localization);
        BootloaderLockText = UnlockStateFormatter.FormatBootloaderLock(
            overview.BootloaderLockState,
            overview.DeviceState,
            overview.VerifiedBootState,
            _localization);
        DeviceStateText = UnlockStateFormatter.FormatState(overview.DeviceState, _localization);
        VerifiedBootText = UnlockStateFormatter.FormatState(overview.VerifiedBootState, _localization);
        NotifyMagiskStateChange(overview.MagiskInstalled);
        MagiskInstalled = overview.MagiskInstalled;
        _magiskStateKnown = true;

        var name = !string.IsNullOrWhiteSpace(overview.DisplayName) ? overview.DisplayName : overview.Model;
        if (!string.IsNullOrWhiteSpace(name))
        {
            DisplayName = name!;
        }

        _identityEnriched = true;
        TouchRefresh();
    }

    /// <summary>Applies the latest battery snapshot; cycle count is shown only when the device reports it.</summary>
    public void ApplyBattery(BatteryInfo battery)
    {
        if (battery is null)
        {
            return;
        }

        BatteryPercent = battery.LevelPercent;
        BatteryText = battery.LevelPercent is { } lvl
            ? string.Format(_localization.CurrentCulture, "{0}%", lvl)
            : _localization.Get("common.unavailable");
        IsCharging = battery.IsCharging;
        ChargingText = battery.IsCharging ? _localization.Get("common.yes") : _localization.Get("common.no");
        BatteryTemperatureCelsius = battery.TemperatureCelsius;
        TemperatureText = battery.TemperatureCelsius is { } celsius
            ? string.Format(_localization.CurrentCulture, "{0:0.0} ℃", celsius)
            : _localization.Get("common.unavailable");
        CycleCount = battery.CycleCount;
        CycleCountAvailable = battery.CycleCount.HasValue;
        CycleCountText = battery.CycleCount is { } cycles
            ? string.Format(_localization.CurrentCulture, "{0}: {1}", _localization.Get("card.cycle"), cycles)
            : _localization.Get("card.cycle.unavailable");
        IsLowBattery = battery.LevelPercent is { } level && level <= LowBatteryThresholdPercent && !battery.IsCharging;

        TouchRefresh();
    }

    /// <summary>Applies detected root status (Yes/No once probed).</summary>
    public void ApplyRoot(RootStatus root)
    {
        if (root is null)
        {
            return;
        }

        IsRooted = root.IsRooted;
        RootText = root.IsRooted ? _localization.Get("status.rooted") : _localization.Get("root.notdetected");

        TouchRefresh();
    }

    /// <summary>Applies fastboot getvar facts (product / bootloader / baseband / unlock / slot), shown in
    /// place of the normal battery + Android facts for a bootloader-mode device.</summary>
    public void ApplyFastbootInfo(IReadOnlyDictionary<string, string> vars)
    {
        if (vars is null || vars.Count == 0)
        {
            return;
        }

        FastbootFacts.Clear();
        AddFact("fastboot.product", Concat(vars, "product"));
        AddFact("fastboot.board", Concat(vars, "board"));
        AddFact("fastboot.cpu", Concat(vars, "cpu"));
        AddFact("fastboot.ram", Concat(vars, "ram"));
        AddFact("fastboot.storage", First(vars, "ufs", "storage-type"));
        AddFact("fastboot.bootloader", Concat(vars, "version-bootloader"));
        AddFact("fastboot.baseband", Concat(vars, "version-baseband"));
        AddFact("fastboot.unlocked", UnlockState(vars));
        AddFact("fastboot.secure", Concat(vars, "secure"));
        AddFact("fastboot.frp", Concat(vars, "frp-state"));
        AddFact("fastboot.slot", Concat(vars, "current-slot"));
        AddFact("fastboot.carrier", Concat(vars, "ro.carrier"));
        AddFact("fastboot.imei", Concat(vars, "imei"));
        AddFact("fastboot.sku", Concat(vars, "sku"));
        AddFact("fastboot.date", Concat(vars, "date"));
        AddFact("fastboot.battid", Concat(vars, "battid"));
        AddFact("card.serial", Serial);
        TouchRefresh();
    }

    private void AddFact(string labelKey, string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && value != "—")
        {
            FastbootFacts.Add(new FastbootFact(_localization.Get(labelKey), value));
        }
    }

    // getvar splits long values across key[0], key[1], … — join them back into one string.
    private static string Concat(IReadOnlyDictionary<string, string> vars, string key)
    {
        if (vars.TryGetValue(key, out var whole) && !string.IsNullOrWhiteSpace(whole))
        {
            return whole;
        }

        var builder = new StringBuilder();
        for (var i = 0; vars.TryGetValue($"{key}[{i}]", out var part); i++)
        {
            builder.Append(part);
        }

        var joined = builder.ToString();
        return string.IsNullOrWhiteSpace(joined) ? "—" : joined;
    }

    private static string First(IReadOnlyDictionary<string, string> vars, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = Concat(vars, key);
            if (value != "—")
            {
                return value;
            }
        }

        return "—";
    }

    // Prefer the standard "unlocked" var; fall back to OEM "securestate" (e.g. flashing_unlocked/locked).
    private static string UnlockState(IReadOnlyDictionary<string, string> vars)
    {
        if (vars.TryGetValue("unlocked", out var unlocked) && !string.IsNullOrWhiteSpace(unlocked))
        {
            return unlocked;
        }

        return vars.TryGetValue("securestate", out var secureState) && !string.IsNullOrWhiteSpace(secureState)
            ? secureState
            : "—";
    }

    private void NotifyMagiskStateChange(bool installed)
    {
        if (!_magiskStateKnown || MagiskInstalled == installed)
        {
            return;
        }

        var key = installed ? "software.installed" : "software.uninstalled";
        var level = installed ? NotifierLevel.Success : NotifierLevel.Warning;
        Notifier.Notify(_localization.Format(key, "Magisk"), level);
    }

    private void TouchRefresh()
    {
        LastRefresh = DateTimeOffset.Now;
        LastRefreshText = string.Format(
            _localization.CurrentCulture,
            "{0}: {1}",
            _localization.Get("card.lastrefresh"),
            LastRefresh.Value.LocalDateTime.ToString("HH:mm:ss", _localization.CurrentCulture));
    }

    private static DeviceBadgeKind MapKind(DeviceConnectionState state) => state switch
    {
        DeviceConnectionState.Online => DeviceBadgeKind.Online,
        DeviceConnectionState.Unauthorized => DeviceBadgeKind.Unauthorized,
        DeviceConnectionState.Offline or DeviceConnectionState.Disconnected => DeviceBadgeKind.Offline,
        _ => DeviceBadgeKind.Other,
    };

    private string MapStatusText(AdbDevice device) => device.State switch
    {
        DeviceConnectionState.Online => _localization.Get("status.online"),
        DeviceConnectionState.Unauthorized => _localization.Get("status.unauthorized"),
        DeviceConnectionState.Bootloader => _localization.Get("status.fastboot"),
        DeviceConnectionState.Recovery => _localization.Get("status.recovery"),
        DeviceConnectionState.Offline or DeviceConnectionState.Disconnected => _localization.Get("status.offline"),
        _ => string.IsNullOrWhiteSpace(device.RawState) ? _localization.Get("status.offline") : device.RawState!,
    };
}

/// <summary>One fastboot getvar fact (label + value) shown on a bootloader-mode device card.</summary>
public sealed record FastbootFact(string Label, string Value);
