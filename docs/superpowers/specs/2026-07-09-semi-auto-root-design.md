# 半自动 Root 功能 — 设计规格

- 日期：2026-07-09
- 状态：已通过设计评审，待用户审阅规格文档
- 关联记忆：`root-feature-relaxes-safety`

## 1. 目标与范围

为 AndroidTreeView 增加一个**半自动 Root 功能**：用户手动上传刷机包，工具自动提取
`boot.img`、用官方 Magisk 组件修补、引导进入 bootloader、把修补后的 `boot.img` 刷入设备。
以**分步向导**形态交付，写操作前强制确认，刷入前自动备份原始 `boot.img`。

### 1.1 第一版明确要做

- 上传刷机包（本地文件选择）。
- 提取 `boot.img`：
  - zip 内含裸 `boot.img`（含 Pixel factory 的嵌套 `image-*.zip`）。
  - `payload.bin`（A/B OTA），通过打包的 `payload_dumper` 解出。
- Magisk 修补：工具**构建时打包固定版本官方 Magisk APK**（SHA-256 校验），运行时
  `adb install` 到手机，调用**已装 Magisk App 自带的** `boot_patch.sh` + native 组件在手机端
  完成修补，pull 回修补后镜像。
- 写操作链路：`adb push` → `adb reboot bootloader` → **flash boot（A/B 机型两个槽都刷）** →
  `fastboot reboot`。
  - **非 A/B 机型**：`fastboot flash boot <img>`。
  - **A/B 机型**：`fastboot flash boot_a <img>` **且** `fastboot flash boot_b <img>`
    （两槽都刷入同一修补后镜像，避免槽位翻转/刷错槽导致 Magisk 不生效）。
  - 刷入前**强制提示用户**双槽都会被写入及其风险（见 §5.3 确认 2、§3 已知风险）。
- 只读检测：`fastboot getvar unlocked`（解锁状态）、**A/B 布局探测
  （`fastboot getvar slot-count`）**、设备 ABI 探测。
- **刷入前自动备份原始未修补 `boot.img`** 到本地用户目录。
- 平台：**Windows + macOS 双平台**，共用同一套 MVVM 代码。

### 1.2 第一版明确不做（后续再议）

- `fastboot flashing unlock`（解锁 bootloader，会抹数据）——**仅检测状态 + 文字引导，不代执行**。
- 刷其他分区（system / vbmeta / recovery / super 等）。
- 卸载 root / 一键恢复原厂 boot（仅提供备份文件与引导，不做自动回刷向导）。
- 厂商私有包格式（`.ozip`、动态分区 super.img 拆分等）。

## 2. 方向性决策（已与用户确认）

| 决策 | 选择 | 理由 |
|---|---|---|
| 与"只读/安全"定位冲突 | **放宽定位**，接受工具含刷机写操作 | 用户明确要 fastboot 刷入的半自动 root |
| 支持平台 | **Windows + macOS 双平台** | 两平台都要能真正刷机 |
| Magisk 修补执行位置 | **手机端执行官方 magiskboot/组件** | 官方 Android 二进制天然可跑，比桌面第三方二进制可靠 |
| Magisk 组件获取 | **构建时打包固定版 Magisk APK，运行时 `adb install`，用已装 App 自带组件修补** | APK 内即含各 ABI native 组件；装 App 后 `boot_patch.sh` 环境完整，比裸跑 `/data/local/tmp` 坑少 |
| 自动化程度 | **分步向导 + 写操作前强制确认** | 变砖风险高，不做无人值守一键刷 |
| 包格式范围 | **zip(含 Pixel 嵌套) + payload.bin** | 覆盖 factory image 与现代 A/B OTA |
| A/B 机型刷入策略 | **两个槽 `boot_a`/`boot_b` 都刷同一镜像 + 刷入前强制提示** | 免探测 active slot、免刷错槽；代价（跨版本双槽变砖）以提示告知用户承担 |
| 工具打包 | **构建时按平台下载打包进 `tools/`** | 对齐现有 scrcpy/adb 打包，开箱即用 |
| UI 落点 | **App 新导航页，扩展 App 到 macOS** | Avalonia 本就跨平台，双平台共用 MVVM |
| 修补第 4 步实现 | **安装官方 Magisk APK 后跑其自带 `boot_patch.sh`** | 最贴近官方；实机验证列为高风险里程碑 |

## 3. 可行性结论

核心链路技术上全部可实现，跨平台可行。真正的风险**不在技术**，而在两个工具无法根除、
必须由用户承担的前提：

- **前提 1 — bootloader 已解锁**：未解锁则 `fastboot flash` 必然失败。工具只检测并引导，
  不自动解锁（解锁抹数据、需在手机上按键、部分厂商需解锁码）。
- **前提 2 — 包与设备匹配**：刷错版本/架构可能变砖。工具做基本校验与警告，但无法保证包正确。
- **前提 3 — A/B 双槽刷入的跨版本风险（已知、以提示承担）**：A/B 机型两个槽都刷入同一
  修补后镜像。若两个槽当前系统版本不一致（如 OTA 后未满一个周期），把当前版本的 boot 刷进
  另一槽会造成 boot 与该槽 system/vendor 版本错配，**日后回滚到该槽可能 bootloop**（延迟变砖，
  刷入当下无异常）。工具**无法可靠检测两槽版本**，故在**确认 2 强制提示**用户此风险，由用户
  在知情后承担（对应 §2 决策：省掉刷错槽 → 接受此代价）。

设计上以**分步向导 + 强制确认 + 自动备份原始 boot** 将风险降到可控。

**最高技术不确定性**：Magisk 修补的第 4 步（执行 `boot_patch.sh`）不是单条命令。现方案先
`adb install` 官方 Magisk APK，再调用其自带的 `boot_patch.sh` + native 组件——装 App 后
`boot_patch.sh` 环境（`MAGISKBIN`/APK 资源）更完整，比裸跑 `/data/local/tmp` 可靠，但
**"已装 App 的 `boot_patch.sh` 能否被 `adb shell` 直接调到"仍需实机验证**（assets 内脚本安装后
不一定落在可直接执行位置，可能仍需从 APK 提取），**必须实机验证**（见 §8 里程碑）。

## 4. 分层与工程落点

严格遵循项目分层（下层不引用上层）：

```
Models   FlashPackage, BootImageInfo(Path, Source, OriginalPackageName),
         RootWizardState(enum), DeviceFastbootStatus, PackageType(enum)
  ↑
Core     接口：IBootImageExtractor, IMagiskPatcher, IFastbootService, IRootWizardService
         IFastbootEnvironment（对齐 IAdbEnvironment），RootException 类型
  ↑
Adb / Infrastructure
         Adb：FastbootService(ProcessRunner)、FastbootLocator、
              BootImageExtractor(zip + payload.bin)、MagiskPatcher(install APK + 手机端执行)、
              Parsers：FastbootVarParser、PackageTypeDetector、CpuAbiParser
         Infra：BootBackupService（备份原始 boot 到用户目录）
  ↑
Shared   AddAndroidTreeViewSharedServices() 内 TryAdd 注册全部新服务
  ↑
App      RootWizardViewModel + RootWizardView（新导航页，分步向导）
```

- 所有 fastboot/adb 写操作走 `ProcessRunner`（异步、可取消、杀进程树）。
- 解析类为纯函数放 `AndroidTreeView.Adb.Parsers`，配套解析测试（项目硬规则）。
- 工具二进制通过新建 `build/AndroidTreeView.RootTools.targets` 按平台下载打包进 `tools/`。

## 5. 向导状态机与流程

### 5.1 状态枚举 `RootWizardState`

```
Idle                        初始
PackageSelected             已选刷机包
Extracting                  提取 boot.img 中（自动）
BootExtracted               已提取（含来源：PlainZip / NestedZip / Payload）
Patching                    修补中（adb install Magisk APK → 手机执行 boot_patch.sh → pull 回）
BootPatched                 已得修补后 boot + 已本地备份原始 boot
AwaitingBootloaderConfirm   ⛔ 强制确认点 1：即将重启进 bootloader
RebootingToBootloader       adb reboot bootloader（自动）
InFastboot                  已进 fastboot，检测解锁状态 + A/B 布局（slot-count）
Blocked_Locked              未解锁：引导用户，终止自动流程（可重新检测）
AwaitingFlashConfirm        ⛔ 强制确认点 2：即将 flash boot（显示目标槽位；A/B 明确告知双槽都刷）
Flashing                    fastboot flash boot（A/B：先 boot_a 再 boot_b，自动）
Rebooting                   fastboot reboot（自动）
Completed                   完成
Failed                      任一步失败（带错误信息 + 重试当前步 / 中止）
```

### 5.2 Happy Path 时序

```
选包 → [自动]提取 → [自动]修补+备份 → ⛔确认1 → [自动]进bootloader
     → 检测解锁 → ⛔确认2 → [自动]flash → [自动]reboot → 完成
```

### 5.3 两个强制确认点

1. **确认 1（进 bootloader 前）**：提示手机将重启进 fastboot、保持数据线稳定。
2. **确认 2（flash 前）**：显示目标分区 `boot`、修补后镜像、**原始 boot 备份路径**、
   **目标槽位（A/B 机型明确显示"将同时刷入 boot_a 和 boot_b"）**、红色"刷错可能变砖"警告；
   **A/B 机型额外红字提示**：两个槽都会被写入同一镜像，若两槽系统版本不一致，日后回滚到另一槽
   可能变砖（§3 前提 3）；用户勾选"我已了解风险"后方可点"刷入"。

### 5.4 关键分支

- **Blocked_Locked（未解锁）**：向导停止，显示解锁引导（开发者选项 OEM 解锁、
  `fastboot flashing unlock` 会抹数据、部分厂商需解锁码）。**工具不代执行解锁**。
  用户自行解锁后可"重新检测"继续。
- **Failed（任一步失败）**：显示该步 stderr/错误摘要（映射成友好文案），提供"重试当前步"或
  "中止"。中止不留危险中间态（若已在 fastboot，引导用户 `fastboot reboot` 回系统）。
- **可取消**：每个自动步走 `CancellationToken`，用户可随时取消（取消后进入 Failed/可重试）。

## 6. 核心机制

### 6.1 boot.img 提取（`BootImageExtractor`）

输入包文件，按类型分派，输出 `boot.img` 本地路径 + 来源标记
（`BootImageInfo { Path, Source, OriginalPackageName }`）。

**包类型判定**（纯函数 `PackageTypeDetector`，读文件头/枚举 zip 条目）：

- zip 顶层有 `boot.img` → **PlainZip**
- zip 含 `image-*.zip`（Pixel factory）→ **NestedZip**（解一层内层 zip 再取 `boot.img`）
- zip 含 `payload.bin` → **Payload**
- 裸 `payload.bin` 文件 → **Payload**
- 都不匹配 → 抛 `RootException`，友好提示"未在包内找到 boot.img"

**提取**：

- PlainZip / NestedZip → 纯 C# `System.IO.Compression`，无外部依赖。
- Payload → 调用打包的 `payload_dumper`（走 `ProcessRunner`）解出 `boot` 分区。
- 输出到工作区 `~/.androidtreeview/root-work/<timestamp>/`。

**可测试性**：`PackageTypeDetector` 纯函数（zip 条目列表/文件头样本做测试）；payload 解包用假
`ProcessRunner` 测试流程/错误。

### 6.2 Magisk 修补（`MagiskPatcher`，安装官方 APK 后手机端执行）

前提：设备已连接、adb 已授权（复用现有 `DeviceMonitor` / 设备状态）。

1. **安装 Magisk App**（`EnsureMagiskInstalledAsync`）：`adb install -r tools/magisk/Magisk.apk`
   （`-r` 覆盖重装，兼容用户已装旧版）。APK 内即含各 ABI native 组件（`lib/<abi>/lib*.so`）。
   装 App **不设新强制确认点**，仅在步骤条/日志告知"正在安装 Magisk App"（loc key
   `root.step.install_magisk`）。install 失败进 `Failed`。
2. **探测 ABI**：`adb shell getprop ro.product.cpu.abi`（纯函数 `CpuAbiParser`），用于定位已装
   Magisk App 的 native 组件路径（`lib/<abi>`）。
3. **本地备份原始 boot.img**（`BootBackupService`）：复制到
   `~/.androidtreeview/root-backups/<serial>-<timestamp>-boot-original.img`，路径供确认 2 显示。
4. **修补**：push 待修补 `boot.img` 到手机临时目录 `/data/local/tmp/atv_root/`，`adb shell` 调用
   **已装 Magisk App 自带的** `boot_patch.sh`（其可自解析 `MAGISKBIN`/APK 资源，内部调用
   `magiskboot unpack/cpio/repack` + `magiskinit`/`magisk`），产出 `new-boot.img`。
5. **pull 回**：`adb pull .../new-boot.img <workdir>/boot-patched.img`。
6. **清理**手机临时目录。

> ⚠️ 第 4 步为最高技术不确定性环节：装 App 后 `boot_patch.sh` 环境更完整，但**"已装 App 的
> `boot_patch.sh` 能否被 `adb shell` 直接调到"仍未完全消除不确定性**（APK 内 `assets` 脚本安装后
> 不一定落在可直接执行位置，可能仍需从 APK 提取脚本再 push），需实机验证（见 §8）。

### 6.3 fastboot 服务（`FastbootService`，走 `ProcessRunner`，全部 async + CancellationToken）

| 方法 | 命令 | 说明 |
|---|---|---|
| `RebootToBootloaderAsync` | `adb reboot bootloader` | 从系统进 fastboot |
| `WaitForFastbootDeviceAsync` | `fastboot devices` 轮询 | 等设备在 fastboot 出现（带超时） |
| `GetUnlockStatusAsync` | `fastboot getvar unlocked` | 只读，`FastbootVarParser` 解析 `unlocked: yes/no` |
| `GetSlotCountAsync` | `fastboot getvar slot-count` | 只读，`FastbootVarParser` 解析槽位数；`>1` 视为 A/B |
| `FlashBootAsync` | 非 A/B：`fastboot flash boot <img>`；A/B：`fastboot flash boot_a <img>` **且** `fastboot flash boot_b <img>` | 核心写操作；A/B 两槽都刷同一镜像 |
| `RebootAsync` | `fastboot reboot` | 刷完回系统 |

解析 `getvar` / `fastboot devices` 输出为纯函数 + 解析测试。`slot-count` 复用同一
`FastbootVarParser`（解析 `slot-count: N`）。A/B 双槽刷入时，`boot_a` 成功、`boot_b` 失败要
视为整体失败（进 `Failed`，错误摘要注明哪个槽失败），不可停在"只刷了一个槽"的中间态。

### 6.4 fastboot 定位（`FastbootLocator` + `IFastbootEnvironment`）

对齐 `AdbLocator` / `IAdbEnvironment`：配置路径 → 打包
`tools/platform-tools/<rid>/fastboot` → PATH → 常见 SDK 位置。单例持有当前路径。缺失时向导页
显示"未找到 fastboot"引导（类比 App 现有 adb 缺失 Setup 页），不崩溃。

### 6.5 备份服务（`BootBackupService`，Infrastructure 层）

- 修补前把原始 boot.img 存到 `~/.androidtreeview/root-backups/`，文件名含序列号 + 时间戳。
- 提供"列出/打开备份目录"给 UI，便于变砖时找回。

## 7. 工具打包 — `build/AndroidTreeView.RootTools.targets`

仿 `AndroidTreeView.Scrcpy.targets`，构建/发布时按平台下载并打包：

```
tools/
  platform-tools/
    win-x64/    fastboot.exe (+ adb.exe)
    osx-x64/    fastboot
    osx-arm64/  fastboot
  payload-dumper/
    win-x64/  osx-x64/  osx-arm64/    payload_dumper
  magisk/
    Magisk.apk        固定版本官方 APK（内含各 ABI native 组件），运行时 adb install
```

- **需支持 macOS 平台产出**（现有 scrcpy 仅 Windows 打包）。
- 下载源写明 URL + SHA-256 校验，文档登记版本，对齐项目"下载工具需校验 SHA-256"风格：
  - platform-tools：Google 官方 dl 链接。
  - magisk：官方 Magisk GitHub release 的 APK 整包（不在桌面侧拆 native 二进制，组件随 App
    安装到手机后使用）。
  - payload_dumper：可信开源发布。
- 新增 `tools/verify-roottools-latest.ps1`（仿 `verify-scrcpy-latest.ps1`）检查上游新版本。

## 8. 高风险里程碑（实机验证）

以下无法单测，须实机验证并在开发中预留调整轮次：

1. **`boot_patch.sh` 修补链路** — `adb install` 官方 Magisk APK 后，能调用其自带
   `boot_patch.sh` 在真机产出可用 `new-boot.img`（重点验证已装 App 的脚本是否可被 `adb shell`
   直接调到）。
2. **真实 flash** — `fastboot flash boot` 在已解锁真机上成功且能正常开机。
3. **payload.bin 解包** — `payload_dumper` 在三种 RID 上均能正确解出 boot 分区。
4. **跨平台 fastboot/adb** — Windows 与 macOS(x64/arm64) 下二进制均能定位与执行。

## 9. App UI

- 新导航页，命名遵循 ViewLocator 约定（`RootWizardViewModel` → `RootWizardView`），严格 MVVM
  （`[ObservableProperty]` / `[RelayCommand]`），编译绑定 `x:DataType`。
- 侧栏新增导航项（新 loc key `nav.root`）。页面：顶部步骤条反映 `RootWizardState`，中间当前步
  内容/进度/日志，底部操作按钮（下一步/确认/取消/重试）。
- 两个强制确认为醒目对话框 + 复选框（未勾选"我已了解风险"则"刷入"禁用）。
- flash 步骤用红色警告样式，与只读功能区分。
- `RootWizardViewModel` 仅编排 `IRootWizardService`，负责状态→UI 映射、UI 线程 marshal
  （`Dispatcher.UIThread.Post`）、错误映射成友好 `ErrorMessage`（App 永不因正常错误崩溃）。
- macOS 支持：让 App 能在 macOS 构建/运行，Root 页双平台共用同一 MVVM 代码。

## 10. 本地化

- 所有向导文案、按钮、警告、错误映射、引导说明新增 key 到**两个 ResX 都加**
  （`Strings.resx` 英文 + `Strings.zh-Hans.resx` 中文），键集保持一致。
- 命名示例：`root.wizard.title`、`root.step.extract`、`root.step.install_magisk`（安装 Magisk
  App 步骤/日志文案）、`root.confirm.flash.warning`、
  `root.confirm.flash.ab.dualslot`（双槽刷入提示）、`root.confirm.flash.targetslot`（目标槽位显示）、
  `root.blocked.locked.guide`、`root.error.*`（含 `root.error.install.magisk`：装 APK 失败；
  `root.error.flash.slotb`：第二槽刷入失败）。
- XAML 用 `{loc:Localize Key=...}`，VM 用 `_localization.Get/Format`。

## 11. 测试

- **Adb.Tests** 新增：
  - `Parsers/FastbootVarParserTests`（`getvar unlocked` / `slot-count` / `fastboot devices`）
  - `Parsers/PackageTypeDetectorTests`（zip 条目 → 包类型）
  - `Parsers/CpuAbiParserTests`（`ro.product.cpu.abi` 解析）
  - `Commands/`：fastboot/adb argv 构建测试（含 A/B 双槽 `flash boot_a`/`boot_b` 的 argv）
  - `Services/`：`BootImageExtractor`、`MagiskPatcher`、`FastbootService`（假 `ProcessRunner`，
    覆盖流程与错误路径；`MagiskPatcher` 覆盖 `adb install` 成功→调 `boot_patch.sh`、install
    失败→`Failed`、ABI 探测；`FlashBootAsync` 覆盖非 A/B 单槽、A/B 双槽成功、A/B 第二槽失败→整体失败）
- **App.Tests** 新增：`RootWizardViewModel` 状态机推进、确认门控（未勾选不能 flash）、错误映射、
  未解锁→Blocked 分支；DI 图解析新服务（`ServiceGraphTests`）。
- **实机验证**（§8）：无法单测，作为手动验证清单。

## 12. CLAUDE.md 更新

因放宽只读定位，实现时需：

- 修改"Read-only / safe by design"条目：说明工具现含**受控、需多重确认的刷机写操作**（仅
  Root 向导内 boot 刷入链路），其余功能仍保持只读；解锁 bootloader 仍不自动执行。
- Architecture / Bundled tools 段补充 fastboot、magisk 组件、payload_dumper 打包说明。
- 版本按发布规则统一 bump。

## 13. 明确的非目标（YAGNI）

- 不做 bootloader 自动解锁。
- 不做 boot 以外分区刷写。
- 不做一键恢复/卸载 root 向导（仅备份 + 引导）。
- 不支持厂商私有包格式。
- 不引入桌面版第三方 `magiskboot`（改用安装官方 Magisk APK 后调用其自带组件，不在桌面侧拆
  native 二进制）。
