# AndroidTreeView x64 ZIP Packaging

Packaging files live under `packaging/`.

Current default product version: `1.0.4`.

## Release Output

Release uploads are x64 ZIP packages only:

```text
artifacts/AndroidTreeView-1.0.4-x64.zip
artifacts/AndroidTreeView-1.0.4-x64.zip.sha256
artifacts/AndroidTreeView-Mini-1.0.4-x64.zip
artifacts/AndroidTreeView-Mini-1.0.4-x64.zip.sha256
```

Each ZIP contains the published application files plus `release.json`.

## Files

| File | Purpose |
| --- | --- |
| `build-update-zip.ps1` | Main release script. Publishes App/Mini x64, writes `release.json`, creates upload ZIP, and writes SHA-256 sidecar. |
| `AndroidTreeView.Package.wixproj` | Optional x64 WiX MSI project kept for diagnostics or fallback packaging. |
| `Product.wxs` | Product-parameterized WiX authoring. |
| `build-msi.ps1` | Optional x64 MSI build script. Not used for the current upload flow. |

## Build Upload ZIPs

From the repository root:

```powershell
./packaging/build-update-zip.ps1 -Product App -Arch x64
./packaging/build-update-zip.ps1 -Product Mini -Arch x64
```

Only `x64` is accepted. Passing `x86` is rejected.

The script:

1. runs `dotnet publish` for `win-x64`
2. writes `release.json`
3. compresses the publish folder to `artifacts/`
4. writes `<zip>.sha256`

## release.json

The updater uses `release.json` to distinguish an automated release ZIP from a random loose-file archive:

```json
{
  "packageKind": "portable-x64",
  "product": "App",
  "productName": "AndroidTreeView",
  "appKey": "android-tree-view-app",
  "version": "1.0.4",
  "arch": "x64",
  "executable": "AndroidTreeView.App.exe"
}
```

Mini uses:

```json
{
  "packageKind": "portable-x64",
  "product": "Mini",
  "productName": "AndroidTreeView Mini",
  "appKey": "android-tree-view-mini",
  "version": "1.0.4",
  "arch": "x64",
  "executable": "AndroidTreeView.App.mini.exe"
}
```

`UpdateInstaller` rejects ZIP packages that do not contain a supported manifest and executable.

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
Get-FileHash -Algorithm SHA256 artifacts\AndroidTreeView-1.0.4-x64.zip
Get-FileHash -Algorithm SHA256 artifacts\AndroidTreeView-Mini-1.0.4-x64.zip
```

The sidecar uses `<hash> *<filename>` format for compatibility with `sha256sum -c`.

## Version Sync

Keep these fields aligned:

- `src/AndroidTreeView.Core/AppInfo.cs` -> `AppInfo.Version`
- App csproj version fields
- Mini csproj version fields
- App manifest assembly identity
- `packaging/build-update-zip.ps1` default `Version`

See [publishing.md](./publishing.md) for the release checklist and update-channel requirements.
