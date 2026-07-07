# AndroidTreeView — App-layer Contract (binding for all App agents)

Extends `docs/architecture.md` + `docs/requirements-v1.md`. This pins exact names so parallel agents
stay coherent. Project `AndroidTreeView.App` (net10.0, Avalonia 11.3.18, CommunityToolkit.Mvvm 8.4.2).
Only the **shell agent** edits `App.csproj`, `App.axaml`, `App.axaml.cs`, `Program.cs`. No agent edits
the `.sln` or the Models/Core/Adb/Infrastructure/Core.Tests projects.

## Concrete service implementations (for DI — names are FIXED by earlier phases)
- `AndroidTreeView.Adb.Services.AdbEnvironment` : `IAdbEnvironment` (singleton)
- `AndroidTreeView.Adb.Services.AdbLocator` : `IAdbLocator`
- `AndroidTreeView.Adb.Services.AdbCommandExecutor` : `IAdbCommandExecutor`
- `AndroidTreeView.Adb.Services.AdbDeviceService` : `IDeviceService`
- `AndroidTreeView.Adb.Services.LogcatService` : `ILogcatService`
- `AndroidTreeView.Core.Services.DeviceMonitor` : `IDeviceMonitor`
- `AndroidTreeView.Infrastructure.Settings.SettingsService` : `ISettingsService`
- `AndroidTreeView.Infrastructure.Update.GitHubUpdateService` : `IUpdateService`
  (ctor needs `HttpClient`, `ISettingsService`, `ILogger<>`)
- App-owned: `AndroidTreeView.App.Services.ThemeService` : `IThemeService`,
  `AndroidTreeView.App.Services.FilePickerService` : `IFilePickerService`,
  `AndroidTreeView.App.Localization.LocalizationService` : `ILocalizationService`.
> If an earlier-phase class name differs from the above, the integration pass fixes DI — feature agents
> should NOT depend on concrete impls, only on the Core interfaces.

## App-owned service interfaces (define in `AndroidTreeView.App.Services`)
```csharp
public interface IThemeService {
    void Initialize();
    void Apply(ThemeMode mode);          // AndroidTreeView.Core.Options.ThemeMode
    void ApplyAccent(string? hexColor);
}
public interface IFilePickerService {
    Task<string?> PickAdbExecutableAsync();     // returns full path or null
    Task OpenUrlAsync(string url);               // opens default browser
}
```

## Shared bases / enums (define in `AndroidTreeView.App.ViewModels`; shell agent owns)
```csharp
public abstract class ViewModelBase : ObservableObject { }

public abstract partial class DeviceCategoryViewModelBase : ViewModelBase {
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string? _errorMessage;
    public string? Serial { get; protected set; }
    public abstract DeviceCategory Category { get; }
    public abstract Task LoadAsync(string serial, CancellationToken ct);
    // protected Task RunAsync(Func<CancellationToken,Task> body, CancellationToken ct) that toggles
    // IsLoading and maps exceptions to a localized ErrorMessage/HasError (never throws to UI).
}

public enum AppLayoutMode { Wide, Medium, Narrow }
public enum NavSection { Devices, Settings, About }
public enum DeviceBadgeKind { Online, Offline, Unauthorized, Other }
public enum DeviceCategory { Overview, Hardware, Battery, System, Storage, Network, Root, Logcat, RawProperties }
```

## ViewModels — exact public surface (bindable). Each `partial`, uses `[ObservableProperty]`/`[RelayCommand]`.

### MainWindowViewModel : ViewModelBase   (shell agent)
Props: `object? CurrentContent`; `DevicesViewModel Devices`; `SettingsViewModel Settings`;
`SetupViewModel Setup`; `NavSection CurrentSection`; `AppLayoutMode LayoutMode`; `double WindowWidth`;
`bool IsWide`, `bool IsMedium`, `bool IsNarrow`; `bool IsSidebarOpen`; `bool IsAdbAvailable`;
`string AdbStatusText`; `int DeviceCount`; `bool ShowUpdateBanner`; `string? LatestVersion`;
`string UpdateBannerText`.
Commands: `NavigateDevices`, `NavigateSettings`, `NavigateAbout`, `Refresh`, `ToggleSidebar`, `Back`,
`OpenRelease`, `DismissUpdate`.
Methods: `Task InitializeAsync()` (load settings → apply language+theme → locate adb → set env → if
adb missing show Setup else start monitor → subscribe DeviceMonitor.DevicesChanged (marshal via
Dispatcher.UIThread) → fire-and-forget update check); `void ShowDeviceDetail(DeviceCardViewModel card)`
(sets CurrentContent to a new DeviceDetailViewModel and loads); `void SetWindowWidth(double w)`.

### DevicesViewModel : ViewModelBase   (devices agent)
Props: `ObservableCollection<DeviceCardViewModel> Devices`; `string SearchText`; `bool HasDevices`;
`bool IsEmpty` (adb ok, zero devices); `bool IsAdbAvailable`; `int DeviceCount`;
`DeviceCardViewModel? SelectedDevice`.
Commands: `Refresh`.
Event: `event EventHandler<DeviceCardViewModel>? DeviceActivated;`
Methods: `void Reconcile(IReadOnlyList<AdbDevice> devices, bool adbAvailable)` (add/update/remove cards,
assign 1-based Index, keep selection); `void ActivateDevice(DeviceCardViewModel card)` (raises event);
holds `ILocalizationService`. Filtering by SearchText over name/model/serial.

### DeviceCardViewModel : ViewModelBase   (devices agent)
Props: `string Serial`; `int Index`; `string DisplayName`; `string Manufacturer`; `string Model`;
`string AndroidVersion`; `DeviceConnectionState State`; `DeviceBadgeKind StatusKind`; `string StatusText`;
`int? BatteryPercent`; `bool IsCharging`; `string ChargingText`; `double? BatteryTemperatureCelsius`;
`string TemperatureText`; `int? CycleCount`; `bool CycleCountAvailable`; `string CycleCountText`;
`bool? IsRooted`; `string RootText`; `DateTimeOffset? LastRefresh`; `string LastRefreshText`;
`bool IsLowBattery`; `bool IsUnauthorized`; `bool IsOnline`.
Command: `Open` (invokes owner callback → DevicesViewModel.ActivateDevice).
Method: `void UpdateFrom(AdbDevice device, BatteryInfo? battery, RootStatus? root)`. Localized text via
injected `ILocalizationService`. "Unavailable" cycle-count text uses key `card.cycle.unavailable`.

### DeviceDetailViewModel : ViewModelBase   (devices agent)
Props: `string Serial`; `string DisplayName`; `string Model`; `string StatusText`; `int? BatteryPercent`;
`string RootText`; `DeviceBadgeKind StatusKind`; `ObservableCollection<DetailTab> Tabs`;
`DetailTab? SelectedTab`; `DeviceCategoryViewModelBase? CurrentCategory`.
Commands: `Refresh`, `Back`.
`DetailTab` (plain class): `DeviceCategory Category`, `string Title`, `string Glyph`,
`DeviceCategoryViewModelBase ViewModel`. Selecting a tab sets `CurrentCategory` + calls its
`LoadAsync(Serial, ct)`. `Back` raises an event the shell subscribes to (return to device grid).
Holds all 9 category VMs (injected).

### Category VMs (`: DeviceCategoryViewModelBase`) — category agents
- `OverviewViewModel`  (Category=Overview):  `DeviceOverview? Overview`. Uses `IDeviceService.GetOverviewAsync`.
- `HardwareViewModel`  (Hardware):           `HardwareInfo? Hardware`.
- `BatteryViewModel`   (Battery):            `BatteryInfo? Battery`; `int LevelPercent`; `string ChargingText`.
- `SystemInfoViewModel`(System):             `SystemInfo? Info` (`using AndroidTreeView.Models.System;`).
- `StorageViewModel`   (Storage):            `ObservableCollection<StoragePartition> Partitions`.
- `NetworkViewModel`   (Network):            `NetworkInfo? Network`; `ObservableCollection<NetworkInterfaceInfo> Interfaces`.
- `RootStatusViewModel`(Root):               `RootStatus? Root`.
- `LogcatViewModel`    (Logcat):             `ObservableCollection<LogcatEntry> Entries`; `string FilterText`;
  `LogPriority MinPriority`; `bool IsRunning`; commands `Start`,`Stop`,`Clear`. Injects `ILogcatService`;
  streams on a background task; marshals adds via `Dispatcher.UIThread.Post`; caps to
  `settings.LogcatMaxLines`.
- `RawPropertiesViewModel` (RawProperties):  `DeviceProperties? Properties`;
  `ObservableCollection<PropertyRow> FilteredProperties`; `string FilterText`.
  `PropertyRow` (plain): `string Key`, `string Value`.

### SettingsViewModel : ViewModelBase   (settings agent)
Bindable (two-way): `AdbPath`; `ThemeMode Theme`; `AppLanguage Language`; `bool AutoRefreshEnabled`;
`int DeviceRefreshIntervalSeconds`; `int BatteryRefreshIntervalSeconds`; `int LogcatMaxLines`;
`bool AutoCheckUpdates`; `string? AccentColor`; `bool RememberLastSelectedDevice`; `StartupBehavior Startup`.
Read-only: `string CurrentVersion` (AppInfo.Version); `string? LatestVersion`; `string UpdateStatusText`;
option lists `ThemeMode[] ThemeOptions`, `AppLanguage[] LanguageOptions`.
Commands: `Save`, `BrowseAdb`, `CheckUpdates`, `OpenReleases`, `Reset`. Injects `ISettingsService`,
`IThemeService`, `ILocalizationService`, `IUpdateService`, `IFilePickerService`, `IAdbLocator`,
`IAdbEnvironment`. Save persists + applies theme/language live.

### SetupViewModel : ViewModelBase   (settings agent)
Props: `string? AdbPath`; `bool IsChecking`; `string? StatusMessage`; `string InstructionsText`.
Commands: `Browse`, `Retry`, `OpenDownloadPage`. Injects `IAdbLocator`, `IAdbEnvironment`,
`ISettingsService`, `IFilePickerService`. Event `event EventHandler? AdbReady;` raised when adb located.

### AboutViewModel : ViewModelBase   (settings agent)
Props: `string AppName`, `string Version`, `string ProjectUrl`, `string? LatestVersion`,
`string UpdateStatusText`. Commands: `CheckUpdates`, `OpenProject`, `OpenReleases`.

## Views (`AndroidTreeView.App.Views`) — one per VM, resolved by ViewLocator (VM `Xxx` → View `Xxx`)
`MainWindow` (Window, shell agent) + UserControls: `DevicesView`, `DeviceCardView`, `DeviceDetailView`,
`OverviewView`, `HardwareView`, `BatteryView`, `SystemInfoView`, `StorageView`, `NetworkView`,
`RootStatusView`, `LogcatView`, `RawPropertiesView`, `SettingsView`, `SetupView`, `AboutView`.
Use compiled bindings: set `x:DataType` to the VM type and bind ONLY to the properties above. Parameterless
ctors + `InitializeComponent()`.

ViewLocator (shell agent, `AndroidTreeView.App`): `IDataTemplate`; maps by replacing namespace segment
`ViewModels`→`Views` and suffix `ViewModel`→`View`; `Match(data) => data is ViewModelBase`.

## Controls (`AndroidTreeView.App.Controls`) — controls agent
- `StatusBadge` (TemplatedControl or styled ContentControl): props `string Text`, `DeviceBadgeKind Kind`.
- `BatteryIndicator`: props `int? Level`, `bool IsCharging`, `bool IsLow`.
- `InfoRow`: props `string Label`, `string? Value` (label/value line; shows "—" when null/empty).
- `SectionCard`: header + content ContentControl with the glass style; props `string Header`.
- `SkeletonBlock`: a shimmer placeholder for loading states (simple).

## Converters (`AndroidTreeView.App.Converters`) — controls agent; register in `Converters/Converters.axaml`
`ResourceDictionary` with keyed instances: `NullToBool` (`x:Key=NullToBool`), `NullOrEmptyToDash`,
`BoolToOpacity`, `BytesToHuman`, `EnumEquals` (param compare, for tab/nav selection highlight),
`LayoutModeEquals`, `LogPriorityToBrush`, `BadgeKindToBrush`, `BatteryLevelToBrush`, `InverseBool`,
`CountToBool`. The shell merges this dictionary into `App.axaml`.

## Styles / liquid glass (`AndroidTreeView.App.Styles`) — design agent
Files: `Styles/Colors.axaml` (light+dark color resources, accent), `Styles/Glass.axaml` (glass brushes:
`Glass.Card.Background`, `Glass.Card.BackgroundHover`, `Glass.Card.Border`, `Glass.Sidebar.Background`,
box-shadow resources), `Styles/Badges.axaml` (`Badge.Online` green, `Badge.Offline` gray,
`Badge.Unauthorized` orange/red, `Badge.Root` purple, `Badge.Charging` accent, `Badge.LowBattery` red),
`Styles/Controls.axaml` (card/button/textbox/tab styling, rounded corners >=8, hover/selected
transitions). Support light/dark via `ThemeVariant` scopes. MainWindow uses
`TransparencyLevelHint = AcrylicBlur, Mica, Blur` + an `ExperimentalAcrylicBorder`/panel behind content;
degrade gracefully to a solid translucent brush where unsupported. The shell `<StyleInclude>`s all four
plus `avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml`.

## Localization — loc agent
- `AndroidTreeView.App.Localization.LocalizationService : ILocalizationService`, backed by a `ResourceManager`
  over `Resources/Strings.resx` (neutral = English, safe fallback) + `Resources/Strings.zh-Hans.resx`
  (Simplified Chinese). `SetLanguage` maps `AppLanguage` → culture (`zh-Hans` / `en` / OS) and sets
  `Thread.CurrentThread.CurrentUICulture` + raises `LanguageChanged`. Default at startup = zh-Hans.
- `LocalizeExtension` (markup, `AndroidTreeView.App.Localization`): usage `{loc:Localize Key=some.key}`;
  updates live on `LanguageChanged`. XAML uses `xmlns:loc="using:AndroidTreeView.App.Localization"`.
- VMs get localized text via injected `ILocalizationService.Get("some.key")`. Missing key → returns the
  key (graceful). Use ONLY keys from the master list below; add both en + zh entries for each.

### Localization master keys (key = en | zh) — resx MUST contain all; VMs/Views use these strings
```
app.title = AndroidTreeView | AndroidTreeView
nav.devices = Devices | 设备
nav.settings = Settings | 设置
nav.about = About | 关于
common.refresh = Refresh | 刷新
common.back = Back | 返回
common.retry = Retry | 重试
common.browse = Browse… | 浏览…
common.save = Save | 保存
common.reset = Reset | 重置
common.loading = Loading… | 加载中…
common.unavailable = Unavailable | 不可用
common.yes = Yes | 是
common.no = No | 否
common.none = None | 无
common.search = Search | 搜索
status.online = Online | 在线
status.offline = Offline | 离线
status.unauthorized = Unauthorized | 未授权
status.rooted = Rooted | 已 Root
status.charging = Charging | 充电中
status.lowbattery = Low Battery | 电量低
adb.status.ready = ADB ready | ADB 就绪
adb.status.missing = ADB not found | 未找到 ADB
adb.status.devices = {0} device(s) | {0} 台设备
devices.title = Android Devices | 安卓设备
devices.empty.title = No devices detected | 未检测到设备
devices.empty.body = Enable Developer Options and USB debugging, connect your phone, and accept the debugging prompt. | 请开启开发者选项与 USB 调试，连接手机并允许调试授权。
devices.count = {0} connected | 已连接 {0} 台
card.serial = Serial | 序列号
card.battery = Battery | 电量
card.charging = Charging | 充电
card.cycle = Cycle Count | 循环次数
card.cycle.unavailable = Cycle Count: Unavailable | 循环次数：不可用
card.temperature = Temperature | 温度
card.root = Root | Root
card.lastrefresh = Last refresh | 最后刷新
detail.tab.overview = Overview | 概览
detail.tab.hardware = Hardware | 硬件
detail.tab.battery = Battery | 电池
detail.tab.system = System | 系统
detail.tab.storage = Storage | 存储
detail.tab.network = Network | 网络
detail.tab.root = Root | Root
detail.tab.logcat = Logcat | 日志
detail.tab.raw = Raw Properties | 原始属性
overview.manufacturer = Manufacturer | 制造商
overview.brand = Brand | 品牌
overview.model = Model | 型号
overview.android = Android Version | 安卓版本
overview.api = API Level | API 级别
overview.build = Build Number | 版本号
overview.fingerprint = Fingerprint | 指纹
overview.patch = Security Patch | 安全补丁
hardware.cpu = CPU | 处理器
hardware.arch = Architecture | 架构
hardware.cores = Cores | 核心数
hardware.abi = ABI List | ABI 列表
hardware.ram = RAM | 内存
hardware.screen = Screen | 屏幕
hardware.density = Density | 密度
battery.level = Level | 电量
battery.status = Status | 状态
battery.health = Health | 健康度
battery.temp = Temperature | 温度
battery.voltage = Voltage | 电压
battery.tech = Technology | 技术
battery.cycle = Cycle Count | 循环次数
system.kernel = Kernel | 内核
system.selinux = SELinux | SELinux
system.uptime = Uptime | 运行时间
system.locale = Locale | 区域
system.timezone = Timezone | 时区
storage.total = Total | 总计
storage.used = Used | 已用
storage.free = Available | 可用
network.wifiip = Wi-Fi IP | Wi-Fi IP
network.wifimac = Wi-Fi MAC | Wi-Fi MAC
root.detected = Root detected | 检测到 Root
root.notdetected = Not rooted | 未 Root
root.selinux = SELinux Mode | SELinux 模式
logcat.start = Start | 开始
logcat.stop = Stop | 停止
logcat.clear = Clear | 清空
logcat.filter = Filter | 过滤
raw.filter = Filter properties | 过滤属性
settings.title = Settings | 设置
settings.adbpath = ADB Path | ADB 路径
settings.theme = Theme | 主题
settings.theme.system = Follow System | 跟随系统
settings.theme.light = Light | 亮色
settings.theme.dark = Dark | 暗色
settings.language = Language | 语言
settings.language.system = Follow System | 跟随系统
settings.language.zh = 简体中文 | 简体中文
settings.language.en = English | English
settings.autorefresh = Auto Refresh | 自动刷新
settings.refreshinterval = Device Refresh Interval (s) | 设备刷新间隔（秒）
settings.batteryinterval = Battery Refresh Interval (s) | 电池刷新间隔（秒）
settings.logcatmax = Logcat Max Lines | 日志最大行数
settings.autoupdate = Auto Check Updates | 自动检查更新
settings.accent = Accent Color | 强调色
settings.remember = Remember Last Device | 记住上次设备
settings.startup = Startup Behavior | 启动行为
update.current = Current Version | 当前版本
update.latest = Latest Version | 最新版本
update.check = Check for Updates | 检查更新
update.available = Update available: {0} | 有可用更新：{0}
update.uptodate = You are on the latest version | 已是最新版本
update.checking = Checking… | 检查中…
update.failed = Update check failed | 检查更新失败
update.openrelease = View Release | 查看发布
about.title = About | 关于
about.project = Project Home | 项目主页
setup.title = ADB Not Found | 未找到 ADB
setup.body = AndroidTreeView needs Android platform-tools (adb). Install it and add adb to PATH, or select adb manually. | AndroidTreeView 需要 Android platform-tools（adb）。请安装并加入 PATH，或手动选择 adb。
setup.download = Download platform-tools | 下载 platform-tools
setup.selectadb = Select adb executable | 选择 adb 可执行文件
error.generic = Something went wrong | 出现错误
error.unauthorized = Device unauthorized — accept the USB debugging prompt on the device | 设备未授权 —— 请在设备上允许 USB 调试
error.offline = Device offline | 设备离线
error.loadfailed = Failed to load {0} | 加载 {0} 失败
```

## DI registration (shell agent, Program.cs)
Register: `HttpClient` (singleton, with User-Agent); all Adb/Core/Infrastructure services (singletons);
`IThemeService`,`IFilePickerService`,`ILocalizationService` (singletons); `MainWindowViewModel`,
`DevicesViewModel` (singleton); `SettingsViewModel`,`SetupViewModel`,`AboutViewModel`,
`DeviceDetailViewModel` + all 9 category VMs (transient); `AddLogging(b => b.AddConsole())`. Bridge the
built `IServiceProvider` to `App` so `OnFrameworkInitializationCompleted` resolves `MainWindowViewModel`,
calls `InitializeAsync()` (logged fire-and-forget), and shows `MainWindow{ DataContext = vm }`.
App.csproj: add `<Version>1.0.0</Version>`, `<AssemblyVersion>1.0.0.0</AssemblyVersion>`,
`<InformationalVersion>1.0.0</InformationalVersion>`; create `app.manifest` (referenced by csproj);
include `Assets/**` as AvaloniaResource and set an app icon.
