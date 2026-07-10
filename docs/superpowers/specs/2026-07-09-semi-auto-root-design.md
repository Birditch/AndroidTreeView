# 半自动 Root 功能 — 设计规格

- 日期：2026-07-09
- 状态：设计已确认，实施计划已起草；Magisk 修补链路待 M0 实机验证
- 关联记忆：`root-feature-relaxes-safety`
- 实施计划：[`../plans/2026-07-10-semi-auto-root-implementation.md`](../plans/2026-07-10-semi-auto-root-implementation.md)

> 仓库现状校正（2026-07-10）：当前代码已包含 `IFastbootService` / `FastbootService`、fastboot
> 设备列表合并、Windows/macOS App 打包和 App 内 fastboot 分发。实施时扩展这些现有能力，
> 不再新建第二套 fastboot locator/environment 或 platform-tools 目录。Magisk“安装 APK 后直接
> 调用组件”仍是 M0 硬门槛，未通过前不得进入刷写主链路。

## 1. 目标与范围

为 AndroidTreeView 增加一个**半自动 Root 功能**：用户手动上传刷机包，工具根据设备安全选择
`boot.img` 或 `init_boot.img`、用官方 Magisk 组件修补、引导进入 bootloader、把修补后的镜像
刷入对应分区。以**分步向导**形态交付，写操作前强制确认，刷入前自动备份原始未修补镜像。

### 1.1 第一版明确要做

- 上传刷机包（本地文件选择）。
- 提取目标启动镜像：
  - zip 内含裸 `boot.img` / `init_boot.img`（含 Pixel factory 的嵌套 `image-*.zip`）。
  - `payload.bin`（A/B OTA），通过打包的 `payload_dumper` 按需解出 `boot` / `init_boot`。
  - 设备明确存在 `init_boot`、内核满足 GKI 13+ 且包中也有 `init_boot.img` 时选择 `init_boot`；
    其他受支持设备选择 `boot`。证据矛盾、目标镜像缺失，或 Magisk 判定必须修补 recovery 时
    阻止流程，第一版不猜测、不刷 recovery。
- Magisk 修补：工具**构建时打包固定版本官方 Magisk APK**（SHA-256 校验），运行时
  `adb install` 到手机，调用**已装 Magisk App 自带的** `boot_patch.sh` + native 组件在手机端
  完成修补，pull 回修补后镜像。
- 写操作链路：`adb push` → `adb reboot bootloader` → **flash 目标启动分区（A/B 机型两个槽都刷）** →
  `fastboot reboot`。
  - **非 A/B 机型**：`fastboot flash <boot|init_boot> <img>`。
  - **A/B 机型**：`fastboot flash <target>_a <img>` **且** `fastboot flash <target>_b <img>`
    （两槽都刷入同一修补后镜像，避免槽位翻转/刷错槽导致 Magisk 不生效）。
  - 刷入前**强制提示用户**双槽都会被写入及其风险（见 §5.3 确认 2、§3 已知风险）。
- 只读检测：`fastboot getvar unlocked`（解锁状态）、**A/B 布局探测
  （联合 `slot-count`、`has-slot:<target>`、`current-slot`，矛盾或缺失时阻止刷写）**、设备 ABI 探测。
- ADB → fastboot 身份连续性：重启前保存 fastboot 基线和所选 ADB 设备身份；重启后只接受新出现且
  serial / USB 路径 / product 等证据能匹配的设备。无法证明是同一设备时阻止刷写。
- **刷入前自动备份原始未修补目标镜像** 到本地用户目录。
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
| 目标启动分区 | **仅在 `init_boot` 存在 + GKI 13+ + 包含对应镜像时选 `init_boot`，否则选 `boot`；歧义或 recovery-only 时阻止** | 对齐 Magisk 官方 `find_boot_image` 逻辑，避免把补丁刷进错误分区 |
| ADB → fastboot 身份连续性 | **重启前记录基线，重启后只接受有充分身份证据的新设备** | 唯一设备不等于目标设备，无法证明身份时必须阻止刷写 |
| A/B 机型刷入策略 | **目标分区的 `_a` / `_b` 两个槽都刷同一镜像 + 刷入前强制提示** | 免探测 active slot、免刷错槽；代价（跨版本双槽变砖）以提示告知用户承担 |
| 工具打包 | **构建时按平台下载打包进 `tools/`** | 对齐现有 scrcpy/adb 打包，开箱即用 |
| UI 落点 | **App 新导航页，复用现有 Windows/macOS App 发布链路** | 双平台共用同一套 Avalonia MVVM 代码 |
| 修补第 4 步实现 | **安装官方 Magisk APK 后跑其自带 `boot_patch.sh`** | 最贴近官方；实机验证列为高风险里程碑 |

## 3. 可行性结论

核心链路技术上全部可实现，跨平台可行。真正的风险**不在技术**，而在三个工具无法根除、
必须由用户承担的前提：

- **前提 1 — bootloader 已解锁**：未解锁则 `fastboot flash` 必然失败。工具只检测并引导，
  不自动解锁（解锁抹数据、需在手机上按键、部分厂商需解锁码）。
- **前提 2 — 包与设备匹配**：刷错版本/架构可能变砖。工具做基本校验与警告，但无法保证包正确。
- **前提 3 — A/B 双槽刷入的跨版本风险（已知、以提示承担）**：A/B 机型两个槽都刷入同一
  修补后镜像。若两个槽当前系统版本不一致（如 OTA 后未满一个周期），把当前版本的目标启动镜像刷进
  另一槽会造成 boot 与该槽 system/vendor 版本错配，**日后回滚到该槽可能 bootloop**（延迟变砖，
  刷入当下无异常）。工具**无法可靠检测两槽版本**，故在**确认 2 强制提示**用户此风险，由用户
  在知情后承担（对应 §2 决策：省掉刷错槽 → 接受此代价）。

设计上以**分步向导 + 强制确认 + 自动备份原始目标镜像** 将风险降到可控。

**最高技术不确定性**：Magisk 修补的第 4 步（执行 `boot_patch.sh`）不是单条命令。现方案先
`adb install` 官方 Magisk APK，再调用其自带的 `boot_patch.sh` + native 组件——装 App 后
`boot_patch.sh` 环境（`MAGISKBIN`/APK 资源）更完整，比裸跑 `/data/local/tmp` 可靠，但
**"已装 App 的 `boot_patch.sh` 能否被 `adb shell` 直接调到"仍需实机验证**（assets 内脚本安装后
不一定落在可直接执行位置，可能仍需从 APK 提取），**必须实机验证**（见 §8 里程碑）。

## 4. 分层与工程落点

严格遵循项目分层（下层不引用上层）：

```
Models   FlashPackage, BootImageInfo(Path, Source, OriginalPackageName, TargetPartition),
         BootPartitionTarget(enum), RootWizardState(enum), DeviceFastbootStatus, PackageType(enum)
  ↑
Core     接口：IBootImageExtractor, IMagiskPatcher, IFastbootService, IRootWizardService
         IExternalCommandRunner、RootException 类型
  ↑
Adb / Infrastructure
         Adb：扩展现有 FastbootService、
              BootImageExtractor(zip + payload.bin)、MagiskPatcher(install APK + 手机端执行)、
              Parsers：FastbootVarParser、PackageTypeDetector、BootPartitionTargetDetector、CpuAbiParser
         Infra：BootBackupService（备份原始目标镜像到用户目录）
  ↑
Shared   AddAndroidTreeViewSharedServices() 内 TryAdd 注册全部新服务
  ↑
App      RootWizardViewModel + RootWizardView（新导航页，分步向导）
```

- 所有 fastboot/adb 写操作走 `ProcessRunner`（异步、可取消、杀进程树）。
- 解析类为纯函数放 `AndroidTreeView.Adb.Parsers`，配套解析测试（项目硬规则）。
- fastboot 复用 App 已有 `scrcpy/fastboot[.exe]`；Magisk APK 与 payload-dumper 通过新建
  `build/AndroidTreeView.RootTools.targets` 按发布 RID 下载、校验并只打包进完整 App。

## 5. 向导状态机与流程

### 5.1 状态枚举 `RootWizardState`

```
Idle                        初始
PackageSelected             已选刷机包
Extracting                  检测并提取 boot/init_boot 目标镜像（自动）
BootExtracted               已提取（含来源：PlainZip / NestedZip / Payload）
Blocked_UnsupportedTarget   目标分区证据冲突、镜像缺失或设备必须修补 recovery，禁止继续
Patching                    修补中（adb install Magisk APK → 手机执行 boot_patch.sh → pull 回）
BootPatched                 已得修补后目标镜像 + 已本地备份原始目标镜像
AwaitingBootloaderConfirm   ⛔ 强制确认点 1：即将重启进 bootloader
RebootingToBootloader       adb reboot bootloader（自动）
InFastboot                  已确认同一设备进入 fastboot，检测解锁状态 + 目标分区 A/B 布局
Blocked_DeviceMismatch      无法证明 fastboot 设备与所选 ADB 设备相同，禁止继续
Blocked_Locked              未解锁：引导用户，终止自动流程（可重新检测）
AwaitingFlashConfirm        ⛔ 强制确认点 2：即将 flash 目标分区（显示分区/槽位；A/B 双槽都刷）
Flashing                    fastboot flash <target>（A/B：先 <target>_a 再 <target>_b，自动）
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
2. **确认 2（flash 前）**：显示目标分区 `boot` 或 `init_boot`、修补后镜像、**原始镜像备份路径**、
   **目标槽位（A/B 机型明确显示将同时刷入 `<target>_a` 和 `<target>_b`）**、红色"刷错可能变砖"警告；
   **A/B 机型额外红字提示**：两个槽都会被写入同一镜像，若两槽系统版本不一致，日后回滚到另一槽
   可能变砖（§3 前提 3）；用户勾选"我已了解风险"后方可点"刷入"。

### 5.4 关键分支

- **Blocked_Locked（未解锁）**：向导停止，显示解锁引导（开发者选项 OEM 解锁、
  `fastboot flashing unlock` 会抹数据、部分厂商需解锁码）。**工具不代执行解锁**。
  用户自行解锁后可"重新检测"继续。
- **Blocked_DeviceMismatch（身份不连续）**：若目标重启后没有出现新 fastboot 设备，或新设备的
  serial / USB 路径 / product 无法与重启前记录相互印证，禁止继续；不得因列表中只剩一台设备就选它。
- **Blocked_UnsupportedTarget（目标分区不受支持）**：设备与包对 `boot` / `init_boot` 的证据冲突、
  目标镜像缺失，或设备必须修补 recovery 时终止流程；第一版不允许用户绕过该阻塞。
- **Failed（任一步失败）**：显示该步 stderr/错误摘要（映射成友好文案），提供"重试当前步"或
  "中止"。中止不留危险中间态（若已在 fastboot，引导用户 `fastboot reboot` 回系统）。
- **可取消**：每个自动步走 `CancellationToken`，用户可随时取消（取消后进入 Failed/可重试）。

## 6. 核心机制

### 6.1 目标启动镜像提取（`BootImageExtractor`）

输入包文件和设备探测结果，按类型分派，输出目标镜像本地路径、目标分区 + 来源标记
（`BootImageInfo { Path, TargetPartition, Source, OriginalPackageName }`）。

**包类型判定**（纯函数 `PackageTypeDetector`，读文件头/枚举 zip 条目）：

- zip 顶层有 `boot.img` / `init_boot.img` → **PlainZip**
- zip 含 `image-*.zip`（Pixel factory）→ **NestedZip**（解一层内层 zip 再取目标镜像）
- zip 含 `payload.bin` → **Payload**
- 裸 `payload.bin` 文件 → **Payload**
- 都不匹配 → 抛 `RootException`，友好提示"未在包内找到受支持的 boot/init_boot 镜像"

**提取**：

- PlainZip / NestedZip → 纯 C# `System.IO.Compression`，无外部依赖。
- Payload → 调用打包的 `payload_dumper`（走 `ProcessRunner`）按探测结果解出 `boot` 或 `init_boot`。
- 目标判定必须同时满足设备侧分区、GKI 版本证据与包内候选镜像；只有 `init_boot` 存在且内核为
  GKI 13+ 时才选择 `init_boot`。设备要求 `init_boot` 但包中缺失，或两侧证据矛盾时返回阻塞错误。
  Magisk 判定 recovery-only 时明确提示首版不支持。
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
3. **本地备份原始目标镜像**（`BootBackupService`）：复制到
   `~/.androidtreeview/root-backups/<serial>-<timestamp>-<target>-original.img`，路径供确认 2 显示。
4. **修补**：push 待修补目标镜像到手机临时目录 `/data/local/tmp/atv_root/`，`adb shell` 调用
   **已装 Magisk App 自带的** `boot_patch.sh`（其可自解析 `MAGISKBIN`/APK 资源，内部调用
   `magiskboot unpack/cpio/repack` + `magiskinit`/`magisk`），产出 `new-boot.img`。
5. **pull 回**：`adb pull .../new-boot.img <workdir>/boot-patched.img`。
6. **清理**手机临时目录。

上述所有 ADB 命令都必须显式指定向导开始时锁定的 ADB serial，不依赖 adb 的“唯一设备”隐式选择。

> ⚠️ 第 4 步为最高技术不确定性环节：装 App 后 `boot_patch.sh` 环境更完整，但**"已装 App 的
> `boot_patch.sh` 能否被 `adb shell` 直接调到"仍未完全消除不确定性**（APK 内 `assets` 脚本安装后
> 不一定落在可直接执行位置，可能仍需从 APK 提取脚本再 push），需实机验证（见 §8）。

### 6.3 fastboot 服务（`FastbootService`，走 `ProcessRunner`，全部 async + CancellationToken）

| 方法 | 命令 | 说明 |
|---|---|---|
| `RebootToBootloaderAsync` | `adb reboot bootloader` | 从系统进 fastboot |
| `CaptureFastbootBaselineAsync` | `fastboot devices -l` | 重启前记录已有 fastboot serial / USB 身份，防止误认其他设备 |
| `WaitForMatchingFastbootDeviceAsync` | `fastboot devices -l` 轮询 + `getvar product` | 只接受新出现且能与所选 ADB 设备相互印证的设备 |
| `GetUnlockStatusAsync` | `fastboot getvar unlocked` | 只读，`FastbootVarParser` 解析 `unlocked: yes/no` |
| `GetBootLayoutAsync` | `fastboot getvar slot-count` + `has-slot:<target>` + `current-slot` | 只读；联合判定目标分区 A/B，矛盾或缺失时返回 Unknown |
| `FlashBootAsync` | 非 A/B：`fastboot flash <target> <img>`；A/B：依次刷 `<target>_a`、`<target>_b` | 核心写操作；A/B 两槽都刷同一镜像 |
| `RebootAsync` | `fastboot reboot` | 刷完回系统 |

解析 `getvar` / `fastboot devices -l` 输出为纯函数 + 解析测试。所有设备命令都必须携带
`-s <matched-fastboot-serial>`；单纯“列表中只有一台”不是身份匹配证据。`slot-count`、
`has-slot:<target>`、`current-slot` 复用同一 `FastbootVarParser`。A/B 双槽刷入时，第一槽成功、第二槽失败要
视为整体失败（进 `Failed`，错误摘要注明哪个槽失败），不可停在"只刷了一个槽"的中间态。

### 6.4 fastboot 定位（复用现有 `FastbootService`）

现有 `FastbootService.ExecutablePath` 从 `IAdbEnvironment` 已定位的 adb 同目录解析
`fastboot[.exe]`；发布脚本也已把 fastboot 放入 App 的 `scrcpy/` 目录。Root 向导扩展该服务的
严格检测/刷写结果，不新建第二套 locator/environment。缺失时向导显示“未找到 fastboot”，不崩溃。

### 6.5 备份服务（`BootBackupService`，Infrastructure 层）

- 修补前把原始目标镜像存到 `~/.androidtreeview/root-backups/`，文件名含序列号、目标分区 + 时间戳。
- 提供"列出/打开备份目录"给 UI，便于变砖时找回。

## 7. 工具打包 — `build/AndroidTreeView.RootTools.targets`

仿 `AndroidTreeView.Scrcpy.targets`，构建/发布时按平台下载并打包：

```
tools/
  scrcpy/                  现有目录，已含 adb + fastboot
  root-tools/
    payload-dumper/        win-x64 / osx-arm64 对应 payload-dumper-go
    magisk/
      Magisk.apk           固定版本官方 APK，运行时 adb install
```

- Root 工具只打包进完整 App；Mini / Mini.Mac 不携带。
- 下载源写明 URL + SHA-256 校验，文档登记版本，对齐项目"下载工具需校验 SHA-256"风格：
  - fastboot：复用现有 Google platform-tools 下载与 App 打包路径。
  - magisk：官方 Magisk GitHub release 的 APK 整包（不在桌面侧拆 native 二进制，组件随 App
    安装到手机后使用）。
  - payload_dumper：可信开源发布。
- 新增 `tools/verify-roottools-latest.ps1`（仿 `verify-scrcpy-latest.ps1`）检查上游新版本。

## 8. 高风险里程碑（实机验证）

以下无法单测，须实机验证并在开发中预留调整轮次：

1. **`boot_patch.sh` 修补链路** — `adb install` 官方 Magisk APK 后，能调用其自带
   `boot_patch.sh` 在真机产出可用 `new-boot.img`（重点验证已装 App 的脚本是否可被 `adb shell`
   直接调到）；`boot` 与 `init_boot` 两种目标都必须留下验证记录。
2. **真实 flash** — `fastboot flash boot` 与 `fastboot flash init_boot` 分别在对应的已解锁真机上
   成功且能正常开机。
3. **payload.bin 解包** — `payload_dumper` 在 `win-x64`、`osx-arm64` 均能按目标解出
   `boot` / `init_boot` 分区。
4. **跨平台 fastboot/adb** — Windows x64 与 macOS arm64 下二进制均能定位与执行。

## 9. App UI

- 新导航页，命名遵循 ViewLocator 约定（`RootWizardViewModel` → `RootWizardView`），严格 MVVM
  （`[ObservableProperty]` / `[RelayCommand]`），编译绑定 `x:DataType`。
- 向导第一步先显示在线且已授权的设备单选列表；多设备时不预选，必须由用户明确选择。开始提取后
  锁定所选设备身份，除非中止并重开向导，否则不能切换目标设备。
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
  `root.confirm.flash.targetpartition`（`boot` / `init_boot`）、
  `root.blocked.fastboot.identity`（ADB → fastboot 身份无法验证）、
  `root.blocked.partition.unsupported`（目标分区歧义或 recovery-only）、
  `root.blocked.locked.guide`、`root.error.*`（含 `root.error.install.magisk`：装 APK 失败；
  `root.error.flash.slotb`：第二槽刷入失败）。
- XAML 用 `{loc:Localize Key=...}`，VM 用 `_localization.Get/Format`。

## 11. 测试

- **Adb.Tests** 新增：
  - `Parsers/FastbootVarParserTests`（`unlocked` / `slot-count` / `has-slot:<target>` /
    `current-slot` / `fastboot devices -l` / 身份证据）
  - `Parsers/PackageTypeDetectorTests`（zip 条目 → 包类型）
  - `Parsers/BootPartitionTargetDetectorTests`（设备/包证据 → `boot` / `init_boot` / 阻塞）
  - `Parsers/CpuAbiParserTests`（`ro.product.cpu.abi` 解析）
  - `Commands/`：fastboot/adb argv 构建测试（含 `boot` / `init_boot` 和 A/B 双槽 argv）
  - `Services/`：`BootImageExtractor`、`MagiskPatcher`、`FastbootService`（假
    `IExternalCommandRunner`，
    覆盖流程与错误路径；`MagiskPatcher` 覆盖 `adb install` 成功→调 `boot_patch.sh`、install
    失败→`Failed`、ABI 探测；`FlashBootAsync` 覆盖 `boot` / `init_boot`、非 A/B 单槽、A/B 双槽成功、
    A/B 第二槽失败→整体失败）
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
