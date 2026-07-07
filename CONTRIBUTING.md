# 参与贡献 (Contributing to AndroidTreeView)

感谢你愿意改进 AndroidTreeView —— 一个基于 **.NET 10 + Avalonia** 的桌面应用，通过 ADB 展示已连接 Android
设备的信息。清晰的问题反馈、聚焦的 Pull Request 和有建设性的设计讨论都很有价值。

## 开始之前 (Before You Start)

1. 先搜索已有 Issue 和 Pull Request，避免重复。
2. 如果是较大的功能或架构变更，请先创建 Issue 讨论方案。
3. 每个 Pull Request 尽量只解决一个问题。
4. 提交应尽量小而清晰，便于审查。

## 开发环境 (Development Environment)

- **.NET 10 SDK**（目标框架 `net10.0`）。
- 主要依赖：Avalonia 11.3、CommunityToolkit.Mvvm 8.4、Microsoft.Extensions.*（Hosting / DI / Logging）10.0。
- NuGet 源：`https://api.nuget.org/v3/index.json`（仓库根目录 `nuget.config` 已配置）。
- 推荐 IDE：JetBrains Rider、Visual Studio 2022+ 或带 C# Dev Kit 的 VS Code。
- 本地运行需要可用的 ADB（platform-tools），详见 [docs/adb-requirements.md](docs/adb-requirements.md)。

## 项目结构 (Project Layout)

```
src/
  AndroidTreeView.Models/          领域模型（无项目依赖）
  AndroidTreeView.Core/            接口、选项、异常、核心服务
  AndroidTreeView.Adb/             ADB 执行器、命令、解析器、设备服务
  AndroidTreeView.Infrastructure/  设置持久化、日志、更新检查
  AndroidTreeView.App/             Avalonia 桌面应用（View / ViewModel / 控件 / 本地化）
tests/
  AndroidTreeView.*.Tests/         xUnit 单元测试
docs/                              架构、需求、ADB、打包等文档
.github/                           Issue / PR 模板、CODEOWNERS、工作流
```

## 构建与测试 (Build & Test)

在仓库根目录执行：

```bash
dotnet build                                  # 还原并编译整个解决方案
dotnet test                                   # 运行全部单元测试
dotnet run --project src/AndroidTreeView.App  # 本地运行桌面应用
```

发布自包含 Windows 可执行文件（用于打包 / 验证）：

```bash
dotnet publish src/AndroidTreeView.App -c Release -r win-x64 --self-contained true
dotnet publish src/AndroidTreeView.App -c Release -r win-x86 --self-contained true
```

提交前请确保 `dotnet build` 与 `dotnet test` 均通过。

## 代码风格 (Coding Style)

- 遵循根目录 `.editorconfig`：4 空格缩进、文件作用域命名空间（file-scoped namespace）、接口以 `I` 前缀命名、
  `using` 置于命名空间外、CRLF 行尾、UTF-8 编码。
- **严格 MVVM**：View 中不写业务逻辑或 ADB 调用；ViewModel 使用 CommunityToolkit.Mvvm 的
  `[ObservableProperty]` / `[RelayCommand]`。
- View 使用**编译绑定**（`x:DataType`），仅绑定 ViewModel 上已定义的成员。
- **不硬编码用户可见文案**：在 ViewModel 中使用 `ILocalizationService.Get("key")`，在 XAML 中使用
  `{loc:Localize Key=key}`，并同时补齐中英文（默认简体中文）资源。
- 异步优先：使用 `async` + `CancellationToken`；不要使用 `.Result` / `.Wait()`。
- 单文件不超过约 400 行；避免死代码、仅含 TODO 的空实现和无关重构。
- 只读、安全：不引入会修改 / 刷写设备的 ADB 命令。

## Issue 建议 (Issue Guidelines)

请优先使用 [Issue 模板](.github/ISSUE_TEMPLATE)，方便维护者获取必要上下文：

- 缺陷反馈：复现步骤、期望行为、实际行为、环境信息（操作系统、.NET 版本、ADB 版本、设备型号 / 地区 / 固件、
  Android 版本、连接方式），并尽量附上截图与日志。
- 功能建议：先说明产品问题，再提出交互或实现方案。
- 性能问题：尽量提供具体测量结果。
- 使用问题：说明已经尝试过的方案。

## Pull Request 建议 (Pull Request Guidelines)

- 行为或公共 API 变化时，同步更新文档与测试。
- 解析逻辑（ADB 输出解析）的改动应补充或更新对应的解析器单元测试。
- 面向 UI 的改动建议附带截图。
- 不要提交生成文件、构建产物（`bin/`、`obj/`）和本地 IDE 状态。
- 遵循 [Pull Request 模板](.github/PULL_REQUEST_TEMPLATE.md) 填写摘要、类型与验证项。

## 提交信息风格 (Commit Style)

推荐使用 Conventional Commit 风格：

```text
feat: add device detail page
fix: map unauthorized device state to friendly message
docs: document adb setup on windows
test: cover battery dumpsys parser cycle count
```

## 行为准则 (Code of Conduct)

参与本项目即表示你同意遵守 [行为准则](CODE_OF_CONDUCT.md)。
