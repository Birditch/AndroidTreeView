# AndroidTreeView ZIP Packaging

Packaging files live under `packaging/`.

Current default product version: `1.0.7`.

Official release artifacts are created by GitHub Actions (`.github/workflows/publish.yml`) only. Local packaging commands are validation helpers and must not be used as the authority for publishing a release.

## Release Output

Every GitHub Release must contain exactly these four ZIP packages plus matching `.sha256` sidecars:

```text
artifacts/AndroidTreeView-1.0.7-win-x64.zip
artifacts/AndroidTreeView-1.0.7-win-x64.zip.sha256
artifacts/AndroidTreeView-1.0.7-osx-arm64.zip
artifacts/AndroidTreeView-1.0.7-osx-arm64.zip.sha256
artifacts/AndroidTreeView-Mini-1.0.7-win-x64.zip
artifacts/AndroidTreeView-Mini-1.0.7-win-x64.zip.sha256
artifacts/AndroidTreeView-Mini-1.0.7-osx-arm64.zip
artifacts/AndroidTreeView-Mini-1.0.7-osx-arm64.zip.sha256
```

Windows ZIPs contain the published application files, the platform-matched `scrcpy` bundle, and `release.json` at the ZIP root. Full App ZIPs additionally contain verified `root-tools/` assets; Mini ZIPs must not. macOS ZIPs contain a top-level `.app` bundle (`AndroidTreeView.app` or `AndroidTreeView Mini.app`); the published files, tool bundles, and `release.json` live inside the bundle.

The Windows updater treats the ZIP as the source of truth for application files. During an update, files that exist in the installed directory but are missing from the new ZIP are removed unless they are config-like files such as `.env`, `settings.json`, `appsettings.*.json`, `*.local.json`, `*.user`, `.config`, `.ini`, `.json`, `.yaml`, `.yml`, or `.toml`.

## Files

| File | Purpose |
| --- | --- |
| `build-update-zip.ps1` | GitHub Actions packaging helper. Publishes App/Mini for `win-x64` or `osx-arm64`, writes `release.json`, creates Windows portable ZIPs or macOS `.app` bundle ZIPs, and writes SHA-256 sidecar. |
| `AndroidTreeView.Package.wixproj` | Optional x64 WiX MSI project kept for diagnostics or fallback Windows packaging. |
| `Product.wxs` | Product-parameterized WiX authoring. |
| `build-msi.ps1` | Optional x64 MSI build script. Not used for the current upload flow. |

## Build Upload ZIPs

For local validation from the repository root:

```powershell
./packaging/build-update-zip.ps1 -Product App -Rid win-x64
./packaging/build-update-zip.ps1 -Product Mini -Rid win-x64
```

The GitHub Actions workflow additionally runs the same script on macOS:

```powershell
./packaging/build-update-zip.ps1 -Product App -Rid osx-arm64
./packaging/build-update-zip.ps1 -Product Mini -Rid osx-arm64
```

Supported release RIDs are `win-x64` and `osx-arm64`.

The script:

1. downloads the matching upstream scrcpy asset (`scrcpy-win64-v4.1.zip` or `scrcpy-macos-aarch64-v4.1.tar.gz`)
2. folds hash-verified Android SDK Platform-Tools 37.0.0 `fastboot` into the full App package
3. for the full App only, downloads and verifies Magisk v30.7 and payload-dumper-go 1.3.0
4. runs `dotnet publish`
5. writes `release.json`
6. stages a macOS `.app` bundle for `osx-arm64`
7. verifies App Root-tool presence and Mini Root-tool absence
8. compresses the package folder to `artifacts/`
9. writes `<zip>.sha256`

Root assets use fixed SHA-256 values in `build/AndroidTreeView.RootTools.targets` and
`packaging/build-update-zip.ps1`. `tools/verify-roottools-latest.ps1` checks the pinned upstream release
metadata and payload checksum manifest without downloading the large assets.

macOS ZIPs are created with the system `zip` command so executable bits and `.app` bundle layout are preserved.

## release.json

The updater uses `release.json` to distinguish an automated release ZIP from a random loose-file archive:

```json
{
  "packageKind": "portable-x64",
  "product": "App",
  "productName": "AndroidTreeView",
  "appKey": "android-tree-view-app",
  "version": "1.0.7",
  "platform": "win",
  "arch": "x64",
  "rid": "win-x64",
  "executable": "AndroidTreeView.App.exe"
}
```

macOS packages use `packageKind` values such as `portable-osx-arm64` and executable names without `.exe`; this metadata is stored inside the `.app` bundle for release auditing. The current automated updater accepts the Windows `portable-x64` package kind; macOS ZIPs are GitHub Release `.app` artifacts.

For Windows update packages, `release.json` and the executable named by `executable` must be present in the ZIP. The updater rejects packages with the wrong `appKey`, wrong expected version, non-x64 architecture, missing executable, or unsupported package kind.

## Optional MSI

MSI packaging is no longer the release upload path, but the x64 WiX project remains available:

```powershell
./packaging/build-msi.ps1 -Product App -Arch x64
./packaging/build-msi.ps1 -Product Mini -Arch x64
```

The WiX project rejects non-x64 platforms.

## Checksums

`build-update-zip.ps1` writes checksums automatically. Manual verification:

```powershell
Get-FileHash -Algorithm SHA256 artifacts\AndroidTreeView-1.0.7-win-x64.zip
Get-FileHash -Algorithm SHA256 artifacts\AndroidTreeView-Mini-1.0.7-win-x64.zip
```

The sidecar uses `<hash> *<filename>` format for compatibility with `sha256sum -c`.

## Version Sync

Keep these fields aligned:

- `src/AndroidTreeView.Core/AppInfo.cs` -> `AppInfo.Version`
- App csproj version fields
- Windows Mini csproj version fields
- macOS Mini csproj version fields
- App manifest assembly identity
- `packaging/build-update-zip.ps1` default `Version`

See [publishing.md](./publishing.md) for the release checklist and update-channel requirements.
