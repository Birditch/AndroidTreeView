# AndroidTreeView Feature Roadmap

This roadmap captures the current App + Mini direction after the shared service and installer work.

## Completed Direction

- App and Mini share ADB location, environment setup, device monitoring, settings, scrcpy launch, update checks, and update installation through `AndroidTreeView.Shared`.
- App and Mini both import `build/AndroidTreeView.Scrcpy.targets`, so scrcpy assets are distributed by one build target.
- The full App owns rich device management UI, details, card actions, and the mirror window.
- Mini owns the tiny always-on companion UI and automatically starts mirroring after a device is online and authorized.
- Update checks are product-aware:
  - App uses `android-tree-view-app`.
  - Mini uses `android-tree-view-mini`.
- Updates are ZIP-driven. The updater downloads, verifies SHA-256 metadata when present, extracts Windows x64 release ZIPs, and starts the automated apply flow. Unsupported loose-file ZIPs are rejected.
- Packaging is product-aware and can produce first-class App and Mini ZIPs for Windows x64 and macOS Apple Silicon.
- The full App includes a semi-automatic Root wizard with safe package extraction, original-image backup,
  pinned official Magisk components, ADB-to-fastboot identity continuity, two confirmation gates, and
  single-slot/A-B partial-write reporting. Mini remains Root-tool-free.

## Remaining Backlog

### Root Wizard Release Validation

- Design: [`superpowers/specs/2026-07-09-semi-auto-root-design.md`](superpowers/specs/2026-07-09-semi-auto-root-design.md)
- Implementation plan: [`superpowers/plans/2026-07-10-semi-auto-root-implementation.md`](superpowers/plans/2026-07-10-semi-auto-root-implementation.md)
- Complete M0 on expendable devices for both `boot` and Android 13+ `init_boot` patching.
- Complete M1 on recoverable single-slot and A/B devices, including second-slot failure recovery.
- Validate recovery-only blocking against representative standard v0-v4 images and document any OEM-wrapped
  boot image formats; unrecognized formats remain blocked rather than guessed.
- Smoke-test pinned payload-dumper and packaged executable permissions on Windows x64 and macOS arm64.
- Remove the default flash gate only after those records are accepted; until then the environment override is
  restricted to recoverable M1 test devices.

### Device Actions

- Keep right-click/card actions universal, non-destructive by default, and no-root-required unless a root-only action is explicitly marked.
- Continue improving state-aware enable/disable logic for reboot, recovery, bootloader, Magisk install, APK install, FRP/setup-complete helper, captive portal helper, and NTP helper.
- Add clear confirmations for disruptive actions such as reboot, shutdown, recovery, and bootloader mode.

### Batch Operations

- Add multi-select on the device grid.
- Add a batch action bar for safe actions across selected devices.
- Run per-device actions concurrently and report per-device failures without aborting the whole batch.

### Fastboot Support

- Merge `fastboot devices` into the device list with a distinct bootloader state.
- Parse `fastboot getvar all` into a fastboot info model.
- Add fastboot-safe actions such as reboot system and reboot bootloader.

### Richer Detail Pages

- Add more network fields such as Wi-Fi SSID, gateway, DNS, link speed, and interface details where available.
- Surface additional system fields such as bootloader, verified boot, patch level, uptime, timezone, locale, build tags, and build type.

### Release Hardening

- Build and smoke-test the release artifacts before publishing:
  - `AndroidTreeView-<version>-win-x64.zip`
  - `AndroidTreeView-<version>-osx-arm64.zip`
  - `AndroidTreeView-Mini-<version>-win-x64.zip`
  - `AndroidTreeView-Mini-<version>-osx-arm64.zip`
- Verify update metadata points each product key at the correct ZIP artifact.
- Test App and Mini update flows on a clean Windows machine or VM.

## Verification Note

Close running App or Mini instances before rebuilding their projects, because Windows can lock the generated `.exe` while it is running.
