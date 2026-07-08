# AndroidTreeView Publishing

Repository: `Birditch/AndroidTreeView`.

Current product version: `1.0.4`.

## Release Policy

Release uploads are x64 ZIP packages only. Do not ask users to download a ZIP and replace files manually; the app updater downloads the ZIP, verifies SHA-256 metadata, extracts it, starts the local update script, replaces the app files, and restarts the product.

Each ZIP must contain:

- published application files
- `release.json`
- the product executable named by `release.json`

ZIP packages without a supported `release.json` are rejected by `UpdateInstaller`.

## Version Alignment

Keep these values identical for every release:

- `src/AndroidTreeView.Core/AppInfo.cs` -> `AppInfo.Version`
- `src/AndroidTreeView.App/AndroidTreeView.App.csproj` -> `Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`
- `src/AndroidTreeView.Mini/AndroidTreeView.Mini.csproj` -> `Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`
- `src/AndroidTreeView.App/app.manifest` -> `assemblyIdentity version`
- `packaging/build-update-zip.ps1` -> default `Version`

For `1.0.4`, release artifacts are named:

```text
AndroidTreeView-1.0.4-x64.zip
AndroidTreeView-1.0.4-x64.zip.sha256
AndroidTreeView-Mini-1.0.4-x64.zip
AndroidTreeView-Mini-1.0.4-x64.zip.sha256
```

## Build Upload ZIP Packages

```powershell
./packaging/build-update-zip.ps1 -Product App -Arch x64
./packaging/build-update-zip.ps1 -Product Mini -Arch x64
```

Each run publishes the selected x64 product, writes `release.json`, creates a ZIP under `artifacts/`, and writes a SHA-256 sidecar.

## Update Channels

The full app and Mini share the same update implementation but use different app keys:

- Full app: `AppInfo.AppUpdateKey` -> `android-tree-view-app`
- Mini: `AppInfo.MiniUpdateKey` -> `android-tree-view-mini`

The update channel must point each product key at its own x64 ZIP:

- `android-tree-view-app` -> `AndroidTreeView-1.0.4-x64.zip`
- `android-tree-view-mini` -> `AndroidTreeView-Mini-1.0.4-x64.zip`

`NekoIndexUpdateService` queries the configured internal update API and compares the returned version with `AppInfo.Version`. `UpdateInstaller` downloads the package, verifies SHA-256 metadata when present, extracts the ZIP, verifies the portable x64 manifest, and starts the automated update script.

## Release Checklist

1. Align all version fields.
2. Run the full verification set:

   ```bash
   dotnet build src/AndroidTreeView.App/AndroidTreeView.App.csproj --no-restore
   dotnet build src/AndroidTreeView.Mini/AndroidTreeView.Mini.csproj --no-restore
   dotnet test AndroidTreeView.sln --no-restore
   ```

3. Build the App x64 upload ZIP.
4. Build the Mini x64 upload ZIP.
5. Upload both ZIPs and checksums to the configured internal update channels.
6. Confirm the full app update flow downloads, applies, and restarts.
7. Confirm Mini auto-update downloads, applies, and restarts.

## GitHub Releases

The in-app updater no longer depends on GitHub Releases. GitHub Releases may still be used for public distribution later, but the automatic update path is the internal NekoIndex channel.
