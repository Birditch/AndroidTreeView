# AGENTS.md

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

## What this is

AndroidTreeView is a **.NET 10 + Avalonia** Windows desktop tool for inspecting, testing and managing
Android devices over ADB. Despite the name it is a C#/.NET solution, **not** a Java/Kotlin Android app.
It has two shipping executables that share the same core:

- **App** (`src/AndroidTreeView.App`) — the full Avalonia UI: device overview, per-category details,
  screen mirroring (scrcpy), CLI tools, settings, updates.
- **Mini** (`src/AndroidTreeView.Mini`) — a lightweight WinForms tray/monitor app that stays resident,
  watches for devices, and auto-launches scrcpy once a device is connected and authorized. Mini
  deliberately does **not** carry the Avalonia/Skia runtime.

`Mini.Mac` is an Avalonia-based Mini variant for macOS.

## Commands

Requires the **.NET 10 SDK**. Run from the repo root.

```bash
dotnet restore AndroidTreeView.sln
dotnet build AndroidTreeView.sln
dotnet test AndroidTreeView.sln
dotnet run --project src/AndroidTreeView.App
dotnet run --project src/AndroidTreeView.Mini
```

Building on non-Windows (App/Mini target Windows) needs the targeting pack flag — CI uses it everywhere:

```bash
dotnet build AndroidTreeView.sln -p:EnableWindowsTargeting=true
```

Run a single test project / a single test:

```bash
dotnet test tests/AndroidTreeView.Adb.Tests
dotnet test AndroidTreeView.sln --filter "FullyQualifiedName~BatteryParser"
```

Format check (CI runs this, non-blocking — but keep it clean):

```bash
dotnet format AndroidTreeView.sln --verify-no-changes
```

Package/verify the release ZIP link locally (real releases only ship via the GitHub `Publish` workflow,
x64 only):

```powershell
./packaging/build-update-zip.ps1 -Product App -Rid win-x64
./packaging/build-update-zip.ps1 -Product Mini -Rid win-x64
```

There are ready-made Rider/VS run configs under `.run/`.

## Architecture

The layering is strict and enforced by project references — respect the dependency direction. Lower
layers never reference upper ones.

```
Models          domain records/enums, no project deps
  ↑
Core            interfaces, options (AppSettings, AdbOptions), typed exceptions, DeviceMonitor
  ↑
Adb  /  Infrastructure     Adb: ProcessRunner, command builders, parsers, AdbDeviceService, LogcatService
  ↑                        Infrastructure: SettingsService (JSON), logging, update check/install
Shared          the shared composition root — AddAndroidTreeViewSharedServices() wires ADB, monitoring,
  ↑             scrcpy, settings, and update services identically for both App and Mini
App  /  Mini  /  Mini.Mac   executables (each references Shared)
```

Key architectural rules (see `docs/architecture.md` for the full binding type/interface contract, and
`docs/app-contract.md`):

- **Shared is the single wiring point.** Both App and Mini call
  `AddAndroidTreeViewSharedServices(...)` so their ADB/scrcpy/settings/update behavior can't drift.
  When adding a service that both should use, register it there (it uses `TryAdd*`), not per-app.
- **App is strict MVVM** with CommunityToolkit.Mvvm (`[ObservableProperty]` / `[RelayCommand]`) and
  `Microsoft.Extensions` DI/Hosting/Logging. `Program.cs` builds the host + `IServiceProvider`, then
  Avalonia resolves `MainWindowViewModel`. `ViewLocator` maps `*.ViewModels.XxxViewModel` →
  `*.Views.XxxView` by name convention.
- **All ADB is out-of-process** via `ProcessRunner` (async stdout/stderr, kills the whole process tree
  on cancel/timeout). Device info comes from parsing ADB text output.
- **Parsers are pure and unit-tested.** ADB output parsing lives in `AndroidTreeView.Adb.Parsers` as
  stateless/static functions (`GetPropParser`, `BatteryParser`, `AdbDevicesParser`, etc.). Any change to
  parsing logic must come with a parser test — fixtures live under the Adb.Tests project.
- `AdbLocator` resolves adb (configured path → PATH → common SDK locations); `IAdbEnvironment` holds the
  current location as a singleton. If adb is missing the App shows a Setup page instead of crashing.
- `DeviceMonitor` owns a `PeriodicTimer` background loop and raises `DevicesChanged`; VMs marshal to the
  UI thread themselves.

## Conventions (from `.editorconfig` + `CONTRIBUTING.md`)

- File-scoped namespaces, 4-space indent, interfaces `I`-prefixed, `using` outside the namespace,
  CRLF line endings, UTF-8.
- **Async-only for I/O**: `async` + `CancellationToken` propagated everywhere. No `.Result` / `.Wait()` /
  `Task.Run` for ADB/process work. UI mutations only on the UI thread (`Dispatcher.UIThread.Post`).
- Views use **compiled bindings** (`x:DataType`) and bind only to members that exist on the VM. No
  business logic or ADB calls in views.
- **No hardcoded user-facing strings.** Use localization for every user-visible string (see below).
- **Read-only / safe by design**: do not introduce ADB commands that modify, wipe, or flash a device.
- Keep files under ~400 lines; the App never crashes on normal ADB/device errors — map them to friendly
  `ErrorMessage` state.

## Localization

Every user-facing string is localized — never hardcode display text. Default language is **Simplified
Chinese**; English is the neutral/fallback resource.

- **Contract**: `ILocalizationService` (`AndroidTreeView.Core/Interfaces`) — `Get(key)`, `Format(key, args)`,
  the `this[key]` indexer, `SetLanguage(AppLanguage)`, and a `LanguageChanged` event. `AppLanguage` lives
  in `AndroidTreeView.Core/Options/SettingsEnums.cs`. `Get` returns the key itself if a resource is
  missing, so a missing translation is visible, not a crash.
- **Implementation**: `LocalizationService` in `src/AndroidTreeView.App/Localization`, backed by two ResX
  files with **identical key sets** (keep them in sync — currently 217 keys each):
  - `src/AndroidTreeView.App/Resources/Strings.resx` — English (neutral fallback)
  - `src/AndroidTreeView.App/Resources/Strings.zh-Hans.resx` — Simplified Chinese (default)
  - Keys are dotted/namespaced: `app.title`, `nav.devices`, `common.refresh`, etc.
- **In ViewModels**: inject `ILocalizationService` and call `_localization.Get("key")` / `.Format(...)`.
- **In XAML**: use the `{loc:Localize Key=some.key}` markup extension (`LocalizeExtension` +
  `LocalizeKeyConverter`). Bindings watch `LocalizationService.LanguageTick` so text refreshes live when
  the language changes (the indexer's own `INotifyPropertyChanged` was unreliable in this Avalonia
  version — that's why the plain `LanguageTick` counter exists; keep using it for live-refresh bindings).

**When adding a string**: add the key to **both** ResX files, then reference it via `Get`/`Format` (VMs)
or `{loc:Localize}` (XAML). Never add a key to only one file.

## Tests

xUnit across four projects (`dotnet test AndroidTreeView.sln`, or target one project — see Commands).
Fixtures are **inline `const` strings** inside the test classes, not external `.txt` files.

- **`Adb.Tests`** — the bulk of the suite. `Parsers/` has one test class per parser/builder
  (`BatteryParserTests`, `StorageParserTests`, `AdbDevicesParserTests`, `GetPropParserTests`,
  `RootStatusParserTests`, `NetworkParserTests`, the `*BuilderTests`, etc.), `Commands/` covers argv
  building (`AdbArgsTests`), `Services/` covers device-action/screen-capture services. **Any change to ADB
  output parsing must add/update the matching parser test here** — this is where correctness is pinned.
- **`Core.Tests`** — `AppSettings` clone/serialize, `DeviceMonitor` start/stop (with a fake
  `IDeviceService`), `SemanticVersion`, file-transfer service.
- **`Infrastructure.Tests`** — `SettingsService` persistence, update check (`NekoIndexUpdateService`) and
  `UpdateInstaller`, using test doubles in `TestDoubles.cs`.
- **`App.Tests`** — ViewModel logic with fake services (`Fakes.cs`): device-list reconcile keeps
  selection, RawProperties filtering, Logcat bounding, DI graph resolves (`ServiceGraphTests`), and a boot
  smoke test via `Avalonia.Headless` (`TestAppBuilder.cs`, `BootSmokeTests.cs`). No real ADB, no on-screen
  rendering.

## Versioning & updates

Version is unified across runtime version, App/Mini assembly versions, manifests, and
`packaging/build-update-zip.ps1` — keep them in sync when bumping (currently `1.0.6`). Update channels:
`android-tree-view-app` (App) and `android-tree-view-mini` (Mini). `NekoIndexUpdateService` checks the
channel and compares semver; `UpdateInstaller` downloads, verifies SHA-256, unpacks the x64 ZIP, and runs
a local update script. Loose ZIPs without a supported `release.json` manifest are rejected.

## Bundled tools

`build/AndroidTreeView.Scrcpy.targets` downloads and bundles scrcpy (which ships adb) into `tools/scrcpy`
at build/publish time on Windows. `tools/verify-scrcpy-latest.ps1` checks for newer scrcpy releases.
