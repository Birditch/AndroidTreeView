# 半自动 Root 功能实施计划

- 日期：2026-07-10
- 状态：草案，等待实施
- 设计依据：[`../specs/2026-07-09-semi-auto-root-design.md`](../specs/2026-07-09-semi-auto-root-design.md)
- 目标平台：`win-x64`、`osx-arm64`

## 1. 交付目标

在完整 App 中增加可恢复、可取消、带两次风险确认的 Root 向导。用户选择与当前设备匹配的
刷机包后，App 根据设备与包证据选择原始 `boot.img` 或 `init_boot.img`，完成备份和 Magisk 修补，
验证 ADB → fastboot 设备身份连续性，再检测解锁与槽位并在明确确认后刷入、重启。

Mini、Mini.Mac 不提供 Root 向导，也不携带 Magisk 或 payload-dumper。

## 2. 仓库现状与计划校正

实施时以当前代码为准，不能照设计稿重复建设已经存在的能力：

- `IFastbootService`、`FastbootService`、fastboot 设备合并和卡片展示已经存在。
- `build/AndroidTreeView.Scrcpy.targets` 与 `packaging/build-update-zip.ps1` 已把 fastboot 放在
  App 的 `scrcpy/` 目录，Windows 与 macOS App 包均可携带 fastboot。
- 完整 App 已是 `net10.0 + Avalonia`，`osx-arm64` 发布链路已经存在，不需要新建 macOS App。
- 现有 `FastbootService` 面向设备列表，普通失败会被吞掉；Root 写入链路必须返回可诊断的严格结果。
- 现有 `ProcessRunner` 是静态内部类，无法直接用假对象覆盖 payload-dumper 和 fastboot 错误路径。
- 当前通用 `IDialogService` 不支持“阅读风险并勾选确认”，第二确认点应由 Root 页面状态门控。

## 3. 不可跳过的技术门槛

### M0：验证 Magisk 修补链路

这是主实现的前置门槛。Magisk `v30.7` APK 中存在 `assets/boot_patch.sh`、
`assets/util_functions.sh` 和各 ABI 的 `libmagisk*.so`，但 `boot_patch.sh` 明确要求脚本、
`magiskboot`、`magiskinit`、`magisk`、`init-ld` 与 `stub.apk` 位于同一目录。安装 APK 并不等于
这些文件可被普通 `adb shell` 直接访问。

验证步骤：

1. 使用可丢弃数据、bootloader 已解锁的测试机和与设备版本匹配的原始镜像；M0 必须分别验证
   `boot` 目标与 Android 13+ GKI 的 `init_boot` 目标。recovery-only 设备明确判为首版不支持。
2. 执行 `adb install -r Magisk-v30.7.apk`，记录 package path、ABI 与 shell 可访问的组件路径。
3. 尝试在非 root 的 `adb shell` 中调用已安装 App 的修补组件，产出 `new-boot.img`。
4. pull 修补结果并让 Magisk App/`magiskboot` 验证镜像；只有验证通过才进入 Task 1。

通过标准：不依赖设备已 root，命令可重复执行，修补产物非空，清理后无残留临时文件。
`boot` 与 `init_boot` 两条路径都通过后 M0 才算完成。

失败处理：暂停主实现，更新设计规格并让用户选择“从已校验 APK 提取官方组件后 push”或其他
方案。不得在未确认的情况下悄悄换成第三方桌面 `magiskboot`。

### M1：首次真实刷写

只在单元测试、包验证和 M0 全部通过后执行。先用非 A/B 测试机或可恢复设备验证单槽写入，再验证
A/B 双槽及第二槽失败提示。真实刷写不放入 CI。

## 4. 依赖顺序

```text
M0 Magisk 实机验证
  -> Task 1 可替换的外部命令执行层
  -> Task 2 领域模型与 Core 合约
  -> Task 3 ZIP / 嵌套 ZIP 提取
  -> Task 4 Root 工具固定版本与打包
  -> Task 5 payload.bin 提取
  -> Task 6 原始目标镜像备份
  -> Task 7 Magisk 修补服务
  -> Task 8 严格 fastboot 检测与刷写
  -> Task 9 向导状态机
  -> Task 10 App 页面、导航、本地化
  -> Task 11 集成与回归测试
  -> Task 12 发布包验证、文档和 M1
```

## 5. 分步实施

### Task 1：建立可替换的外部命令执行层

文件：

- 新增 `src/AndroidTreeView.Core/Interfaces/IExternalCommandRunner.cs`
- 新增 `src/AndroidTreeView.Core/Services/ExternalCommandRequest.cs`
- 新增 `src/AndroidTreeView.Core/Services/ExternalCommandResult.cs`
- 新增 `src/AndroidTreeView.Adb/Services/ExternalCommandRunner.cs`
- 修改 `src/AndroidTreeView.Adb/Services/FastbootService.cs`
- 修改 `src/AndroidTreeView.Shared/AndroidTreeViewSharedServices.cs`
- 新增 `tests/AndroidTreeView.Adb.Tests/TestDoubles/FakeExternalCommandRunner.cs`
- 新增 `tests/AndroidTreeView.Adb.Tests/Services/FastbootServiceTests.cs`

步骤：

1. 先写失败测试，覆盖 fastboot 缺失、超时、非零退出、stderr 输出和取消传播。
2. 用 `ExternalCommandRunner` 适配现有 `ProcessRunner.RunAsync`，保留参数列表传递，禁止拼 shell 字符串。
3. 让 `FastbootService` 注入 runner；设备列表相关 API 继续保持 best-effort 行为。
4. 在 Shared 中用 `TryAddSingleton` 注册 runner，不改 Mini 的现有行为。

验收：`dotnet test tests/AndroidTreeView.Adb.Tests --filter FullyQualifiedName~FastbootServiceTests`

### Task 2：定义 Root 领域模型与 Core 合约

文件：

- 新增 `src/AndroidTreeView.Models/Rooting/BootImageSource.cs`
- 新增 `src/AndroidTreeView.Models/Rooting/BootPartitionTarget.cs`
- 新增 `src/AndroidTreeView.Models/Rooting/BootImageInfo.cs`
- 新增 `src/AndroidTreeView.Models/Rooting/RootWizardState.cs`
- 新增 `src/AndroidTreeView.Models/Rooting/RootWizardSnapshot.cs`
- 新增 `src/AndroidTreeView.Models/Rooting/FastbootBootLayout.cs`
- 新增 `src/AndroidTreeView.Models/Rooting/FirmwarePackageMetadata.cs`
- 新增 `src/AndroidTreeView.Models/Rooting/RootErrorCode.cs`
- 新增 `src/AndroidTreeView.Core/Interfaces/IBootImageExtractor.cs`
- 新增 `src/AndroidTreeView.Core/Interfaces/IBootBackupService.cs`
- 新增 `src/AndroidTreeView.Core/Interfaces/IMagiskPatcher.cs`
- 新增 `src/AndroidTreeView.Core/Interfaces/IRootWizardService.cs`
- 新增 `src/AndroidTreeView.Core/Exceptions/RootWorkflowException.cs`

约束：

- `BootImageInfo` 显式保存 `BootPartitionTarget`（`Boot` / `InitBoot`），禁止从文件名反推刷写分区。
- `RootWizardSnapshot` 保存 ADB serial、USB 路径、product、重启前 fastboot 基线、匹配后的 fastboot
  serial、包路径、目标分区、工作目录、原始/修补镜像、备份路径、槽位和错误码。
- 错误对 UI 暴露稳定错误码，不直接把任意 stderr 当成用户文案。
- 状态增加 `BlockedFastbootIdentity`：ADB 重启后没有出现新设备，或 serial / USB 路径 / product
  等证据不能证明 fastboot 设备就是所选 ADB 设备时禁止刷写；列表中仅有一台设备不算证明。
- 所有 I/O 接口均为 async，并传递 `CancellationToken`。

验收：Core 和 Models 无上层项目引用，`dotnet build AndroidTreeView.sln -p:EnableWindowsTargeting=true`。

### Task 3：实现安全的 ZIP 与 Pixel 嵌套 ZIP 提取

文件：

- 新增 `src/AndroidTreeView.Adb/Parsers/PackageTypeDetector.cs`
- 新增 `src/AndroidTreeView.Adb/Parsers/BootPartitionTargetDetector.cs`
- 新增 `src/AndroidTreeView.Adb/Parsers/FirmwarePackageMetadataParser.cs`
- 新增 `src/AndroidTreeView.Adb/Services/BootImageExtractor.cs`
- 新增 `tests/AndroidTreeView.Adb.Tests/Parsers/PackageTypeDetectorTests.cs`
- 新增 `tests/AndroidTreeView.Adb.Tests/Parsers/BootPartitionTargetDetectorTests.cs`
- 新增 `tests/AndroidTreeView.Adb.Tests/Parsers/FirmwarePackageMetadataParserTests.cs`
- 新增 `tests/AndroidTreeView.Adb.Tests/Services/BootImageExtractorTests.cs`

测试先覆盖：顶层 `boot.img` / `init_boot.img`、两者同时存在、GKI 13+ 选择 `init_boot`、存在
`init_boot` 的 Android 12 内核仍选择 `boot`、大小写差异、Pixel `image-*.zip`、payload、目标镜像
缺失、设备与包证据冲突、recovery-only、重复目标、损坏 ZIP、路径穿越、取消和工作目录清理。

实现约束：

- 只允许一层嵌套 ZIP；条目路径必须经过 `Path.GetFullPath` 边界检查。
- 不把整个大包读入内存；使用流复制。
- 设定单条目和总展开大小上限，拒绝 ZIP bomb。
- 读取 OTA `META-INF/com/android/metadata` 与 Pixel `android-info.txt`；元数据明确不匹配当前
  `ro.product.device`/product 时硬阻止，元数据缺失时标记为“无法自动验证”并留给第二确认点。
- 解析设备的 `init_boot` 分区存在性和内核/GKI 版本；仅当 `init_boot` 存在、内核满足 GKI 13+
  且包中包含 `init_boot.img` 时选择 `InitBoot`，否则选择 `Boot`。设备要求 `init_boot` 但包中缺失、
  证据矛盾或 Magisk 判定 recovery-only 时返回稳定阻塞错误，不回退猜测其他分区。
- 每次会话使用独立的 `root-work/<session-id>/`，失败时清理，成功时由向导结束后清理。

验收：提取测试全部通过，且测试用临时目录在成功、失败、取消后均被回收。

### Task 4：固定并打包 Root 工具

固定版本：

- Magisk：`v30.7`，资产 `Magisk-v30.7.apk`，SHA-256
  `e0d32d2123532860f97123d927b1bb86c4e08e6fd8a48bfc6b5bee0afae9ebd5`。
- payload-dumper-go：`1.3.0`，使用上游 `payload-dumper-go_sha256checksums.txt` 中的
  `windows_amd64` 与 `darwin_arm64` 校验值。
- fastboot：继续复用现有 App `scrcpy/fastboot[.exe]`，不新增第二份 platform-tools。

文件：

- 新增 `build/AndroidTreeView.RootTools.targets`
- 修改 `src/AndroidTreeView.App/AndroidTreeView.App.csproj`
- 修改 `packaging/build-update-zip.ps1`
- 修改 `.github/workflows/publish.yml`
- 新增 `tools/verify-roottools-latest.ps1`

步骤：

1. App 单独导入 RootTools target；Mini 和 Mini.Mac 不导入。
2. 下载后先计算 SHA-256，不匹配立即失败，再解包或复制。
3. 输出布局固定为 `root-tools/magisk/Magisk-v30.7.apk` 和
   `root-tools/payload-dumper/payload-dumper-go[.exe]`。
4. macOS 包为 payload-dumper 设置执行位。
5. 发布工作流验证 App 包含三个 Root 必需工具，Mini 包不包含 `root-tools/`。

验收：对 `win-x64` 与 `osx-arm64` 各运行一次 App 打包，并检查 ZIP 条目和哈希失败路径。

### Task 5：接入 payload.bin 提取

文件：

- 修改 `src/AndroidTreeView.Adb/Services/BootImageExtractor.cs`
- 新增 `src/AndroidTreeView.Adb/Services/RootToolPaths.cs`
- 扩展 `tests/AndroidTreeView.Adb.Tests/Services/BootImageExtractorTests.cs`

步骤：

1. 用 fake runner 写测试，固定断言可执行文件、argv、超时和输出目录。
2. 对裸 payload 与 ZIP 内 payload 使用同一分支，按已解析的 `BootPartitionTarget` 只请求
   `boot` 或 `init_boot` 分区。
3. runner 非零退出、超时或未产出目标镜像时抛稳定错误码。
4. 校验输出文件存在、大小合理，再返回 `BootImageInfo`。

验收：不安装真实 payload-dumper 也能覆盖所有流程分支；打包 smoke test 再验证真实二进制可启动。

### Task 6：实现原始目标镜像备份

文件：

- 新增 `src/AndroidTreeView.Infrastructure/Rooting/BootBackupService.cs`
- 新增 `tests/AndroidTreeView.Infrastructure.Tests/BootBackupServiceTests.cs`
- 修改 `src/AndroidTreeView.Shared/AndroidTreeViewSharedServices.cs`

约束：

- 目录为 `~/.androidtreeview/root-backups/`。
- serial 先清洗为文件名安全字符；使用 UTC 时间和随机后缀防碰撞。
- 先写临时文件，再原子移动；取消或复制失败不得留下半个备份。
- 返回绝对路径，并验证备份长度与源文件相同。

验收：覆盖非法 serial、同秒多次备份、取消、源文件不存在和成功内容一致。

### Task 7：实现 Magisk 修补服务

文件：

- 新增 `src/AndroidTreeView.Adb/Parsers/CpuAbiParser.cs`
- 新增 `src/AndroidTreeView.Adb/Services/MagiskPatcher.cs`
- 新增 `tests/AndroidTreeView.Adb.Tests/Parsers/CpuAbiParserTests.cs`
- 新增 `tests/AndroidTreeView.Adb.Tests/Services/MagiskPatcherTests.cs`
- 修改 `src/AndroidTreeView.Shared/AndroidTreeViewSharedServices.cs`

严格按 M0 验证通过的命令序列实现：安装固定 APK、探测 ABI、创建会话临时目录、push 镜像、执行
官方修补脚本、pull 结果、校验产物，并在 `finally` 中清理手机临时目录。
每条 ADB 命令都必须通过 argv 显式指定 `RootWizardSnapshot` 中锁定的 ADB serial。

测试覆盖：安装失败、未知 ABI、push 失败、脚本失败、pull 失败、空产物、取消和清理失败不遮蔽主错误。
日志不得输出完整敏感 stderr；UI 只接收错误码与经过裁剪的诊断摘要。

验收：`MagiskPatcherTests` 全部通过，随后在 M0 测试机重复一次端到端修补。

### Task 8：增加严格 fastboot 检测与刷写结果

文件：

- 新增 `src/AndroidTreeView.Adb/Commands/FastbootArgs.cs`
- 新增 `src/AndroidTreeView.Adb/Parsers/FastbootVarParser.cs`
- 修改 `src/AndroidTreeView.Core/Interfaces/IFastbootService.cs`
- 修改 `src/AndroidTreeView.Adb/Services/FastbootService.cs`
- 新增 `tests/AndroidTreeView.Adb.Tests/Commands/FastbootArgsTests.cs`
- 新增 `tests/AndroidTreeView.Adb.Tests/Parsers/FastbootVarParserTests.cs`
- 扩展 `tests/AndroidTreeView.Adb.Tests/Services/FastbootServiceTests.cs`

新增能力：重启前捕获 `fastboot devices -l` 基线，等待并匹配重启后新出现的 fastboot 设备，读取
`unlocked`、`slot-count`、`has-slot:<target>` 与 `current-slot`，按 `BootPartitionTarget` 执行单槽
或 A/B 双槽刷写，返回失败分区和 stderr 摘要并重启系统。

安全规则：

- 重启前保存已有 fastboot serial 集合和所选 ADB 设备的 serial、USB 路径、product；重启后只考虑
  基线之外的新设备。优先匹配保持不变的 serial，再用 USB 路径和 `getvar product` 交叉验证。
- 没有新设备、证据冲突或无法充分匹配时返回 `FastbootIdentityUnverified`，绝不因列表中只有一台
  就选择它。后续每条 fastboot 命令都必须显式带 `-s <matched-fastboot-serial>`。
- 可执行文件先查已定位 adb 的同目录，再回退到 App 自带 `scrcpy/fastboot[.exe]`。
- `unlocked` 不是明确 yes/true 时按未解锁处理。
- `has-slot:<target>=no` 才判为非 A/B；`has-slot:<target>=yes` 且槽位数明确时判为 A/B；信息矛盾或
  缺失时不猜布局，进入阻塞状态。
- A/B 的 `<target>_a` 成功、`<target>_b` 失败时返回部分写入结果，UI 必须显示设备处于危险中间态。
- 非零退出和超时不得继续执行下一条写命令。

验收：argv、stdout/stderr 解析和全部失败分支均由纯单测固定；至少覆盖“已有其他 fastboot 设备且
目标重启失败”“目标作为新设备出现”“serial 改变但 USB/product 匹配”“唯一设备但无身份证据”四种场景。

### Task 9：实现可恢复的 Root 向导状态机

文件：

- 新增 `src/AndroidTreeView.Core/Services/RootWizardService.cs`
- 新增 `tests/AndroidTreeView.Core.Tests/RootWizardServiceTests.cs`
- 修改 `src/AndroidTreeView.Shared/AndroidTreeViewSharedServices.cs`

状态转换由显式方法驱动：`SelectPackage`、`ExtractAndPatchAsync`、`ConfirmBootloaderAsync`、
`DetectFastbootAsync`、`ConfirmFlashAsync`、`RetryAsync`、`CancelAsync`。服务不弹 UI，也不自行越过
确认点。
`ConfirmBootloaderAsync` 必须先捕获 fastboot 基线，再对锁定的 ADB serial 执行 reboot；
`DetectFastbootAsync` 只接收 `WaitForMatchingFastbootDeviceAsync` 返回的已验证 serial。

测试固定以下不变量：

- 未完成备份不能进入 flash 确认。
- 包元数据明确与设备不匹配时不能进入修补或 flash；无法验证时必须显示额外风险提示。
- 未解锁、槽位未知、目标分区未知、fastboot 身份未验证、未勾选风险确认均不能调用 flash。
- 取消会杀掉当前外部进程并保留原始备份。
- 重试只重跑失败步骤，不重复刷已成功的分区。
- 完成/中止后清理工作目录，但永不删除备份。

验收：状态转换表的每条边和每个阻塞状态至少有一个测试。

### Task 10：接入 App 导航、页面和本地化

文件：

- 新增 `src/AndroidTreeView.App/ViewModels/RootWizardViewModel.cs`
- 新增 `src/AndroidTreeView.App/Views/RootWizardView.axaml`
- 新增 `src/AndroidTreeView.App/Views/RootWizardView.axaml.cs`
- 修改 `src/AndroidTreeView.App/ViewModels/AppEnums.cs`
- 修改 `src/AndroidTreeView.App/ViewModels/MainWindowViewModel.cs`
- 修改 `src/AndroidTreeView.App/Views/MainWindow.axaml`
- 修改 `src/AndroidTreeView.App/AppServices.cs`
- 修改 `src/AndroidTreeView.App/Services/IFilePickerService.cs`
- 修改 `src/AndroidTreeView.App/Services/FilePickerService.cs`
- 修改两个 `Strings*.resx`

实现：

- Root VM 注册为 singleton，使用户切换导航后不丢工作流。
- 向导第一步列出在线且已授权的 ADB 设备并要求单选；多设备时不默认选第一台。开始提取后锁定
  serial、USB 路径和 product，中途切换设备必须先中止当前向导。
- 文件选择器新增单选刷机包入口，只接受 ZIP 与 payload；给 `FilePickerService` 注入
  `ILocalizationService`，选择器标题与文件类型名称也必须本地化。
- 第一次确认可用现有 `IDialogService`；第二次在 Root 页面显示备份路径、目标分区和风险复选框。
- 第二确认点同时显示 ADB/fastboot 身份匹配结果、设备代号、目标分区和包元数据匹配结果；无法自动
  验证设备身份时不显示可绕过警告，而是保持刷写按钮禁用。
- 新增 `root.blocked.fastboot.identity`、`root.blocked.partition.unsupported` 和
  `root.confirm.flash.targetpartition` 等中英文资源键，两个 ResX 键集保持一致。
- `FlashCommand` 的 CanExecute 同时依赖状态和 `HasAcknowledgedRisk`。
- 页面只绑定 VM，使用 `x:DataType`；所有正常错误映射成 `root.error.*`。
- 狭窄窗口下步骤条可横向滚动，底部操作区不遮挡错误和日志。

验收：两个 ResX 键集合完全一致，App headless boot 能解析 Root 页面全部 compiled bindings。

### Task 11：补齐 App 集成和回归测试

文件：

- 新增 `tests/AndroidTreeView.App.Tests/RootWizardViewModelTests.cs`
- 修改 `tests/AndroidTreeView.App.Tests/Fakes.cs`
- 修改 `tests/AndroidTreeView.App.Tests/ServiceGraphTests.cs`
- 修改 `tests/AndroidTreeView.App.Tests/BootSmokeTests.cs`

覆盖：导航、单/多设备选择、禁止默认选择第一台、选包取消、`boot` / `init_boot` 目标选择、
recovery-only 阻塞、两个确认门、锁定设备、ADB → fastboot 身份匹配失败、A/B 警告、第二槽失败、
重试、取消、语言切换、DI 图和 XAML 启动。
再运行全量 build/test/format，确认设备列表与现有 best-effort fastboot 行为未回归。

验收命令：

```bash
dotnet restore AndroidTreeView.sln -p:EnableWindowsTargeting=true
dotnet build AndroidTreeView.sln -c Release --no-restore -p:EnableWindowsTargeting=true
dotnet test AndroidTreeView.sln -c Release --no-build -p:EnableWindowsTargeting=true
dotnet format AndroidTreeView.sln --verify-no-changes
```

### Task 12：发布验证、文档同步和 M1

修改：`CLAUDE.md`、`AGENTS.md`、`docs/architecture.md`、`docs/app-contract.md`、
`docs/packaging.md`、`docs/publishing.md`、`docs/roadmap-features.md` 和统一版本位置。

发布前：

1. 生成 App 的 `win-x64` 与 `osx-arm64` ZIP，确认 fastboot、Magisk APK、payload-dumper 和执行位。
2. 生成两个 Mini ZIP，确认没有 `root-tools/`，体积与依赖没有意外增长。
3. 在干净 Windows 与 macOS 机器启动 App，完成选包、提取和到达第一次确认点的无写入 smoke test。
4. 按 M1 在可恢复真机执行刷写；记录设备、包版本、槽位、命令结果和回滚方式。
5. M1 通过后再 bump 版本、更新 changelog 并进入发布流程。

## 6. 完成定义

- M0、M1 有可复核记录，且没有未说明的手工步骤。
- 两个平台可从发布 ZIP 启动并走完整向导。
- 未解锁、fastboot 身份未验证、目标分区或槽位未知、包不匹配、包损坏、工具缺失和命令失败都
  无法越过写入门。
- 原始目标镜像在任何刷写前已完成本地备份，失败或取消不会删除。
- App 不因正常设备/命令错误崩溃；错误均有中英文文案和可操作的下一步。
- 全量测试通过，两个 ResX 键集合一致，Mini 不携带 Root 工具。

## 7. 明确不在本计划内

- 自动解锁 bootloader。
- 刷写 `boot` / `init_boot` 以外分区。
- 自动回刷、卸载 Root 或救砖向导。
- 厂商私有包与动态分区拆包。
- Linux 发布包或 macOS x64 发布包。
