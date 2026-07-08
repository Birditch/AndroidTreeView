# AndroidTreeView Requirements Addendum

Current product version: `1.0.4`.

This addendum extends `docs/architecture.md` and reflects the current App + Mini direction.

## Existing Repository

- Preserve `.git`, `.agents`, `.idea`, existing source projects, and user worktree changes.
- Keep the canonical layout under `src/`, `tests/`, `docs/`, `packaging/`, and `build/`.
- Do not duplicate shared code between the full App and Mini when it can live in a shared service or target.

## App And Mini Sharing

- Shared DI registration lives in `AndroidTreeView.Shared.AndroidTreeViewSharedServices`.
- Full App and Mini share:
  - ADB location/environment/device monitor services
  - scrcpy launch logic
  - settings service
  - NekoIndex update checking
  - update downloading/verification/automatic apply flow
  - scrcpy asset distribution through `build/AndroidTreeView.Scrcpy.targets`
- Full App and Mini use different update keys:
  - `android-tree-view-app`
  - `android-tree-view-mini`

## Localization

- Neutral resources are English fallback: `Strings.resx`.
- Simplified Chinese resources are in `Strings.zh-Hans.resx`.
- User-facing App strings must use localization resources.
- Mini may use direct concise bilingual strings because it is a tiny companion window, but visible strings must be readable and not mojibake.

## Update Automation

- `IUpdateService` + `NekoIndexUpdateService` query the internal update API.
- `IUpdateInstaller` + `UpdateInstaller` download packages, verify SHA-256 metadata when present, extract x64 ZIP packages, and start the automated apply flow.
- The user must not be asked to download a ZIP and replace files manually.
- ZIP packages without a supported `release.json` and executable are rejected.
- No internet, API failure, rate limit, invalid metadata, wrong app key, no release, disabled auto-check, and already-latest states map to explicit statuses and never throw to UI.

## UI Requirements

- Main screen uses a responsive card grid, not a tree as the primary UI.
- Device cards show real data only: name/index, manufacturer/model/serial, Android version, state, battery, charging, temperature, cycle count when available, root state, and last refresh.
- Details include Overview, Hardware, Battery, System, Storage, Network, Root, Logcat, and Raw Properties.
- Fastboot devices are represented without pretending Android OS data is available.
- Destructive device actions are not allowed; potentially disruptive actions require confirmation.
- Mirroring is shared and kept consistent between App and Mini.

## Packaging

- Windows target: `win-x64` only.
- Packaging uses WiX v5.
- ZIP package version must match `AppInfo.Version`.
- App and Mini must both have first-class x64 upload ZIPs:
  - `AndroidTreeView-<version>-x64.zip`
  - `AndroidTreeView-Mini-<version>-x64.zip`
- Framework-dependent builds must clearly require the .NET 10 Desktop Runtime.

## Verification

Before calling a release candidate done, run:

```bash
dotnet build src/AndroidTreeView.App/AndroidTreeView.App.csproj --no-restore
dotnet build src/AndroidTreeView.Mini/AndroidTreeView.Mini.csproj --no-restore
dotnet test AndroidTreeView.sln --no-restore
```

Current expected baseline: App build passes, Mini build passes, and 265 tests pass.
