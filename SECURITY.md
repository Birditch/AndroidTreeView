# 安全策略 (Security Policy)

AndroidTreeView 是一个基于 .NET 10 + Avalonia 的桌面应用，通过 ADB **只读**展示已连接 Android 设备的信息。
它不会修改、刷写或破坏设备，也不在源码中存放任何密钥。即便如此，安全问题依然会被认真对待。
请不要用公开 Issue 报告安全漏洞。

## 数据与权限说明 (Data & Permissions)

- 应用仅通过标准 ADB 命令读取设备信息，**不修改设备**。
- 设备信息仅在本机展示，不会上传到任何服务器。
- 更新检查会访问 GitHub Releases 公共 API（不发送设备数据）。
- 源码中不包含任何凭证 / 令牌 / 私钥。

## 支持版本 (Supported Versions)

| 版本 (Version) | 支持状态 (Supported) |
| --- | --- |
| 1.0.x | 接收安全修复 (Security fixes) |
| main（开发分支 / development） | 尽力处理 (Best effort) |
| < 1.0.0 | 不再支持 (Unsupported) |

## 报告漏洞 (Reporting a Vulnerability)

请优先使用 GitHub 的私密漏洞报告流程：

https://github.com/Birditch/AndroidTreeView/security/advisories/new

报告中建议包含：

- 问题的清晰描述。
- 复现步骤或概念验证。
- 受影响的版本、提交或分支。
- 已知影响范围和可能的缓解方式。

维护者会尽力审查安全报告，并在适合的情况下先私下协调修复，再公开披露。请勿在修复公开前披露漏洞细节。
