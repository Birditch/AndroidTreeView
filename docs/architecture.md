# AndroidTreeView — Architecture &amp; Implementation Contract

> This document is the **authoritative contract** for the codebase. Every type name,
> namespace, enum member, interface signature, and DI registration below is binding.
> Implementation agents MUST implement exactly these shapes so the solution compiles as a
> coherent whole. When in doubt, read the actual `.cs` files produced by the foundation
> layer — they are the source of truth in code.

## 1. Overview

AndroidTreeView is a cross-platform Avalonia desktop app (primary target Windows) that
discovers Android devices via ADB and displays rich per-device information. Pure GUI, no
backend. Strict MVVM with CommunityToolkit.Mvvm and Microsoft.Extensions dependency
injection / hosting / logging.

## 2. Toolchain &amp; package versions (pin exactly)

- Target framework: **net10.0** (WPF-free; Avalonia desktop).
- **Avalonia 11.3.18** (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`,
  `Avalonia.Fonts.Inter`, `Avalonia.Controls.DataGrid`, and `Avalonia.Diagnostics` in Debug).
- **CommunityToolkit.Mvvm 8.4.2**
- **Microsoft.Extensions.Hosting 10.0.9**, `Microsoft.Extensions.DependencyInjection 10.0.9`,
  `Microsoft.Extensions.Logging 10.0.9`, `Microsoft.Extensions.Logging.Console 10.0.9`.
- Tests: **Microsoft.NET.Test.Sdk 17.12.0**, **xunit 2.9.2**, **xunit.runner.visualstudio 2.8.2**,
  **coverlet.collector 6.0.2**.
- A repo-root `nuget.config` MUST add `https://api.nuget.org/v3/index.json` (the machine has
  only the offline VS source configured).

## 3. Solution layout

```
AndroidTreeView.sln
nuget.config
Directory.Build.props
.editorconfig  .gitignore  README.md  LICENSE  CONTRIBUTING.md
src/
  AndroidTreeView.Models/        (no project deps)
  AndroidTreeView.Core/          -> Models
  AndroidTreeView.Adb/           -> Models, Core
  AndroidTreeView.Infrastructure/-> Models, Core
  AndroidTreeView.App/           -> Models, Core, Adb, Infrastructure  (Avalonia exe)
tests/
  AndroidTreeView.Adb.Tests/     -> Adb, Models, Core
  AndroidTreeView.Core.Tests/    -> Core, Models
  AndroidTreeView.App.Tests/     -> App, Core, Models
docs/  .github/workflows/ci.yml
```

`Directory.Build.props` sets: `Nullable=enable`, `ImplicitUsings=enable`,
`LangVersion=latest`, `TargetFramework=net10.0`, `EnableNETAnalyzers=true`,
`AnalysisLevel=latest`, `GenerateDocumentationFile=false`, `TreatWarningsAsErrors=false`
(warnings visible but not fatal — keeps first build green). The App csproj sets
`<OutputType>WinExe</OutputType>` only via `<OutputType>Exe</OutputType>` plus
`<BuiltInComInteropSupport>` not needed; use `OutputType=Exe`. App also sets
`<ApplicationManifest>` not required. App references `Avalonia.Diagnostics` under a Debug
condition. Test projects set `IsPackable=false` and `<Nullable>enable</Nullable>`.

## 4. Namespaces

- `AndroidTreeView.Models` (+ `.Devices .Battery .Hardware .System .Storage .Network .Logs`)
- `AndroidTreeView.Core.Interfaces`, `.Options`, `.Exceptions`, `.Services`, `.Diagnostics`
- `AndroidTreeView.Adb.Services`, `.Commands`, `.Parsers`, `.Internal`
- `AndroidTreeView.Infrastructure.Settings`, `.Logging`, `.Persistence`
- `AndroidTreeView.App` (+ `.ViewModels .Views .Controls .Converters .Services`)

## 5. Models (project `AndroidTreeView.Models`)

Use immutable-ish classes with `{ get; init; }` (records allowed). Nullable where data may be
absent. Collections typed `IReadOnlyList<T>` / `IReadOnlyDictionary<string,string>` and never null
(default to empty).

### 5.1 `.Devices`
```csharp
public enum DeviceConnectionState { Unknown, Online, Offline, Unauthorized, Authorizing,
    Bootloader, Recovery, Sideload, NoPermission, Connecting, Disconnected }

// Lightweight entry from `adb devices -l`
public sealed class AdbDevice {
    public required string Serial { get; init; }
    public DeviceConnectionState State { get; init; }
    public string? RawState { get; init; }         // original token e.g. "device","unauthorized"
    public string? Model { get; init; }            // model:... descriptor
    public string? Product { get; init; }
    public string? Device { get; init; }           // device:... descriptor (codename)
    public string? TransportId { get; init; }
    public string? UsbPath { get; init; }
    public IReadOnlyDictionary<string,string> Descriptors { get; init; }
        = new Dictionary<string,string>();
    public bool IsOnline => State == DeviceConnectionState.Online;
    public bool IsAuthorized => State != DeviceConnectionState.Unauthorized;
    public string DisplayName => string.IsNullOrWhiteSpace(Model) ? Serial : Model!;
}

public sealed class DeviceOverview {
    public string? DisplayName { get; init; }
    public string? Manufacturer { get; init; }
    public string? Brand { get; init; }
    public string? Model { get; init; }
    public string? Product { get; init; }
    public string? Codename { get; init; }         // ro.product.device
    public string? SerialNumber { get; init; }
    public string? AndroidVersion { get; init; }   // ro.build.version.release
    public int? ApiLevel { get; init; }            // ro.build.version.sdk
    public string? BuildNumber { get; init; }      // ro.build.display.id
    public string? BuildFingerprint { get; init; }
    public string? SecurityPatch { get; init; }
    public string? BuildTags { get; init; }
    public string? BuildType { get; init; }
}

public enum RootDetectionLevel { Unknown, NotRooted, Likely, Confirmed }
public sealed class RootStatus {
    public bool SuBinaryExists { get; init; }
    public bool SuGrantsRoot { get; init; }        // `su -c id` returned uid=0
    public string? CurrentUserId { get; init; }    // output of `id`
    public string? RootUserId { get; init; }       // output of `su -c id` if any
    public string? MagiskVersion { get; init; }
    public string? SelinuxMode { get; init; }      // Enforcing/Permissive
    public RootDetectionLevel Level { get; init; }
    public bool IsRooted => Level is RootDetectionLevel.Confirmed or RootDetectionLevel.Likely;
}
```

### 5.2 `.Battery`
```csharp
public enum BatteryStatus { Unknown, Charging, Discharging, NotCharging, Full }
public enum BatteryHealth { Unknown, Good, Overheat, Dead, OverVoltage, UnspecifiedFailure, Cold }
public enum BatteryPluggedType { None, Ac, Usb, Wireless, Dock }
public sealed class BatteryInfo {
    public int? LevelPercent { get; init; }
    public int? RawLevel { get; init; }
    public int? Scale { get; init; }
    public BatteryStatus Status { get; init; }
    public BatteryPluggedType Plugged { get; init; }
    public BatteryHealth Health { get; init; }
    public double? TemperatureCelsius { get; init; }   // dumpsys value / 10
    public int? VoltageMillivolts { get; init; }
    public string? Technology { get; init; }
    public bool? Present { get; init; }
    public bool IsCharging => Status == BatteryStatus.Charging || Plugged != BatteryPluggedType.None;
}
```

### 5.3 `.Hardware`
```csharp
public sealed class CpuInfo {
    public string? Model { get; init; }
    public string? Hardware { get; init; }
    public string? Architecture { get; init; }
    public int? CoreCount { get; init; }
    public IReadOnlyList<string> Features { get; init; } = Array.Empty<string>();
}
public sealed class MemoryInfo {
    public long? TotalBytes { get; init; }
    public long? AvailableBytes { get; init; }
    public long? FreeBytes { get; init; }
}
public sealed class ScreenInfo {
    public string? Resolution { get; init; }   // "1080x2400"
    public int? DensityDpi { get; init; }
}
public sealed class HardwareInfo {
    public string? CpuModel { get; init; }
    public string? CpuArchitecture { get; init; }
    public int? CpuCoreCount { get; init; }
    public IReadOnlyList<string> AbiList { get; init; } = Array.Empty<string>();
    public long? RamTotalBytes { get; init; }
    public long? RamAvailableBytes { get; init; }
    public string? ScreenResolution { get; init; }
    public int? ScreenDensityDpi { get; init; }
    public string? Gpu { get; init; }
    public string? HardwarePlatform { get; init; }   // ro.board.platform
    public string? BoardName { get; init; }          // ro.product.board
}
```

### 5.4 `.System`
```csharp
public sealed class SystemInfo {
    public string? KernelVersion { get; init; }
    public string? SelinuxStatus { get; init; }
    public TimeSpan? Uptime { get; init; }
    public string? Bootloader { get; init; }
    public string? VerifiedBootState { get; init; }
    public string? BuildTags { get; init; }
    public string? BuildType { get; init; }
    public string? Locale { get; init; }
    public string? Timezone { get; init; }
    public int? SdkVersion { get; init; }
}
```

### 5.5 `.Storage`
```csharp
public sealed class StoragePartition {
    public required string Name { get; init; }        // filesystem or friendly label
    public string? MountPoint { get; init; }
    public long? TotalBytes { get; init; }
    public long? UsedBytes { get; init; }
    public long? AvailableBytes { get; init; }
    public double? UsePercent { get; init; }
}
public sealed class StorageInfo {
    public IReadOnlyList<StoragePartition> Partitions { get; init; } = Array.Empty<StoragePartition>();
}
```

### 5.6 `.Network`
```csharp
public sealed class NetworkInterfaceInfo {
    public required string Name { get; init; }
    public string? IpAddress { get; init; }
    public string? MacAddress { get; init; }
    public string? State { get; init; }
}
public sealed class NetworkInfo {
    public string? WifiIpAddress { get; init; }
    public string? WifiMacAddress { get; init; }
    public string? MobileNetworkState { get; init; }
    public IReadOnlyList<NetworkInterfaceInfo> Interfaces { get; init; } = Array.Empty<NetworkInterfaceInfo>();
    public IReadOnlyList<string> DnsServers { get; init; } = Array.Empty<string>();
}
```

### 5.7 `.Logs`
```csharp
public enum LogPriority { Unknown, Verbose, Debug, Info, Warn, Error, Fatal, Silent }
public sealed class LogcatEntry {
    public string? Timestamp { get; init; }
    public LogPriority Priority { get; init; }
    public string? Tag { get; init; }
    public int? Pid { get; init; }
    public int? Tid { get; init; }
    public required string Message { get; init; }
}
```

### 5.8 root `AndroidTreeView.Models`
```csharp
public sealed class DeviceProperties {
    public IReadOnlyDictionary<string,string> Values { get; init; } = new Dictionary<string,string>();
    public int Count => Values.Count;
}
```

## 6. Core (project `AndroidTreeView.Core`)

### 6.1 `.Options`
```csharp
public enum ThemeMode { System, Light, Dark }
public enum StartupBehavior { Normal, StartMinimized, RememberWindow }

public sealed class AppSettings {
    public string? AdbPath { get; set; }
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public bool AutoRefreshEnabled { get; set; } = true;
    public int DeviceRefreshIntervalSeconds { get; set; } = 3;
    public int BatteryRefreshIntervalSeconds { get; set; } = 10;
    public int LogcatMaxLines { get; set; } = 5000;
    public StartupBehavior Startup { get; set; } = StartupBehavior.Normal;
    public bool RememberLastSelectedDevice { get; set; } = true;
    public string? LastSelectedSerial { get; set; }
    public AppSettings Clone();   // deep copy
}

public sealed class AdbOptions {          // static defaults / knobs
    public TimeSpan DefaultCommandTimeout { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan DeviceListTimeout { get; set; } = TimeSpan.FromSeconds(8);
}

public sealed class LogcatOptions {
    public LogPriority MinPriority { get; init; } = LogPriority.Verbose;
    public bool ClearBeforeStart { get; init; } = false;
    public string? TagFilter { get; init; }
}
```

### 6.2 `.Exceptions` (all derive from `AdbException : Exception`)
`AdbException` (base, with ctors), `AdbNotFoundException`, `AdbTimeoutException`,
`AdbCommandFailedException` (props `int ExitCode`, `string? StandardError`),
`DeviceUnauthorizedException` (prop `string Serial`), `DeviceOfflineException` (prop `string Serial`),
`DeviceNotFoundException` (prop `string Serial`), `OutputParseException`.

### 6.3 ADB command primitives (`.Interfaces` / `.Services`)
```csharp
public sealed class AdbCommandRequest {
    public string? Serial { get; init; }              // null => global adb command
    public required IReadOnlyList<string> Arguments { get; init; }
    public TimeSpan? Timeout { get; init; }
    public bool RunInShell { get; init; }             // true => "-s serial shell <args...>"
    public static AdbCommandRequest Shell(string serial, params string[] args);
    public static AdbCommandRequest Global(params string[] args);
}
public sealed class AdbCommandResult {
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = "";
    public string StandardError { get; init; } = "";
    public bool TimedOut { get; init; }
    public TimeSpan Duration { get; init; }
    public bool IsSuccess => ExitCode == 0 && !TimedOut;
}
public sealed class AdbLocation {
    public required string ExecutablePath { get; init; }
    public string? Version { get; init; }
    public AdbLocationSource Source { get; init; }
}
public enum AdbLocationSource { Configured, EnvironmentPath, CommonSdkLocation, NotFound }
```

### 6.4 `.Interfaces`
```csharp
public interface IAdbLocator {
    Task<AdbLocation?> LocateAsync(string? configuredPath, CancellationToken ct = default);
}
public interface IAdbCommandExecutor {
    Task<AdbCommandResult> ExecuteAsync(AdbCommandRequest request, CancellationToken ct = default);
    IAsyncEnumerable<string> StreamAsync(AdbCommandRequest request, CancellationToken ct = default);
}
public interface IDeviceService {
    Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(CancellationToken ct = default);
    Task<DeviceOverview> GetOverviewAsync(string serial, CancellationToken ct = default);
    Task<HardwareInfo> GetHardwareAsync(string serial, CancellationToken ct = default);
    Task<BatteryInfo> GetBatteryAsync(string serial, CancellationToken ct = default);
    Task<SystemInfo> GetSystemInfoAsync(string serial, CancellationToken ct = default);
    Task<StorageInfo> GetStorageAsync(string serial, CancellationToken ct = default);
    Task<NetworkInfo> GetNetworkAsync(string serial, CancellationToken ct = default);
    Task<RootStatus> GetRootStatusAsync(string serial, CancellationToken ct = default);
    Task<DeviceProperties> GetPropertiesAsync(string serial, CancellationToken ct = default);
}
public interface ILogcatService {
    IAsyncEnumerable<LogcatEntry> StreamAsync(string serial, LogcatOptions options, CancellationToken ct = default);
    Task ClearAsync(string serial, CancellationToken ct = default);
}
public sealed class DeviceListChangedEventArgs : EventArgs {
    public required IReadOnlyList<AdbDevice> Devices { get; init; }
    public bool AdbAvailable { get; init; }
    public string? Error { get; init; }
}
public interface IDeviceMonitor {
    event EventHandler<DeviceListChangedEventArgs>? DevicesChanged;
    bool IsRunning { get; }
    void Start();
    Task StopAsync();
    Task<DeviceListChangedEventArgs> RefreshNowAsync(CancellationToken ct = default);
    void UpdateInterval(TimeSpan interval);
}
public interface ISettingsService {
    AppSettings Current { get; }
    event EventHandler<AppSettings>? SettingsChanged;
    Task<AppSettings> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
}
public interface IAdbEnvironment {   // shared adb availability state, updated by locator
    bool IsAvailable { get; }
    AdbLocation? Location { get; }
    string ExecutablePath { get; }   // throws AdbNotFoundException if unavailable
    void Set(AdbLocation? location);
    event EventHandler? Changed;
}
```

### 6.5 `.Services`
- `DeviceMonitor : IDeviceMonitor` — owns a `PeriodicTimer` loop on a background task; calls
  `IDeviceService.ListDevicesAsync`; raises `DevicesChanged` (marshalling to UI is the VM's job).
  Catches `AdbNotFoundException` -> raises event with `AdbAvailable=false`. No polling when stopped.
- Static `AdbCommands` constants live in `AndroidTreeView.Adb.Commands` (see §7). Property-key
  constants (`ro.product.manufacturer`, etc.) live in `AndroidTreeView.Adb.Commands.PropKeys`.

## 7. Adb (project `AndroidTreeView.Adb`)

### 7.1 `.Commands`
- `static class PropKeys` — const strings for every getprop key used
  (Manufacturer, Brand, Model, ProductName, Device, SerialNo, AndroidVersion, Sdk, BuildDisplayId,
  Fingerprint, SecurityPatch, BuildTags, BuildType, BoardPlatform, ProductBoard, AbiList,
  Locale/`persist.sys.locale`+`ro.product.locale`, Timezone `persist.sys.timezone`,
  Bootloader `ro.bootloader`, VerifiedBoot `ro.boot.verifiedbootstate`, MagiskVersion candidates).
- `static class AdbArgs` — argument arrays / builders for each command in §7.4.

### 7.2 `.Internal`
- `ProcessRunner` — wraps `System.Diagnostics.Process`; async stdout/stderr capture via
  `RedirectStandardOutput`; kills whole process tree on cancel/timeout (`Process.Kill(entireProcessTree:true)`);
  returns exit code + captured text. Also a streaming variant yielding stdout lines via `Channel<string>`
  for `IAsyncEnumerable`. Never blocks the calling thread; no `.Result`/`.Wait()`.

### 7.3 `.Services`
- `AdbLocator : IAdbLocator` — order: configured path -> `adb`/`adb.exe` on PATH -> common SDK
  locations (`%LOCALAPPDATA%/Android/Sdk/platform-tools`, `~/Library/Android/sdk/platform-tools`,
  `~/Android/Sdk/platform-tools`, `/usr/local/bin`, `/opt/android-sdk/platform-tools`, `ANDROID_HOME`,
  `ANDROID_SDK_ROOT`). Validates by running `adb version`. Returns `AdbLocation` or null.
- `AdbEnvironment : IAdbEnvironment` — singleton holding current `AdbLocation`.
- `AdbCommandExecutor : IAdbCommandExecutor` — builds argv (`-s <serial>` + `shell` when
  `RunInShell`), uses `IAdbEnvironment.ExecutablePath`, applies timeout (default from `AdbOptions`),
  maps failures to typed exceptions where clearly detectable (unauthorized/offline strings in stderr),
  otherwise returns non-success result. Streaming for logcat.
- `AdbDeviceService : IDeviceService` — orchestrates commands + parsers/builders. Each Get* runs the
  minimal command set, is resilient (a failed sub-command yields nulls, never throws for normal device
  errors), and reuses the cached getprop dictionary where possible (Overview/Hardware/System all read
  props — fetch once per call).
- `LogcatService : ILogcatService` — `StreamAsync` runs `logcat -v threadtime` (+ `*:<P>` priority),
  parses each line to `LogcatEntry`, honors ct; `ClearAsync` runs `logcat -c`.

### 7.4 Command set (exact adb invocations)
- `adb devices -l`
- `adb -s S shell getprop`
- `adb -s S shell dumpsys battery`
- `adb -s S shell cat /proc/cpuinfo`
- `adb -s S shell cat /proc/meminfo`
- `adb -s S shell wm size` / `wm density`
- `adb -s S shell df` (POSIX 1K blocks; parser also tolerates `df -h`)
- `adb -s S shell getenforce`
- `adb -s S shell uptime`
- `adb -s S shell cat /proc/version` (kernel)
- `adb -s S shell id`
- `adb -s S shell which su` (also `command -v su`)
- `adb -s S shell su -c id`
- `adb -s S shell ip addr` (fallback `ifconfig`)
- `adb -s S shell getprop | ...` for network props (wifi mac `cat /sys/class/net/wlan0/address`)
- `adb -s S logcat -v threadtime`

### 7.5 `.Parsers` (pure, static or stateless; deterministic; unit-tested)
- `AdbDevicesParser.Parse(string) -> IReadOnlyList<AdbDevice>` — parses `devices -l`, maps state
  tokens (`device`->Online, `unauthorized`->Unauthorized, `offline`->Offline, `bootloader`,
  `recovery`, `sideload`, `no permissions`->NoPermission, `authorizing`, `connecting`). Skips the
  `List of devices attached` header and blank lines.
- `GetPropParser.Parse(string) -> IReadOnlyDictionary<string,string>` — parses `[key]: [value]` lines.
- `BatteryParser.Parse(string) -> BatteryInfo` — parses `dumpsys battery` key/values; temperature/10;
  status int 1..5 -> enum; health int -> enum; plugged int (0 none,1 AC,2 USB,4 wireless) -> enum.
- `CpuInfoParser.Parse(string) -> CpuInfo` — model name/Hardware, processor count, Features.
- `MemInfoParser.Parse(string) -> MemoryInfo` — MemTotal/MemAvailable/MemFree (kB -> bytes).
- `StorageParser.Parse(string) -> StorageInfo` — df table; supports 1K blocks and `-h` suffixes
  (K/M/G/T). Extracts mount points; flags `/data`,`/system`,`/cache`,external `/storage`,`/sdcard`.
- `ScreenParser.ParseSize(string) -> string?` (from `wm size`, "Physical size: 1080x2400"),
  `ScreenParser.ParseDensity(string) -> int?` (from `wm density`).
- `RootStatusParser.Parse(idOutput, whichSuOutput, suIdOutput, getenforceOutput, magiskProp) -> RootStatus`.
- `NetworkParser.Parse(ipAddrOutput) -> IReadOnlyList<NetworkInterfaceInfo>` and helpers for wifi ip/mac.
- Builders in `.Parsers`: `OverviewBuilder.Build(IReadOnlyDictionary<string,string> props)`,
  `HardwareBuilder.Build(props, CpuInfo, MemoryInfo, ScreenInfo)`,
  `SystemInfoBuilder.Build(props, kernel, selinux, uptime)`.

## 8. Infrastructure (project `AndroidTreeView.Infrastructure`)

- `.Settings.SettingsService : ISettingsService` — JSON at
  `Path.Combine(Environment.GetFolderPath(SpecialFolder.ApplicationData),"AndroidTreeView","settings.json")`.
  Uses `System.Text.Json` (source-gen `JsonSerializerContext` optional; reflection is fine). Debounced
  save, atomic write (temp file + move). Loads defaults if missing/corrupt (never throws to caller).
- `.Logging` — helper `LoggingSetup` to configure console + in-memory ring buffer logger provider
  (optional). Not mandatory beyond console.
- `.Persistence` — optional small JSON store helper reused by settings.

## 9. App (project `AndroidTreeView.App`)

### 9.1 ViewModels (`.ViewModels`, all `partial`, use `[ObservableProperty]` / `[RelayCommand]`)
- `ViewModelBase : ObservableObject` — plus a `DeviceCategoryViewModelBase` with
  `[ObservableProperty] bool isLoading; [ObservableProperty] string? errorMessage; string? Serial;`
  and `Task LoadAsync(string serial, CancellationToken ct)` abstract; guards + try/catch mapping
  ADB exceptions to friendly `ErrorMessage`.
- Category VMs (each `: DeviceCategoryViewModelBase`, ctor injects `IDeviceService` + `ILogger<T>`):
  `DeviceOverviewViewModel` (exposes a `DeviceOverview? Overview` + convenience strings),
  `HardwareViewModel`, `BatteryViewModel` (exposes `BatteryInfo?` + `LevelPercent` for the gauge),
  `SystemInfoViewModel`, `StorageViewModel` (`ObservableCollection<StoragePartition>`),
  `NetworkViewModel`, `RootStatusViewModel`,
  `RawPropertiesViewModel` (all props + `[ObservableProperty] string filterText` + filtered view),
  `LogcatViewModel` (injects `ILogcatService`; `ObservableCollection<LogcatEntry> Entries` bounded by
  `LogcatMaxLines`; `StartCommand/StopCommand/ClearCommand`; `[ObservableProperty] string filterText`;
  `[ObservableProperty] LogPriority minPriority`; streams on a background task, marshals adds to UI via
  `Dispatcher.UIThread.Post`).
- `DeviceNodeViewModel : ObservableObject` — wraps `AdbDevice`; props: `Serial`, `DisplayName`,
  `StatusText`, `StatusKind` (enum `DeviceBadgeKind { Online, Offline, Unauthorized, Other }`),
  `AndroidVersion?`, `int? BatteryPercent`, `bool IsRooted`, `ObservableCollection<DeviceCategory> Categories`,
  `bool IsExpanded`, `bool IsSelected`. `DeviceCategory` enum: `Overview, Hardware, Battery, System,
  Storage, Network, Root, Logs, RawProperties` with display metadata via a `CategoryNodeViewModel`
  (Category + Title + Icon glyph).
- `DeviceTreeViewModel : ObservableObject` — `ObservableCollection<DeviceNodeViewModel> Devices`;
  `[ObservableProperty] DeviceNodeViewModel? selectedDevice`; `[ObservableProperty] CategoryNodeViewModel?
  selectedCategory`; `[ObservableProperty] string searchText` (filters); `DeviceCount`; `AdbStatusText`;
  `bool IsAdbAvailable`; `RefreshCommand` (AsyncRelayCommand). Reconciles incoming `AdbDevice` list into
  existing nodes (add/remove/update) without losing selection.
- `DeviceDetailsViewModel : ObservableObject` — injects all 9 category VMs; `[ObservableProperty]
  object? currentViewModel`; `Task ShowAsync(string serial, DeviceCategory category)` selects and loads;
  exposes each category VM for binding if needed.
- `SetupViewModel : ObservableObject` — shown when ADB unavailable; `[ObservableProperty] string? adbPath`;
  `BrowseCommand` (opens file picker via an injected `IFilePickerService`), `RetryCommand`,
  instructions text. Uses `IAdbLocator` + `IAdbEnvironment` + `ISettingsService`.
- `SettingsViewModel : ObservableObject` — binds all `AppSettings` fields; `SaveCommand`,
  `BrowseAdbCommand`, `ResetCommand`; applies theme via `IThemeService`.
- `MainWindowViewModel : ObservableObject` — the shell. Injects `DeviceTreeViewModel`,
  `DeviceDetailsViewModel`, `SettingsViewModel`, `SetupViewModel`, `IDeviceMonitor`, `ISettingsService`,
  `IAdbLocator`, `IAdbEnvironment`, `IThemeService`, `ILogger`. Responsibilities:
  * `InitializeAsync()`: load settings, locate adb, set `IAdbEnvironment`, apply theme, start monitor.
  * Subscribe to `IDeviceMonitor.DevicesChanged` -> marshal to UI -> `DeviceTree` reconcile + update
    `IsAdbAvailable`.
  * React to `DeviceTree.SelectedDevice`/`SelectedCategory` -> `DeviceDetails.ShowAsync`.
  * Battery auto-refresh `PeriodicTimer` for the selected online device (interval from settings).
  * `[ObservableProperty] double windowWidth;` with `LayoutMode` (`enum AppLayoutMode { Wide, Medium,
    Narrow }`) computed; expose `IsWide/IsMedium/IsNarrow` + narrow nav state
    (`enum NarrowPage { Devices, Categories, Detail }`) + `BackCommand`.
  * `[ObservableProperty] bool isSettingsOpen;` `OpenSettingsCommand`/`CloseSettingsCommand`.
  * `IsAdbAvailable`, `HasDevices` for empty-state switching.

### 9.2 Views (`.Views`) — one `.axaml`(+`.axaml.cs`) per VM, resolved by `ViewLocator`
`MainWindow` (the only `Window`; hosts everything), plus `UserControl` views:
`DeviceTreeView`, `DeviceDetailsView`, `DeviceOverviewView`, `HardwareView`, `BatteryView`,
`SystemInfoView`, `StorageView`, `NetworkView`, `RootStatusView`, `LogcatView`,
`RawPropertiesView`, `SettingsView`, `SetupView`. Use **compiled bindings**
(`x:DataType`, `x:CompileBindings=True`) referencing the exact VM property names above. Views have
parameterless ctors + `InitializeComponent()`; DataContext supplied by ViewLocator/ContentControl.

### 9.3 `.Controls` / `.Converters` / `.Services`
- Controls: `StatusBadge` (styled ContentControl / templated), `BatteryIndicator` (level bar +
  charging glyph), `InfoRow` (label/value pair), `SectionCard`.
- Converters: `NullToBoolConverter`, `EnumToBrushConverter` (badge colors), `BytesToHumanConverter`,
  `BoolToOpacityConverter`, `LayoutModeConverter`, `LogPriorityToBrushConverter`,
  `EqualityConverter` (for narrow-nav / category selection).
- `.Services`: `IThemeService`/`ThemeService` (applies `ThemeMode` to `Application.Current`),
  `IFilePickerService`/`FilePickerService` (Avalonia `StorageProvider` file dialog for adb.exe).
- `ViewLocator : IDataTemplate` — maps `*.ViewModels.XxxViewModel` -> `*.Views.XxxView` by name;
  `Match(data) => data is ObservableObject`. Registered in `App.axaml` `<Application.DataTemplates>`.

### 9.4 Composition root
- `Program.cs` — `[STAThread] static void Main`; builds a `HostApplicationBuilder`
  (`Microsoft.Extensions.Hosting`), registers all services (see §9.5), then `BuildAvaloniaApp()`
  (`AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace()`) and
  `StartWithClassicDesktopLifetime(args)`. The built `IServiceProvider` is stored on the `App` instance
  (static/DI bridge) so `App.OnFrameworkInitializationCompleted` can resolve `MainWindowViewModel`.
- `App.axaml` / `App.axaml.cs` — Fluent theme + Inter fonts + merged style dictionaries + ViewLocator +
  converters resources. `OnFrameworkInitializationCompleted`: resolve `MainWindowViewModel`, call
  `InitializeAsync()` (fire-and-forget with logging), set `MainWindow = new MainWindow { DataContext = vm }`.

### 9.5 DI registration (in `Program.cs`)
```
services.AddSingleton<AdbOptions>();
services.AddSingleton<IAdbEnvironment, AdbEnvironment>();
services.AddSingleton<IAdbLocator, AdbLocator>();
services.AddSingleton<IAdbCommandExecutor, AdbCommandExecutor>();
services.AddSingleton<IDeviceService, AdbDeviceService>();
services.AddSingleton<ILogcatService, LogcatService>();
services.AddSingleton<IDeviceMonitor, DeviceMonitor>();
services.AddSingleton<ISettingsService, SettingsService>();
services.AddSingleton<IThemeService, ThemeService>();
services.AddSingleton<IFilePickerService, FilePickerService>();
// ViewModels
services.AddSingleton<MainWindowViewModel>();
services.AddSingleton<DeviceTreeViewModel>();
services.AddTransient<DeviceDetailsViewModel>();
services.AddTransient<DeviceOverviewViewModel>(); // + each category VM transient
services.AddTransient<SettingsViewModel>();
services.AddTransient<SetupViewModel>();
services.AddLogging(b => b.AddConsole());
```

## 10. Responsive layout rules
- `MainWindow.axaml.cs` observes `ClientSizeProperty`/`Bounds` and pushes width to
  `MainWindowViewModel.WindowWidth`. VM computes `LayoutMode`: `>=1200 Wide`, `800..1199 Medium`,
  `<800 Narrow`. XAML swaps layout via bindings to `IsWide/IsMedium/IsNarrow`:
  * Wide: 3 columns (Tree | Category nav | Content).
  * Medium: 2 columns (Tree | Content with category tabs on top).
  * Narrow: single column driven by `NarrowPage` (Devices -> Categories -> Detail) with `BackCommand`.
- Must not throw / clip at any size; use `*`/Auto grids, `ScrollViewer`s, min widths.

## 11. Theming
- Fluent theme. `ThemeService.Apply(ThemeMode)`: System => `Application.Current.RequestedThemeVariant =
  ThemeVariant.Default`; Light/Dark => explicit variant. Provide `Styles/Colors.axaml`,
  `Styles/Controls.axaml`, `Styles/Theme.axaml`. Cards rounded (CornerRadius 8), Fluent spacing,
  badge brushes keyed by resource (`Badge.Online` green, `Badge.Offline` gray, `Badge.Unauthorized`
  orange/red, `Badge.Root` purple, `Badge.Charging` accent).

## 12. Error / empty / loading states
- Setup (no ADB): SetupView with install instructions + path browse + retry.
- No devices: empty-state panel in tree area with enable-USB-debugging steps.
- Unauthorized device: node badge + detail panel guidance.
- Loading: per-category `IsLoading` spinner overlay. Errors: `ErrorMessage` banner in each category view.
- The app never crashes on normal ADB/device errors — all mapped to messages.

## 13. Threading / async rules
- All ADB via async `Process`; no `.Result`/`.Wait()`/`Task.Run` for I/O.
- CancellationToken propagated everywhere; timeouts via linked CTS.
- UI updates from background streams marshalled with `Dispatcher.UIThread.Post/InvokeAsync`.
- Collections mutated only on UI thread.

## 14. Tests
- `Adb.Tests`: one test class per parser, fixtures under `Fixtures/*.txt` (embedded or copied to
  output). Cover: devices parser (online/offline/unauthorized/-l descriptors), getprop, battery
  (charging/discharging/health/plugged/temp), cpuinfo, meminfo, df (both formats), screen, root
  (rooted/not/likely), network, overview/system/hardware builders.
- `Core.Tests`: settings clone/serialize, device state mapping, monitor start/stop no-throw with a fake
  `IDeviceService`.
- `App.Tests`: VM logic with fake services (e.g., DeviceTree reconcile keeps selection; RawProperties
  filter; Battery gauge mapping). No real ADB, no UI rendering.
```
