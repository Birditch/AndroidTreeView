# 拉取请求 (Pull Request)

## 摘要 (Summary)

<!-- 说明改动内容和原因 (What changed and why?). -->

## SIGN 声明 (SIGN Declaration)

- [ ] SIGN: 我确认我有权提交这些改动，并同意它们按本仓库许可证发布。
      (I certify that I have the right to submit this work and agree it is provided under this repository's license.)

## 类型 (Type)

- [ ] 缺陷修复 (Bug fix)
- [ ] 新功能 (Feature)
- [ ] 文档 (Documentation)
- [ ] 重构 (Refactor)
- [ ] 测试 (Test)
- [ ] 维护 (Maintenance)

## 细节 (Details)

<!-- 如有帮助，请补充实现说明、截图或 API 示例 (Add implementation notes, screenshots, or API examples if useful). -->

## 安全影响 (Security Impact)

- [ ] 未修改 GitHub Actions、发布脚本、打包脚本或二进制产物。
      (No GitHub Actions, release scripts, packaging scripts, or binary artifacts were changed.)
- [ ] 如修改了上述敏感区域，我已在摘要或细节中说明原因。
      (If sensitive areas were changed, I explained why in the summary or details.)

## 验证 (Verification)

- [ ] `dotnet build -c Release` 通过 (build succeeds).
- [ ] `dotnet test -c Release` 通过 (tests pass).
- [ ] 已运行 `dotnet format --verify-no-changes`（或已格式化）(code is formatted).
- [ ] 行为或 API 变化时，我已更新文档与本地化文案 (updated docs and localization strings when behavior or API changed).
- [ ] 我已考虑无障碍、性能和兼容性影响 (I considered accessibility, performance, and compatibility impact).

## 关联 Issue (Related Issues)

<!-- Closes #123 -->
