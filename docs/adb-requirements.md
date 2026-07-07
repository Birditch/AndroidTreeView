# ADB 环境要求与配置 (ADB Requirements & Setup)

AndroidTreeView 通过 **ADB（Android Debug Bridge）** 与设备通信。ADB 是 Android SDK **platform-tools** 的一部分。
本文档说明如何在 Windows / macOS / Linux 上安装 platform-tools、开启 USB 调试、完成授权，以及常见问题排错。

> AndroidTreeView 只做**只读**信息展示，所有操作都通过标准 ADB 命令完成，不会修改、刷写或破坏设备。

---

## 目录

- [1. 安装 platform-tools](#1-安装-platform-tools)
  - [1.1 Windows](#11-windows)
  - [1.2 macOS](#12-macos)
  - [1.3 Linux](#13-linux)
- [2. 验证 ADB 是否可用](#2-验证-adb-是否可用)
- [3. 在应用中配置 ADB 路径](#3-在应用中配置-adb-路径)
- [4. 开启 USB 调试](#4-开启-usb-调试)
- [5. 授权本机 (Authorization Prompt)](#5-授权本机-authorization-prompt)
- [6. 常见问题排错 (Troubleshooting)](#6-常见问题排错-troubleshooting)

---

## 1. 安装 platform-tools

官方下载地址（推荐，独立压缩包，无需完整 Android Studio）：
<https://developer.android.com/tools/releases/platform-tools>

### 1.1 Windows

**方式 A：官方压缩包（推荐）**

1. 下载 “SDK Platform-Tools for Windows” 压缩包并解压，例如解压到 `C:\platform-tools`。
2. 将该目录加入系统 `PATH`：
   - 打开 “编辑系统环境变量” → “环境变量” → 在 `Path` 中新增 `C:\platform-tools`。
   - 或用 PowerShell（当前用户，重开终端生效）：

     ```powershell
     [Environment]::SetEnvironmentVariable(
       'Path',
       $env:Path + ';C:\platform-tools',
       'User')
     ```

3. 重新打开终端，运行 `adb version` 验证。

**方式 B：包管理器**

```powershell
winget install --id Google.PlatformTools -e
# 或
choco install adb -y
# 或
scoop install adb
```

**方式 C：已安装 Android Studio**

`adb.exe` 通常位于：`%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe`。
AndroidTreeView 会自动扫描该常见位置。

> Windows 上多数设备需要安装厂商 USB 驱动或通用 “Google USB Driver”，否则设备可能无法被识别。

### 1.2 macOS

```bash
# Homebrew（推荐）
brew install --cask android-platform-tools
```

或使用官方压缩包，解压后把目录加入 `PATH`（在 `~/.zshrc` 或 `~/.bash_profile` 中）：

```bash
echo 'export PATH="$HOME/platform-tools:$PATH"' >> ~/.zshrc
source ~/.zshrc
```

若已安装 Android Studio，`adb` 通常位于 `~/Library/Android/sdk/platform-tools`。

### 1.3 Linux

```bash
# Debian / Ubuntu
sudo apt update && sudo apt install -y android-tools-adb

# Fedora
sudo dnf install -y android-tools

# Arch
sudo pacman -S android-tools
```

或使用官方压缩包，解压后加入 `PATH`：

```bash
echo 'export PATH="$HOME/platform-tools:$PATH"' >> ~/.bashrc
source ~/.bashrc
```

Linux 上通常还需要配置 **udev 规则** 才能以普通用户访问设备：

```bash
# 安装社区维护的 udev 规则（示例）
sudo apt install -y android-sdk-platform-tools-common
# 修改规则后重新加载
sudo udevadm control --reload-rules && sudo udevadm trigger
```

如果设备仍不可见，可将当前用户加入 `plugdev` 组后重新登录：

```bash
sudo usermod -aG plugdev "$USER"
```

---

## 2. 验证 ADB 是否可用

打开终端运行：

```bash
adb version      # 应打印 Android Debug Bridge 版本号
adb devices -l   # 列出已连接设备
```

`adb devices -l` 的每行末尾状态含义：

| 状态 (State)   | 含义 (Meaning) |
| --- | --- |
| `device`       | 已连接且已授权，可用 |
| `unauthorized` | 已连接但**未授权**，需在设备上允许调试 |
| `offline`      | 设备无响应 / 未就绪 |
| `no permissions` | 主机权限不足（多见于 Linux udev 未配置） |
| `recovery` / `sideload` / `bootloader` | 设备处于特殊模式 |

---

## 3. 在应用中配置 ADB 路径

AndroidTreeView 按以下顺序自动查找 `adb`：

1. **设置中手动配置的 ADB 路径**；
2. 系统 `PATH` 中的 `adb` / `adb.exe`；
3. 常见 SDK 位置：
   - Windows：`%LOCALAPPDATA%\Android\Sdk\platform-tools`
   - macOS：`~/Library/Android/sdk/platform-tools`
   - Linux：`~/Android/Sdk/platform-tools`
   - 以及 `ANDROID_HOME` / `ANDROID_SDK_ROOT`、`/usr/local/bin`、`/opt/android-sdk/platform-tools`。

若自动查找失败，应用会显示引导页（Setup）。你可以：

- 安装 platform-tools 并加入 `PATH` 后点击“重试”；或
- 点击“浏览”手动选择 `adb`（Windows 为 `adb.exe`）可执行文件。

---

## 4. 开启 USB 调试

1. 打开手机 **设置 → 关于手机（About phone）**。
2. 连续点击 **版本号（Build number）** 7 次，直到提示“已进入开发者模式 / You are now a developer”。
3. 返回 **设置 → 系统 → 开发者选项（Developer options）**。
4. 打开 **USB 调试（USB debugging）**。
5. 用数据线连接手机与电脑（建议使用原装 / 支持数据传输的线缆，纯充电线无法调试）。

> 不同厂商（小米、华为、OPPO、vivo、三星、摩托罗拉等）与不同 Android 版本的菜单位置略有差异，
> 部分机型的开发者选项中还需额外打开“USB 安装 / USB 调试（安全设置）”等开关，但核心流程一致。

---

## 5. 授权本机 (Authorization Prompt)

- 首次用某台电脑连接设备时，手机会弹出 **“是否允许 USB 调试？”**，并显示该电脑的 RSA 密钥指纹。
- 勾选 **“一律允许使用这台计算机进行调试 / Always allow from this computer”**，然后点击 **允许 / 确定**。
- 授权后，`adb devices` 中该设备状态应从 `unauthorized` 变为 `device`。

如果一直没有弹窗，请见下方排错。

---

## 6. 常见问题排错 (Troubleshooting)

### 设备显示 `unauthorized`（未授权）

1. 确认手机屏幕已解锁，查看是否有授权弹窗，勾选“一律允许”后点“允许”。
2. 若无弹窗：断开数据线 → 在开发者选项中点击 **“撤销 USB 调试授权（Revoke USB debugging authorizations）”** →
   重新插线，等待弹窗。
3. 重置主机 ADB 密钥（会让所有设备重新授权）：

   ```bash
   adb kill-server
   # 删除主机端 adb 密钥（下次连接会重新生成并重新弹出授权）
   # Windows: %USERPROFILE%\.android\adbkey 和 adbkey.pub
   # macOS/Linux: ~/.android/adbkey 和 ~/.android/adbkey.pub
   adb start-server
   adb devices
   ```

4. 关闭并重开 USB 调试开关，或重启手机后重试。

### 设备显示 `offline`（离线）

1. 重启 ADB 服务：

   ```bash
   adb kill-server && adb start-server && adb devices
   ```

2. 更换数据线 / USB 接口（避免使用 USB 集线器，直连主机）。
3. 手机的 **USB 连接模式** 改为 **“文件传输（MTP）”** 或 **“无数据传输”** 之外能触发调试的模式。
4. 确认主机 platform-tools 为较新版本；过旧的 `adb` 可能与新系统握手失败。
5. 若仍离线，重启手机与电脑后重试。

### `adb devices` 列表为空 / 找不到设备

1. 确认数据线支持数据传输（换一根原装线）。
2. Windows：安装厂商 USB 驱动或 Google USB Driver；在“设备管理器”中确认设备被正确识别。
3. Linux：确认已配置 udev 规则，且状态不是 `no permissions`（见 [1.3](#13-linux)）。
4. macOS：确认已通过 Homebrew 或官方包安装 platform-tools。
5. 运行 `adb version` 确认 `adb` 本身可用。

### `no permissions`（Linux）

- 按 [1.3 Linux](#13-linux) 配置 udev 规则并重新加载；将用户加入 `plugdev` 组后重新登录；
  必要时 `sudo adb kill-server && adb start-server`。

### 多台 ADB 版本冲突

- 系统里可能同时存在多个 `adb`（PATH、Android Studio、厂商工具等）。请统一使用同一份较新的 `adb`，
  避免服务端 / 客户端版本不一致导致的连接异常。可用 `adb version` 与 `which adb`（Windows 用 `where adb`）确认当前使用的是哪一个。

### 无线调试（可选）

Android 11+ 支持无线调试。先用 USB 授权，再启用开发者选项中的“无线调试”，然后：

```bash
adb tcpip 5555
adb connect <设备IP>:5555
```

> AndroidTreeView 使用 `adb` 报告的设备列表；只要 `adb devices` 能看到设备，应用即可展示其信息。
