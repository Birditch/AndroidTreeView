# AndroidTreeView

[![License: MIT](https://img.shields.io/badge/License-MIT-0E7A5F.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Avalonia 11](https://img.shields.io/badge/Avalonia-11.3-663399.svg)](https://avaloniaui.net/)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows-0078D6.svg)](#windows-usage)
[![Issues](https://img.shields.io/github/issues/Birditch/AndroidTreeView?label=Issues&color=2F4858)](https://github.com/Birditch/AndroidTreeView/issues)
[![Pull Requests](https://img.shields.io/github/issues-pr/Birditch/AndroidTreeView?label=Pull%20Requests&color=0E7A5F)](https://github.com/Birditch/AndroidTreeView/pulls)

[简体中文（默认）](README.md) | **English**

AndroidTreeView is a cross-platform desktop app built with **.NET 10 + [Avalonia](https://avaloniaui.net/)**
(primary target **Windows**) that discovers connected Android devices via **ADB (Android Debug Bridge)**
and displays rich per-device information. It uses a **card-grid** UI: each connected device is rendered as a
card on the main screen; clicking a card opens a **device detail page** with categories such as Overview,
Hardware, Battery, System, Storage, Network, Root, Logcat, and Raw Properties. The UI **defaults to
Simplified Chinese** and can switch to English; it supports Light / Dark / System themes, a tasteful liquid-glass
style, and update checking.

> Note: AndroidTreeView is **read-only** — it never modifies, flashes, or damages your device. All data is
> read through standard ADB commands.

<p align="center">
  <a href="https://github.com/Birditch/AndroidTreeView">
    <img src="https://socialify.git.ci/Birditch/AndroidTreeView/image?description=1&amp;font=Inter&amp;forks=1&amp;issues=1&amp;language=1&amp;owner=1&amp;pattern=Signal&amp;pulls=1&amp;stargazers=1&amp;theme=Light" alt="AndroidTreeView repository overview" width="680" />
  </a>
</p>

## Introduction

AndroidTreeView targets workflows that require **frequent inspection of Android device information** —
development, testing, repair, labs, and device management. It is not a phone app; it is a **desktop tool**
that talks to devices through the locally installed `adb`:

1. Detects whether ADB is available (guides you to install it or pick `adb` manually if not).
2. Lists currently connected devices and shows core info as cards.
3. Opens a detail page on card click to view the full device data by category.

Tech stack: **.NET 10**, **Avalonia 11**, **CommunityToolkit.Mvvm** (strict MVVM),
**Microsoft.Extensions** (Dependency Injection / Hosting / Logging).

## Features

- **Card-first device overview**: one card per device showing display name / index (#1, #2…), manufacturer,
  model, serial number, Android version, connection status, battery %, charging status, battery temperature,
  battery cycle count (shows "Unavailable" when not readable — never faked), root status, and last refresh time.
- **Status badges**: Online / Offline / Unauthorized / Rooted / Charging / Low Battery.
- **Device detail page**: opened by clicking a card, with categories for Overview, Hardware, Battery, System,
  Storage, Network, Root, Logcat, and searchable Raw Properties (`getprop`).
- **i18n**: **Simplified Chinese by default**, switchable to English (extensible), with all UI strings coming
  from localization resources — nothing hardcoded.
- **Theming**: Light / Dark / System, optional accent color.
- **Liquid-glass UI**: translucent cards, soft shadows, rounded corners, hover/selected transitions, readable
  in both light and dark, with graceful fallback where blur is unsupported.
- **Responsive layout**: Wide / Medium / Narrow breakpoints; nothing overflows or clips at any width.
- **Update check**: checks GitHub Releases for newer versions with a non-intrusive banner and a manual
  "Check for Updates" action.
- **Robust ADB interaction**: fully async, cancellable, with timeouts; normal device errors (unauthorized /
  offline) are mapped to friendly messages instead of crashing.
- **Read-only and safe**: reads information through standard ADB commands only; never modifies the device.

## Screenshots

> Screenshot placeholder: real screenshots will be added after the first release. Planned shots include the
> device card overview, the device detail page, and the settings page.

<!-- Screenshots are planned under docs/screenshots/, e.g.:
| Device overview | Device detail | Settings |
| --- | --- | --- |
| ![devices](docs/screenshots/devices.png) | ![detail](docs/screenshots/detail.png) | ![settings](docs/screenshots/settings.png) |
-->

## Installation

After the first release, Windows installers (MSI) will be available on the
[Releases page](https://github.com/Birditch/AndroidTreeView/releases).

Before then, run from source (requires the .NET 10 SDK — see [Development Environment](#development-environment)):

```bash
git clone https://github.com/Birditch/AndroidTreeView.git
cd AndroidTreeView
dotnet run --project src/AndroidTreeView.App
```

> Whether you use the installer or run from source, a working **ADB (platform-tools)** installation is required
> — see the next section.

## ADB Configuration

AndroidTreeView relies on `adb` from the Android SDK **platform-tools**. It locates `adb` in this order:

1. An ADB path configured manually in Settings.
2. `adb` / `adb.exe` on the system `PATH`.
3. Common SDK locations (e.g. `%LOCALAPPDATA%\Android\Sdk\platform-tools`,
   `~/Library/Android/sdk/platform-tools`, `~/Android/Sdk/platform-tools`, `ANDROID_HOME` / `ANDROID_SDK_ROOT`).

If ADB cannot be found, the app shows a setup screen: install platform-tools and add it to `PATH`, or select the
`adb` executable manually.

Full installation and troubleshooting steps (Windows / macOS / Linux) are in
**[docs/adb-requirements.md](docs/adb-requirements.md)**.

## Enable USB Debugging

1. Open **Settings → About phone** on the device and tap **Build number** seven times until developer mode is enabled.
2. Go to **Settings → System → Developer options** and turn on **USB debugging**.
3. Connect the device to the computer via USB; when the **"Allow USB debugging?"** prompt appears, check
   "Always allow from this computer" and tap **Allow / OK**.
4. If the prompt never appears, or the device shows **unauthorized** / **offline**, see the troubleshooting section
   of [docs/adb-requirements.md](docs/adb-requirements.md).

> Menu locations vary slightly across vendors and OS versions, but the flow is the same: Developer options →
> USB debugging → authorize this computer on the device.

## Windows Usage

1. Install the MSI or run from source, then launch AndroidTreeView.
2. On first launch, if ADB is not detected you land on the setup screen: install platform-tools and add it to
   `PATH`, or pick `adb.exe` manually, then click Retry.
3. Enable USB debugging on the device and authorize this computer (see above).
4. Connected devices appear as cards; click a card to open the detail page.
5. In **Settings** you can switch language (Chinese / English), theme (System / Light / Dark), refresh intervals,
   and more; use **About / Settings** to check for updates manually.

## MSI Installation

- MSI installers are provided for both **win-x64** and **win-x86**, packaged with WiX Toolset v5.
- The MSI installs the application, required files, and app icon, and creates a Start Menu shortcut (optionally a
  Desktop shortcut).
- The **.NET 10 desktop runtime** is required; if missing, the installer / app shows a clear message and guides
  you to install it.
- Local MSI packaging scripts and detailed steps are documented in **[docs/packaging.md](docs/packaging.md)**.

## Auto Update

- The app checks the latest release via the GitHub Releases API
  (`/repos/Birditch/AndroidTreeView/releases/latest`) and compares it against the current version using semantic
  versioning.
- When a newer version is found, only a **non-intrusive banner** is shown, with a button to open the release page.
  **v1 does not auto-download or auto-install.**
- You can also **Check for Updates** manually from the **Settings / About** pages.
- No network, API failure, rate limiting, no release, and already-latest cases are all handled gracefully and never
  interrupt usage.
- Whether to check for updates on startup is configurable in Settings.

## Development Environment

- **.NET 10 SDK** (target framework `net10.0`).
- **Avalonia 11.3**, **CommunityToolkit.Mvvm 8.4**, **Microsoft.Extensions.\* (Hosting / DI / Logging) 10.0**.
- NuGet feed `https://api.nuget.org/v3/index.json` (configured in the repo-root `nuget.config`).
- Recommended IDEs: JetBrains Rider, Visual Studio 2022+, or VS Code with the C# Dev Kit.
- Code style is enforced by the root `.editorconfig` (4-space indent, file-scoped namespaces, `I` interface
  prefix, CRLF line endings).

## Build

From the repository root:

```bash
# Restore and build the whole solution
dotnet build

# Run all unit tests
dotnet test

# Run the desktop app locally
dotnet run --project src/AndroidTreeView.App
```

## Publish & Release

Publish self-contained Windows executables:

```bash
# win-x64
dotnet publish src/AndroidTreeView.App -c Release -r win-x64 --self-contained true

# win-x86
dotnet publish src/AndroidTreeView.App -c Release -r win-x86 --self-contained true
```

- MSI packaging steps are in [docs/packaging.md](docs/packaging.md).
- CI and release workflows will be enabled after the desktop app entry point and packaging path are stable. For now,
  use local `dotnet test` and module-level test results as the source of truth.

## Contributing

Contributions are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) first, and note:

- Keep changes focused and commits clear for easy review.
- Follow strict MVVM, the `.editorconfig` style, and the localization convention (no hardcoded user-facing strings).
- Update docs and tests when behavior or public API changes.

AndroidTreeView is early-stage; the star history gives a simple view of community interest as releases,
compatibility checks, and feedback land over time:

<p align="center">
  <a href="https://starchart.cc/Birditch/AndroidTreeView">
    <img src="https://starchart.cc/Birditch/AndroidTreeView.svg" alt="AndroidTreeView GitHub Stars history" width="640" />
  </a>
</p>

Related docs:
[Issue templates](.github/ISSUE_TEMPLATE) ·
[Pull request template](.github/PULL_REQUEST_TEMPLATE.md) ·
[Security policy](SECURITY.md) ·
[Support guide](SUPPORT.md) ·
[Code of conduct](CODE_OF_CONDUCT.md)

## License

AndroidTreeView is open source under the [MIT License](LICENSE), copyright **Birditch**. You may freely use, copy,
modify, merge, publish, distribute, sublicense, and sell copies of the software, provided the original copyright and
license notices are retained.

Open-source work depends on code, tooling, documentation, build systems, and community feedback. Thanks to the
.NET, Avalonia, Android, and ADB ecosystems, and to everyone who contributes issues, reproduction details, tests,
and pull requests.

Special thanks to [JetBrains](https://www.jetbrains.com/community/opensource/) for supporting open-source projects,
including JetBrains Rider open-source license support.

<p align="center">
  <a href="https://www.jetbrains.com/community/opensource/">
    <img src="https://resources.jetbrains.com/storage/products/company/brand/logos/jb_beam.svg" alt="JetBrains Open Source Support" height="88" />
  </a>
</p>
