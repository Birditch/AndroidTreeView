# AndroidTreeView

[![License: MIT](https://img.shields.io/badge/License-MIT-0E7A5F.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Avalonia 11.3](https://img.shields.io/badge/Avalonia-11.3-663399.svg)](https://avaloniaui.net/)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows-0078D6.svg)](#windows-usage)

[Simplified Chinese](README.md) | **English**

AndroidTreeView is a Windows desktop tool for inspecting, testing, and managing Android devices through ADB. The full app shows device cards, detail pages, mirroring, tools, settings, and updates. The Mini app stays resident, watches for devices, and starts mirroring automatically after authorization.

Current version: **v1.0.4**. Current verification target: full App build passes, Mini build passes, all tests pass, and the packaging flow creates x64 upload ZIP artifacts for both App and Mini.

## Features

- Card-first device overview with serial, model, Android version, battery, charging, temperature, cycle count, connection state, root state, and last refresh time.
- Device detail pages for Overview, Hardware, Battery, System, Storage, Network, Root, Logcat, and Raw Properties.
- Shared scrcpy and ADB stack between the full app and Mini, so mirroring behavior stays in sync.
- Mirror window supports tap/swipe control, Back/Home/Recents buttons, and drag-and-drop APK installation.
- Mini listens for devices and starts mirroring automatically after USB debugging is authorized.
- Main and Mini share ADB, scrcpy, settings, update checks, and update installation services.
- Update automation downloads packages, verifies SHA-256 metadata when available, extracts x64 ZIP packages, and starts a local update script so users are not asked to manually replace files.
- Simplified Chinese and English UI, with Light / Dark / System theme modes.

## Repository Layout

```text
src/
  AndroidTreeView.Models
  AndroidTreeView.Core
  AndroidTreeView.Adb
  AndroidTreeView.Infrastructure
  AndroidTreeView.Shared
  AndroidTreeView.App
  AndroidTreeView.Mini
tests/
  AndroidTreeView.*.Tests
packaging/
  x64 ZIP packaging and optional WiX MSI packaging
build/
  Shared MSBuild targets
```

## Build And Run

Requires the .NET 10 SDK:

```bash
dotnet restore AndroidTreeView.sln
dotnet build AndroidTreeView.sln
dotnet test AndroidTreeView.sln
dotnet run --project src/AndroidTreeView.App
dotnet run --project src/AndroidTreeView.Mini
```

If ADB is not found, the app opens an ADB setup screen. Install Android platform-tools and add it to `PATH`, or choose `adb.exe` manually.

## Enable USB Debugging

1. Open Settings > About phone and tap Build number seven times.
2. Open Developer options and enable USB debugging.
3. Connect the device and accept the USB debugging prompt.
4. If the device is Unauthorized or Offline, re-authorize it, check the cable, or restart ADB.

See [docs/adb-requirements.md](docs/adb-requirements.md) for platform-tools setup and troubleshooting.

## Windows Usage

1. Launch AndroidTreeView.
2. Connect an Android device with USB debugging enabled and authorized.
3. Use the device cards for status, mirroring, CLI access, and non-destructive tools.
4. Use Settings or About to check and install updates.

## x64 ZIP Packaging

The product version is currently `1.0.4` and is kept in sync across runtime version, App/Mini assembly metadata, manifest, and the ZIP build script. Release uploads are x64-only.

```powershell
./packaging/build-update-zip.ps1 -Product App -Arch x64
./packaging/build-update-zip.ps1 -Product Mini -Arch x64
```

Example output:

```text
artifacts/AndroidTreeView-1.0.4-x64.zip
artifacts/AndroidTreeView-1.0.4-x64.zip.sha256
artifacts/AndroidTreeView-Mini-1.0.4-x64.zip
artifacts/AndroidTreeView-Mini-1.0.4-x64.zip.sha256
```

## Auto Update

- Full app update key: `android-tree-view-app`.
- Mini update key: `android-tree-view-mini`.
- `NekoIndexUpdateService` checks the internal update channel and compares semantic versions.
- `UpdateInstaller` downloads, verifies, extracts, and launches the local update script.
- Supported release packages are x64 ZIP files with `release.json`.
- Loose-file ZIP replacement is intentionally rejected.

## Verification

```bash
dotnet build src/AndroidTreeView.App/AndroidTreeView.App.csproj --no-restore
dotnet build src/AndroidTreeView.Mini/AndroidTreeView.Mini.csproj --no-restore
dotnet test AndroidTreeView.sln --no-restore
```

## License

AndroidTreeView is open source under the [MIT License](LICENSE).
