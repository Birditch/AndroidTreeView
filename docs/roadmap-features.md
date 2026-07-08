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
- Updates are ZIP-driven. The updater downloads, verifies SHA-256 metadata when present, extracts x64 release ZIPs, and starts the automated apply flow. Unsupported loose-file ZIPs are rejected.
- Packaging is product-aware and can produce first-class App and Mini x64 upload ZIPs.

## Remaining Backlog

### Device Actions

- Keep right-click/card actions universal, non-destructive by default, and no-root-required unless a root-only action is explicitly marked.
- Continue improving state-aware enable/disable logic for reboot, recovery, bootloader, network repair, APK install, FRP/setup-complete helper, captive portal helper, and NTP helper.
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

- Build and smoke-test the x64 upload artifacts before publishing:
  - `AndroidTreeView-<version>-x64.zip`
  - `AndroidTreeView-Mini-<version>-x64.zip`
- Verify update metadata points each product key at the correct ZIP artifact.
- Test App and Mini update flows on a clean Windows machine or VM.

## Verification Note

Close running App or Mini instances before rebuilding their projects, because Windows can lock the generated `.exe` while it is running.
