# AndroidTreeView App Contract

This document records the App-layer contract for the full app and the Mini companion.

Current product version: `1.0.7`.

## Shared Services

The full App and Mini must share infrastructure wherever practical. Shared registration lives in:

```text
src/AndroidTreeView.Shared/AndroidTreeViewSharedServices.cs
```

Shared services include ADB location/environment, device monitoring, scrcpy launch, settings, update checks,
update installation, and the lazily resolved Root workflow services. Mini never resolves the Root workflow and
does not import or package its tools.

## Update Products

`UpdateProductOptions` selects the product-specific update channel:

- Full App: `UpdateProductOptions.ForMainApp()` -> `AppInfo.AppUpdateKey`
- Mini: `UpdateProductOptions.ForMiniApp()` -> `AppInfo.MiniUpdateKey`

Both products use `GitHubUpdateService` and `UpdateInstaller`.

## App ViewModels

The full App owns the rich UI:

- `MainWindowViewModel`
- `DevicesViewModel`
- `DeviceCardViewModel`
- `DeviceDetailViewModel`
- `SettingsViewModel`
- `AboutViewModel`
- `SetupViewModel`
- category view models for Overview, Hardware, Battery, System, Storage, Network, Root, Logcat, and Raw Properties
- `ScreenMirrorViewModel`
- `RootWizardViewModel` (singleton so an active workflow survives navigation)

Update commands exposed to UI:

- `CheckUpdates`
- `InstallUpdate`
- `OpenRelease` / `OpenReleases`
- `DismissUpdate`

## Mini ViewModel

Mini owns only the tiny always-on companion UI:

- locate adb
- scan USB for phones with debugging disabled
- monitor devices
- guide USB debugging authorization
- launch scrcpy automatically after a device is online and authorized
- check the Mini update channel and install updates automatically

Mini must not copy ADB, scrcpy, or update implementation details from the full App.

## Views

Main App views live in `src/AndroidTreeView.App/Views`.

`RootWizardView` is a top-level App page. The per-device `RootStatusView` remains read-only.

Windows Mini views live in `src/AndroidTreeView.Mini/Views`.

macOS Apple Silicon Mini views live in `src/AndroidTreeView.Mini.Mac`.

User-facing App strings use `Strings.resx` / `Strings.zh-Hans.resx`. Mini visible strings must remain readable bilingual Chinese/English text.

## Localization

Authoritative App localization resources:

- `src/AndroidTreeView.App/Resources/Strings.resx`
- `src/AndroidTreeView.App/Resources/Strings.zh-Hans.resx`

The two files must contain the same key set. Current expected count: 329 keys each.

## Root Workflow Safety

The Root wizard is the only controlled device-write exception. It locks one ADB identity, validates the
firmware target, backs up the original image, patches with components extracted from the pinned official
Magisk APK, captures the pre-reboot fastboot baseline, verifies identity/unlock/layout, and requires a second
risk acknowledgement before writing only `boot` or `init_boot`. It never unlocks the bootloader or writes
`system`, `vendor`, `vbmeta`, `recovery`, or `super`.

Partial A/B writes retain completed partitions for retry. Cancellation during an active flash is reported as
an unknown outcome and is never treated as a clean rollback.

Real flash is gated only by the in-app confirmation screen and its explicit risk acknowledgement. Once the
user confirms, the wizard writes the patched image to the evidence-backed target partition.

## Versioning

Version fields must stay aligned:

- `AppInfo.Version`
- App csproj version fields
- Mini csproj version fields
- macOS Mini csproj version fields
- App manifest assembly identity
- WiX `ProductVersion`
- ZIP build script default version

The GitHub Actions `Publish` workflow is the official release pipeline and must build these ZIP artifacts:

- `AndroidTreeView-<version>-win-x64.zip`
- `AndroidTreeView-<version>-osx-arm64.zip`
- `AndroidTreeView-Mini-<version>-win-x64.zip`
- `AndroidTreeView-Mini-<version>-osx-arm64.zip`

## Verification

Required gates:

```bash
dotnet build src/AndroidTreeView.App/AndroidTreeView.App.csproj --no-restore
dotnet build src/AndroidTreeView.Mini/AndroidTreeView.Mini.csproj --no-restore
dotnet test AndroidTreeView.sln --no-restore
```

The update path is considered correct only when users can install through the automated installer flow and are not asked to manually replace application files.
