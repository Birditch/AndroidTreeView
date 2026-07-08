# AndroidTreeView Publishing

Repository: `Birditch/AndroidTreeView`.

Current product version: `1.0.5`.

## Release Policy

Official releases are produced only by GitHub Actions (`.github/workflows/publish.yml`). Do not publish artifacts from a local workstation. Local packaging commands are for validation and diagnostics only.

Release uploads are ZIP packages for Windows x64 and macOS Apple Silicon. Windows ZIPs are portable updater packages. macOS Apple Silicon ZIPs are distribution packages whose top-level item is a `.app` bundle.

Each Windows ZIP must contain:

- published application files
- platform-matched `scrcpy`/`adb`
- `release.json`
- the product executable named by `release.json`

ZIP packages without a supported `release.json` are rejected by `UpdateInstaller`.

Each macOS ZIP must contain one top-level app bundle:

- `AndroidTreeView.app` for the full app
- `AndroidTreeView Mini.app` for Mini

The macOS `.app` bundle contains `Contents/Info.plist`, the product executable under `Contents/MacOS/`, the platform-matched `scrcpy` bundle, and release metadata.

## Version Alignment

Keep these values identical for every release:

- `src/AndroidTreeView.Core/AppInfo.cs` -> `AppInfo.Version`
- `src/AndroidTreeView.App/AndroidTreeView.App.csproj` -> `Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`
- `src/AndroidTreeView.Mini/AndroidTreeView.Mini.csproj` -> `Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`
- `src/AndroidTreeView.Mini.Mac/AndroidTreeView.Mini.Mac.csproj` -> `Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`
- `src/AndroidTreeView.App/app.manifest` -> `assemblyIdentity version`
- `packaging/build-update-zip.ps1` -> default `Version`

For `1.0.5`, release artifacts are named:

```text
AndroidTreeView-1.0.5-win-x64.zip
AndroidTreeView-1.0.5-win-x64.zip.sha256
AndroidTreeView-1.0.5-osx-arm64.zip
AndroidTreeView-1.0.5-osx-arm64.zip.sha256
AndroidTreeView-Mini-1.0.5-win-x64.zip
AndroidTreeView-Mini-1.0.5-win-x64.zip.sha256
AndroidTreeView-Mini-1.0.5-osx-arm64.zip
AndroidTreeView-Mini-1.0.5-osx-arm64.zip.sha256
```

## Build Upload ZIP Packages

Official publication happens through the `Publish` GitHub Actions workflow:

- push a tag named `v<major>.<minor>.<patch>`, for example `v1.0.5`
- or run `workflow_dispatch` and provide the same version string

The workflow validates on Windows, verifies the pinned scrcpy release against the latest upstream release, builds the two Windows portable ZIPs and two macOS `.app` bundle ZIPs, writes SHA-256 sidecars, uploads workflow artifacts, and creates the GitHub Release.

For local validation only:

```powershell
./packaging/build-update-zip.ps1 -Product App -Rid win-x64
./packaging/build-update-zip.ps1 -Product Mini -Rid win-x64
```

macOS artifacts are built by GitHub Actions on `macos-latest`.

## Update Channels

The full app and Mini share the same update implementation but use different app keys:

- Full app: `AppInfo.AppUpdateKey` -> `android-tree-view-app`
- Mini: `AppInfo.MiniUpdateKey` -> `android-tree-view-mini`

The Windows update channel must point each product key at its own Windows ZIP:

- `android-tree-view-app` -> `AndroidTreeView-1.0.5-win-x64.zip`
- `android-tree-view-mini` -> `AndroidTreeView-Mini-1.0.5-win-x64.zip`

`NekoIndexUpdateService` queries the configured internal update API and compares the returned version with `AppInfo.Version`. `UpdateInstaller` downloads the package, verifies SHA-256 metadata when present, extracts the ZIP, verifies the portable Windows x64 manifest, and starts the automated update script.

The internal update API endpoint is:

```text
GET http://192.168.89.71:14000/api/update/{appKey}/latest
Accept: application/json
```

Minimum response contract:

```json
{
  "ok": true,
  "data": {
    "appKey": "android-tree-view-app",
    "title": "AndroidTreeView",
    "version": "1.0.5",
    "zip": {
      "sha256": "<64-character sha256>",
      "downloadUrl": "/api/resources/android-tree-view-app/versions/latest/archive"
    },
    "files": []
  },
  "error": null
}
```

`downloadUrl` may be absolute or relative to `AppInfo.UpdateServerBaseUrl`. The updater requires the response `appKey` to match the configured product channel and the package `release.json` to match the same `appKey`, version, Windows x64 architecture, and executable.

## Internal Update Deployment

Use this flow for the intranet update server:

1. Push a `v1.0.5` tag or run the `Publish` GitHub Actions workflow manually.
2. Download the GitHub Actions-built Windows ZIP and `.sha256` sidecar for each Windows update channel.
3. Upload the ZIPs to the internal server storage.
4. Configure `/api/update/android-tree-view-app/latest` to return `AndroidTreeView-1.0.5-win-x64.zip` metadata.
5. Configure `/api/update/android-tree-view-mini/latest` to return `AndroidTreeView-Mini-1.0.5-win-x64.zip` metadata.
6. Confirm the `sha256` value matches the uploaded ZIP.
7. Use the app "Check for updates" action to test download, validation, replacement, cleanup, and restart.

macOS Apple Silicon ZIPs are published to GitHub Releases as `.app` bundle ZIPs for distribution, but the current automated in-app updater accepts the Windows `portable-x64` package kind.

## Update Cleanup

The portable Windows updater now performs a directory reconciliation after copying the new package:

- files present in the installed directory but absent from the new ZIP are deleted
- config-like files are preserved even when absent from the new ZIP
- empty directories left after file deletion are removed
- package files are copied with `robocopy /E`; the updater does not use `robocopy /MIR`

Preserved config-like files include `.env`, `settings.json`, `appsettings.json`, `appsettings.*.json`, `*.local.json`, `*.user`, and files with `.config`, `.ini`, `.json`, `.yaml`, `.yml`, or `.toml` extensions. Everything else must be present in the new release ZIP or it is treated as obsolete and removed.

## Release Checklist

1. Align all version fields.
2. Run the full verification set:

   ```bash
   dotnet build AndroidTreeView.sln -c Release --no-restore
   dotnet test AndroidTreeView.sln -c Release --no-build
   ```

3. Confirm `tools/verify-scrcpy-latest.ps1` passes.
4. Locally smoke-test at least the Windows App/Mini packaging scripts.
5. Push a `v<version>` tag or run the `Publish` GitHub Actions workflow manually.
6. Confirm the workflow produced all four ZIPs and checksum sidecars.
7. Point configured Windows update channels at the GitHub Actions-built Windows ZIPs only.
8. Confirm each internal update API response returns the correct product key, version, download URL, and SHA-256.
9. Confirm the full app update flow downloads, applies, removes obsolete non-config files, preserves config files, and restarts.
10. Confirm Mini auto-update downloads, applies, removes obsolete non-config files, preserves config files, and restarts.

## GitHub Releases

The `Publish` workflow creates a GitHub Release for each release tag and uploads the four ZIPs plus checksum files. The in-app updater may still consume an internal NekoIndex channel, but that channel must reference artifacts built by GitHub Actions, not local builds.
