# AndroidTreeView

[![License: MIT](https://img.shields.io/badge/License-MIT-0E7A5F.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Avalonia 11.3](https://img.shields.io/badge/Avalonia-11.3-663399.svg)](https://avaloniaui.net/)
[![Platform: Windows + macOS](https://img.shields.io/badge/Platform-Windows%20%2B%20macOS-0078D6.svg)](#使用说明)

**简体中文** | [English](README.en.md) | [中文副本](README-CN.md)

AndroidTreeView 是一个用于 Android 设备巡检、测试与管理的桌面工具，支持 Windows 与 macOS（Apple Silicon）。主程序负责设备总览、详情、投屏、基础工具、设置和更新；Mini 版本保持独立运行，常驻监听设备，并在授权后自动启动投屏。

当前版本：**v1.0.6**。当前验证目标：App 构建通过、Mini 构建通过、全量测试通过，打包链路能为 App 和 Mini 分别生成 x64 上传 ZIP。

## 产品样式展示

![AndroidTreeView 设备总览](docs/images/product-devices-v1.0.6.png)

## 核心能力

- 设备卡片总览：展示设备名称、序列号、型号、Android 版本、电量、充电状态、温度、循环次数、连接状态、Root 状态和最后刷新时间。
- 详情页：按 Overview、Hardware、Battery、System、Storage、Network、Root、Logcat、Raw Properties 分类查看。
- 共享投屏：主程序和 Mini 共用同一套 scrcpy/ADB 启动逻辑，避免两边实现漂移。
- 投屏窗口支持点击、滑动控制，Back/Home/Recents 按钮，以及拖拽 APK 安装。
- Mini 自动投屏：Mini 常驻监听设备，设备连接并授权后自动启动投屏。
- Mini 使用轻量 WinForms 窗口，不再随 Mini 包携带 Avalonia/Skia 运行时。
- App 和 Mini 共用 ADB、scrcpy、设置、更新检查和更新安装服务。
- 自动更新会下载、校验 SHA-256、解包 x64 ZIP，并启动本地更新脚本完成替换和重启。
- 中英文 UI，支持跟随系统、浅色、深色主题。

## 项目结构

```text
src/
  AndroidTreeView.Models
  AndroidTreeView.Core
  AndroidTreeView.Adb
  AndroidTreeView.Infrastructure
  AndroidTreeView.Shared
  AndroidTreeView.App
  AndroidTreeView.Mini
  AndroidTreeView.Mini.Mac
tests/
  AndroidTreeView.*.Tests
packaging/
  win-x64 / osx-arm64 ZIP packaging and optional WiX MSI packaging
build/
  Shared MSBuild targets
```

## 开发运行

需要 .NET 10 SDK：

```bash
dotnet restore AndroidTreeView.sln
dotnet build AndroidTreeView.sln
dotnet test AndroidTreeView.sln
dotnet run --project src/AndroidTreeView.App
dotnet run --project src/AndroidTreeView.Mini
```

功能规划与实施文档入口见 [docs/roadmap-features.md](docs/roadmap-features.md)。

如果找不到 ADB，主程序会显示 ADB 设置页。请安装 Android platform-tools 并加入 `PATH`，或手动选择 `adb.exe`。

## 开启 USB 调试

1. 打开设置 > 关于手机，连续点击版本号 7 次。
2. 进入开发者选项并开启 USB 调试。
3. 连接设备，并允许 USB 调试授权。
4. 如果设备显示 Unauthorized 或 Offline，请重新授权、检查数据线或重启 ADB。

ADB 安装和排错见 [docs/adb-requirements.md](docs/adb-requirements.md)。

## 使用说明

1. 启动 AndroidTreeView。
2. 连接已开启并授权 USB 调试的 Android 设备。
3. 使用设备卡片查看状态、投屏、打开 CLI 或执行非破坏性工具。
4. 通过设置或关于页检查并安装更新。

### macOS 说明

- 从 Release 下载 `AndroidTreeView-<版本>-osx-arm64.zip`，解压后将 `AndroidTreeView.app` 拖入 `/Applications`。
- 首次打开若被 Gatekeeper 拦截（未签名），右键选择「打开」，或执行 `xattr -dr com.apple.quarantine AndroidTreeView.app` 放行。
- 设备卡片的「CLI 终端」在 macOS 上通过 Terminal.app 打开，提供与 Windows 一致的编号菜单（设备信息 / adb shell / logcat / 重启 / 关机 等）。

## Release ZIP 打包

当前版本号统一为 `1.0.6`，运行时版本、App/Mini 程序集版本、manifest 和 `build-update-zip.ps1` 默认版本保持一致。正式发布只通过 GitHub Actions 的 `Publish` 工作流完成，发布只接受 x64。

本地命令仅用于验证打包链路：

```powershell
./packaging/build-update-zip.ps1 -Product App -Rid win-x64
./packaging/build-update-zip.ps1 -Product Mini -Rid win-x64
```

示例输出：

```text
artifacts/AndroidTreeView-1.0.6-win-x64.zip
artifacts/AndroidTreeView-1.0.6-osx-arm64.zip
artifacts/AndroidTreeView-Mini-1.0.6-win-x64.zip
artifacts/AndroidTreeView-Mini-1.0.6-osx-arm64.zip
```

## 自动更新

- 主程序更新通道：`android-tree-view-app`。
- Mini 更新通道：`android-tree-view-mini`。
- `NekoIndexUpdateService` 检查内部更新通道并比较语义版本。
- `UpdateInstaller` 下载、校验、解包并启动本地更新脚本。
- Windows 自动更新支持带 `release.json` 的 x64 ZIP；GitHub Release 同时包含 macOS Apple Silicon ZIP。
- 没有受支持发布清单的散文件 ZIP 会被拒绝。

## 验证

```bash
dotnet build src/AndroidTreeView.App/AndroidTreeView.App.csproj --no-restore
dotnet build src/AndroidTreeView.Mini/AndroidTreeView.Mini.csproj --no-restore
dotnet build src/AndroidTreeView.Mini.Mac/AndroidTreeView.Mini.Mac.csproj --no-restore
dotnet test AndroidTreeView.sln --no-restore
```

## 许可证

AndroidTreeView 基于 [MIT License](LICENSE) 开源。
