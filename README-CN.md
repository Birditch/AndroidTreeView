# AndroidTreeView

[![License: MIT](https://img.shields.io/badge/License-MIT-0E7A5F.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Avalonia 11.3](https://img.shields.io/badge/Avalonia-11.3-663399.svg)](https://avaloniaui.net/)
[![Platform: Windows + macOS](https://img.shields.io/badge/Platform-Windows%20%2B%20macOS-0078D6.svg)](#使用说明)

[主文档](README.md) | **简体中文** | [English](README.en.md)

AndroidTreeView 是一个用于 Android 设备巡检、测试与管理的桌面工具，支持 Windows 与 macOS（Apple Silicon）。主程序负责设备总览、详情、投屏、基础工具和设置；Mini 版本保持独立运行，常驻监听设备并自动投屏。

当前版本：**v1.0.6**。

## 产品样式展示

![AndroidTreeView 设备总览](docs/images/product-devices-v1.0.6.png)

## 功能

- 设备卡片总览与详情页。
- ADB / Fastboot 信息读取与常用非破坏性操作。
- 主程序投屏窗口支持点击、滑动控制、返回/主页/多任务按键和拖拽 APK 安装。
- Mini 自动监听设备，设备授权后自动启动投屏。
- Mini 使用轻量 WinForms 窗口，不再随 Mini 包携带 Avalonia/Skia 运行时。
- App 和 Mini 共用 ADB、scrcpy、设置、更新检查和更新安装服务。
- Windows 自动更新会下载、校验并应用 x64 ZIP 更新包；GitHub Release 同时包含 macOS Apple Silicon ZIP。
- 中英文 UI，浅色/深色/跟随系统主题。

## 开发运行

```bash
dotnet restore AndroidTreeView.sln
dotnet build AndroidTreeView.sln
dotnet test AndroidTreeView.sln
dotnet run --project src/AndroidTreeView.App
dotnet run --project src/AndroidTreeView.Mini
```

功能规划与实施文档入口见 [docs/roadmap-features.md](docs/roadmap-features.md)。

## 使用说明

1. 安装 Android platform-tools，或让应用在启动时引导选择 `adb.exe`。
2. 在手机上开启开发者选项和 USB 调试。
3. 连接手机并允许 USB 调试授权。
4. 主程序会显示设备卡片；Mini 会自动监听并启动投屏。

### macOS 说明

- 从 Release 下载 `AndroidTreeView-<版本>-osx-arm64.zip`，解压后将 `AndroidTreeView.app` 拖入 `/Applications`。
- 首次打开若被 Gatekeeper 拦截，右键「打开」，或执行 `xattr -dr com.apple.quarantine AndroidTreeView.app` 放行。
- 设备卡片的「CLI 终端」在 macOS 上通过 Terminal.app 打开，编号菜单与 Windows 一致。

ADB 安装与排错见 [docs/adb-requirements.md](docs/adb-requirements.md)。

## 自动更新

- 主程序更新通道：`android-tree-view-app`。
- Mini 更新通道：`android-tree-view-mini`。
- Windows 更新包使用带 `release.json` 的 x64 ZIP；GitHub Release 同时包含 macOS Apple Silicon ZIP。
- ZIP 中没有受支持的发布清单时会拒绝安装，避免用户手动替换文件。

## 打包

正式发布只通过 GitHub Actions 的 `Publish` 工作流完成。本地命令仅用于验证打包链路：

```powershell
./packaging/build-update-zip.ps1 -Product App -Rid win-x64
./packaging/build-update-zip.ps1 -Product Mini -Rid win-x64
```

默认产物版本是 `1.0.6`。只发布 x64 架构。验证输出包含主程序和 Mini 的上传 ZIP，例如：

```text
artifacts/AndroidTreeView-1.0.6-win-x64.zip
artifacts/AndroidTreeView-1.0.6-osx-arm64.zip
artifacts/AndroidTreeView-Mini-1.0.6-win-x64.zip
artifacts/AndroidTreeView-Mini-1.0.6-osx-arm64.zip
```

更多细节见 [docs/packaging.md](docs/packaging.md)。

## 验证

```bash
dotnet build src/AndroidTreeView.App/AndroidTreeView.App.csproj --no-restore
dotnet build src/AndroidTreeView.Mini/AndroidTreeView.Mini.csproj --no-restore
dotnet build src/AndroidTreeView.Mini.Mac/AndroidTreeView.Mini.Mac.csproj --no-restore
dotnet test AndroidTreeView.sln --no-restore
```

## 许可证

AndroidTreeView 基于 [MIT License](LICENSE) 开源。
