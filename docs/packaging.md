# AndroidTreeView — Windows MSI Packaging

> 中文摘要：本文档说明如何在本机使用 **WiX Toolset v5** 为 AndroidTreeView 构建
> win-x64 与 win-x86 的 MSI 安装包，包含自包含 / 依赖运行时两种模式、.NET 10 桌面运行时
> 检测行为、可选的 Burn 引导安装程序、以及校验和生成方式。所有产物输出到 `artifacts/`。

All packaging lives under [`packaging/`](../packaging) and is intentionally **not** part of
`AndroidTreeView.sln`. Nothing here is built by the normal solution build.

| File | Purpose |
| --- | --- |
| `packaging/AndroidTreeView.Package.wixproj` | WiX v5 SDK-style project that packages one publish output into an MSI. |
| `packaging/Product.wxs` | Package / Feature / Component authoring: files, Start Menu + optional desktop shortcut, icon, upgrade logic, .NET runtime launch condition. |
| `packaging/Bundle.wxs` | *Optional* Burn bootstrapper (`.exe`) that installs the .NET 10 Desktop Runtime if missing, then the MSI. |
| `packaging/build-msi.ps1` | End-to-end: publish → build MSI → checksum → print path. |
| `packaging/build-msi.cmd` | Thin `cmd` wrapper around `build-msi.ps1`. |

Output naming: `artifacts/AndroidTreeView-<version>-<arch>.msi` (e.g.
`AndroidTreeView-1.0.0-x64.msi`) plus a `.sha256` sibling.

---

## 1. Prerequisites

* **.NET 10 SDK** on `PATH` (`dotnet --info`). Required to publish the app and to build the
  MSI (the WiX SDK is an MSBuild SDK restored via NuGet).
* **Internet access to nuget.org** for the first restore. The repo's `nuget.config` already
  adds `https://api.nuget.org/v3/index.json`. The `WixToolset.Sdk/5.0.2` referenced by the
  `.wixproj` is restored automatically the first time you build it.
* **Optional — the global `wix` CLI.** Only needed to build the optional `Bundle.wxs`
  bootstrapper (the MSI itself does not need it):

  ```powershell
  dotnet tool install --global wix --version 5.0.2
  wix --version
  wix extension add -g WixToolset.BootstrapperApplications.wixext
  wix extension add -g WixToolset.Netfx.wixext
  ```

WiX v5 runs cross-platform, but building a Windows MSI is only supported on Windows.

---

## 2. Build the MSIs locally

From the repository root (Windows PowerShell or PowerShell 7):

```powershell
# Framework-dependent (smaller; requires .NET 10 Desktop Runtime on the target machine)
./packaging/build-msi.ps1 -Arch x64
./packaging/build-msi.ps1 -Arch x86

# Self-contained (larger; NO runtime prerequisite)
./packaging/build-msi.ps1 -Arch x64 -SelfContained
./packaging/build-msi.ps1 -Arch x86 -SelfContained
```

Or via the `cmd` wrapper:

```bat
packaging\build-msi.cmd x64
packaging\build-msi.cmd x86 selfcontained
```

What the script does:

1. `dotnet publish src/AndroidTreeView.App -c Release -r win-<arch> --self-contained <bool>`
   into `artifacts/publish/<arch>` (PDBs suppressed).
2. `dotnet build packaging/AndroidTreeView.Package.wixproj` with `-p:Platform=<arch>`,
   `-p:ProductVersion=<version>`, `-p:SelfContained=<bool>`, `-p:PublishDir=<publish folder>`.
3. Copies the MSI to `artifacts/AndroidTreeView-<version>-<arch>.msi`, writes
   `…​.msi.sha256`, and prints both paths.

### Building the wixproj directly (advanced)

`build-msi.ps1` is just orchestration. You can invoke the project yourself after publishing:

```powershell
dotnet build packaging/AndroidTreeView.Package.wixproj -c Release `
    -p:Platform=x64 `
    -p:ProductVersion=1.0.0 `
    -p:SelfContained=false `
    "-p:PublishDir=D:\AndroidTreeView\artifacts\publish\x64\"
```

`PublishDir` **must** be an absolute path **with a trailing `\`** because `Product.wxs`
harvests `$(var.PublishDir)**`.

---

## 3. Self-contained vs framework-dependent

| Mode | Command | Size | Target machine needs .NET runtime? |
| --- | --- | --- | --- |
| Framework-dependent (default) | `build-msi.ps1 -Arch x64` | Small | **Yes** — .NET 10 Desktop Runtime (matching arch) |
| Self-contained | `build-msi.ps1 -Arch x64 -SelfContained` | Large | **No** — runtime is bundled |

Ship **self-contained** for the simplest end-user experience. Ship **framework-dependent**
plus the `Bundle.wxs` bootstrapper (below) if you prefer smaller downloads and are willing to
install the shared runtime.

---

## 4. .NET 10 Desktop Runtime check behavior

There are three layers, and they are complementary:

1. **App host dialog (always, precise).** A framework-dependent `AndroidTreeView.App.exe`
   launched without the matching runtime shows the OS/apphost dialog *"You must install the
   .NET Desktop Runtime 10.0"* with a direct download link. This is the authoritative check.
2. **MSI launch condition (framework-dependent MSIs only, best-effort).** `Product.wxs` adds
   a `<Launch>` condition that reads
   `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\<arch>\sharedhost\Version`. If **no** .NET
   host is present at all, the install is blocked with a message pointing to
   <https://dotnet.microsoft.com/download/dotnet/10.0>. This is a courtesy gate — it does not
   assert the exact 10.0 Desktop version (that is the apphost's job). Bypass it with
   `msiexec /i AndroidTreeView-1.0.0-x64.msi CHECKDOTNET=0`. Self-contained MSIs emit no such
   condition.
3. **Burn bundle (optional, auto-install).** `Bundle.wxs` uses
   `netfx:DotNetCoreSearch` to detect the .NET 10 Desktop Runtime and silently installs it
   from the official installer before chaining the MSI. This is the smoothest option for
   framework-dependent distribution.

---

## 5. Optional: build the runtime-aware bootstrapper (`Bundle.wxs`)

Only needed for framework-dependent distribution where you want the runtime installed
automatically. Requires the global `wix` CLI and extensions from §1, an already-built MSI, and
a locally downloaded runtime installer
(`windowsdesktop-runtime-10.0.x-win-<arch>.exe` from
<https://dotnet.microsoft.com/download/dotnet/10.0>).

```powershell
wix build packaging/Bundle.wxs -arch x64 `
    -d ProductVersion=1.0.0 `
    -d Platform=x64 `
    -d MsiPath=artifacts\AndroidTreeView-1.0.0-x64.msi `
    -d DotNetRuntimeExe=C:\downloads\windowsdesktop-runtime-10.0.0-win-x64.exe `
    -ext WixToolset.BootstrapperApplications.wixext `
    -ext WixToolset.Netfx.wixext `
    -o artifacts\AndroidTreeView-1.0.0-x64-setup.exe
```

The bundle installs the runtime as **permanent** (it is left in place when AndroidTreeView is
uninstalled).

---

## 6. Checksums

`build-msi.ps1` writes `artifacts/AndroidTreeView-<version>-<arch>.msi.sha256` automatically.
To verify or regenerate manually:

```powershell
Get-FileHash -Algorithm SHA256 artifacts\AndroidTreeView-1.0.0-x64.msi
```

The checksum file uses the `<hash> *<filename>` format so it is compatible with
`sha256sum -c` on machines that have coreutils.

---

## 7. Keeping the version in sync

The product version appears in three places and must match for a release:

* `src/AndroidTreeView.Core/AppInfo.cs` → `AppInfo.Version` (`1.0.0`).
* `src/AndroidTreeView.App/*.csproj` → `<Version>` / `<InformationalVersion>` (owned by the
  shell agent).
* `packaging/build-msi.ps1` `-Version` (default `1.0.0`) → flows to the wixproj
  `ProductVersion`.

See [`publishing.md`](./publishing.md) for the release/tag flow.
