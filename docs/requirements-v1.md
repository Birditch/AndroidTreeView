# AndroidTreeView v1.0.0 — Requirements Addendum (binding)

This addendum EXTENDS `docs/architecture.md`. Where they differ, this file wins. Version = **1.0.0**.
Do **not** create a git tag or GitHub Release in this pass.

## A. Existing repo — refine, don't clobber
- Repo already has `.git`, `.agents` (Codex), `.idea` (JetBrains). Preserve them.
- No README/LICENSE/CONTRIBUTING/.github existed before this pass → create fresh.
- Investigate/clean the stray top-level `AndroidTreeView/` directory (should not duplicate the real
  `src/AndroidTreeView.App`). Consolidate into the canonical `src/` + `tests/` layout.

## B. Localization / i18n (NEW, cross-cutting)
- Languages: **Simplified Chinese (zh-Hans, DEFAULT)** and **English (en)**. Extensible for more.
- Resource-based. Do NOT hardcode UI strings in Views/ViewModels.
- Approach: `.resx` satellite resources + `ILocalizationService` (`AndroidTreeView.Core.Interfaces`)
  and impl `LocalizationService` (Infrastructure or App). A XAML markup extension `LocalizeExtension`
  (`{loc:Localize Key}`) resolves keys against the current culture and updates live on language change.
- `AppSettings` gains `AppLanguage Language` (enum `{ System, ChineseSimplified, English }`,
  default `ChineseSimplified`). Language selectable in Settings.
- Resource files: `Strings.resx` (zh-Hans as the neutral/default set OR en neutral + zh-Hans satellite —
  pick one and be consistent; recommended: neutral = en for fallback safety, `Strings.zh-Hans.resx`
  for Chinese, and force default culture to zh-Hans at startup). Keys grouped logically.
- All user-facing text (window title, nav, card labels, badges, settings, errors, empty states) localized.

## C. Settings additions
- `AppLanguage Language` (default ChineseSimplified)
- `bool AutoCheckUpdates` (default true)
- `string? AccentColor` (optional hex; nullable)
- Existing theme modes localized labels: 跟随系统 / 亮色 / 暗色.

## D. Update checker (NEW)
- `IUpdateService` (Core) + `GitHubUpdateService` (Infrastructure) using `HttpClient` against GitHub
  Releases API (`/repos/{owner}/{repo}/releases/latest`). Owner/repo configurable via constants
  (placeholder `AndroidTreeView/AndroidTreeView`).
- Model `UpdateCheckResult` (`AndroidTreeView.Models`): `CurrentVersion`, `LatestVersion`,
  `bool UpdateAvailable`, `string? ReleaseUrl`, `string? ReleaseNotes`, `UpdateCheckStatus Status`
  (enum `{ UpToDate, UpdateAvailable, NoRelease, NetworkError, RateLimited, InvalidData, Disabled }`).
- Semantic version compare (implement a small SemVer comparer; tolerate leading `v`).
- Behavior: never block startup; run async/fire-and-forget after UI shows. Show non-intrusive banner
  when update available + button to open release page (via `IFilePickerService`/OS open-url helper).
  No auto-download/auto-install in v1. Manual "检查更新 / Check for updates" button in Settings/About.
- Handle: no internet, API failure, rate limit, invalid version, no release, already latest — all mapped
  to `UpdateCheckStatus`, never throw to UI.
- App version source: assembly `InformationalVersion` (set `<Version>1.0.0</Version>` in App csproj).

## E. GUI redesign — CARD GRID primary (replaces tree-primary)
- Main screen = **responsive card grid** of connected devices (NOT a tree as the primary view).
- Left sidebar = app navigation (Devices / Settings / About), collapsible. Optional right status panel
  (ADB status, device count, refresh) on wide layout.
- **Device card** shows: device name, device index (#1, #2…), manufacturer, model, serial number,
  Android version, connection status, battery %, charging status, battery temperature, **battery cycle
  count if available (else “Cycle Count: Unavailable” — never fake)**, root status if available, last
  refresh time. Status badges: Online / Offline / Unauthorized / Rooted / Charging / Low Battery.
- Click card → **Device Detail page** (navigation):
  - Header: name, model, serial, connection status, battery %, root status, Refresh + Back buttons.
  - Tabs/side-nav: Overview, Hardware, Battery, System, Storage, Network, Root, Logcat, Raw Properties.
  - Content = clean grouped info cards (no giant unstyled text blobs). Raw Properties = searchable table.
  - Loading = skeleton/loading UI. Errors = clear error cards per section.

## F. Battery cycle count (Models + parser change)
- Add to `BatteryInfo`: `int? CycleCount`. Presence indicated by `CycleCount != null`.
- Parse safely, NO root required: from `dumpsys battery` (`Charge counter`/`Cycle count` when present),
  and/or readable sysfs (`/sys/class/power_supply/battery/cycle_count`) via `adb shell cat` (non-root,
  best-effort). Never destructive. If unavailable → null (UI shows “Unavailable”).
- Root may add optional enhanced data but is never required.

## G. Responsive layout (reaffirmed for card UI)
- Wide (>=1200): left sidebar + card grid (multi-column) + optional right panel.
- Medium (800–1199): collapsible sidebar + 2-column grid; detail page uses tabs.
- Narrow (<800): single-column card list + top nav + Back; details as stacked cards.
- Nothing overflows/clips at any width.

## H. Liquid-glass visual style
- Translucent/acrylic cards where Avalonia supports it (`ExperimentalAcrylicBorder`,
  `TransparencyLevelHint = AcrylicBlur/Mica` on the window), soft borders, subtle shadows (BoxShadow),
  rounded corners (>=8), layered background, smooth hover/selected transitions, light/dark/system.
  Optional accent color. Tasteful + readable, not flashy. Must degrade gracefully where blur
  unsupported (fallback to solid translucent brushes).

## I. Windows MSI packaging (NEW)
- Targets: **win-x64** and **win-x86** (ignore macOS/Linux packaging for v1).
- Use **WiX Toolset v5** (`.wixproj` / `wix` CLI) or equivalent reliable MSI approach.
- MSI installs: app exe + required files + app icon + Start Menu shortcut (+ optional Desktop shortcut).
- MSI must check for the required **.NET 10 Desktop/runtime**; if missing, show a clear message + guide
  the user to install it (bootstrapper/launch condition + doc link). App itself also detects
  runtime/env problems gracefully where practical.
- Provide `build/` scripts and docs for building win-x64 + win-x86 MSIs locally.

## J. GitHub Actions — split workflows
- `.github/workflows/ci.yml` (push + pull_request): checkout, setup .NET 10, restore, format check
  (`dotnet format --verify-no-changes`, non-fatal-tolerant if needed), vulnerability check
  (`dotnet list package --vulnerable --include-transitive`), build, test, optional analysis, upload
  logs/artifacts. Fails on restore/build/test failure, serious vuln, or format (if enabled).
- `.github/workflows/publish.yml` (only on tag `v*`): reuse build/test, build win-x64 + win-x86,
  package MSIs, generate checksums, create GitHub Release, upload MSI + checksums + release notes.
  Must NOT run on normal commits. Do not push a tag now.

## K. Open-source files (create/refine)
- README.md — **primarily Chinese**, with an English section or `README.en.md` link. Sections:
  项目介绍 / 功能特性 / 截图占位 / 安装方式 / ADB 配置说明 / USB 调试开启说明 / Windows 使用说明 /
  MSI 安装说明 / 自动更新说明 / 开发环境 / 编译方式 / 发布方式 / 贡献指南 / 许可证.
- LICENSE (MIT), CONTRIBUTING.md, CODE_OF_CONDUCT.md, SECURITY.md, `.github/ISSUE_TEMPLATE/*`,
  `.github/pull_request_template.md`. `.gitignore`/`.editorconfig` refine existing.

## L. v1.0.0 acceptance (superset — all must hold)
App starts; Chinese default UI; English selectable; theme switch works; ADB detection + missing-ADB
screen; devices shown as cards; online/offline/unauthorized; real basic info + battery % + cycle count
(if available); click card → detail; detail Overview/Battery/Hardware/System/Storage/Root; raw getprop
searchable; basic Logcat; Settings page; update-check service; CI + Publish workflows; MSI packaging
config; refined OSS files; **project builds**; parser unit tests. No fake data for core logic; no
destructive ADB; never modify the device.
