# AndroidTreeView — Publishing & GitHub Releases

> 中文摘要：本文档说明 v1.0.0 的发布流程——版本对齐、构建 win-x64 / win-x86 的 MSI、生成
> 校验和、创建 GitHub Release 并上传产物，以及与自动更新检查（`GitHubUpdateService`）的
> 配合方式。**v1 阶段不要打 tag、不要发布 Release。**

Repository: **`Birditch/AndroidTreeView`** (see `src/AndroidTreeView.Core/AppInfo.cs`).

> **v1 policy:** Do **not** push a `v*` tag and do **not** create a GitHub Release in this
> pass. The instructions below document the flow for when a real release is cut later.

---

## 1. How releases are triggered

CI is split into two workflows under `.github/workflows/` (authored by the CI/OSS agent):

* **`ci.yml`** — runs on every push and pull request: restore, format check, vulnerability
  scan, build, test. It does **not** publish anything.
* **`publish.yml`** — runs **only on tags matching `v*`** (e.g. `v1.0.0`). It reuses the
  build/test steps, then packages and publishes the release. It must never run on ordinary
  commits.

Because `publish.yml` is tag-gated, the entire release is produced by pushing an annotated
tag whose name matches the app version with a leading `v`.

---

## 2. Release artifacts produced by `publish.yml`

For each architecture (`x64`, `x86`) the workflow runs the same packaging path documented in
[`packaging.md`](./packaging.md):

```powershell
./packaging/build-msi.ps1 -Arch x64
./packaging/build-msi.ps1 -Arch x86
```

Resulting uploads attached to the GitHub Release:

| Asset | Notes |
| --- | --- |
| `AndroidTreeView-1.0.0-x64.msi` | 64-bit installer |
| `AndroidTreeView-1.0.0-x64.msi.sha256` | SHA-256 checksum |
| `AndroidTreeView-1.0.0-x86.msi` | 32-bit installer |
| `AndroidTreeView-1.0.0-x86.msi.sha256` | SHA-256 checksum |

> Decide once whether the shipped MSIs are self-contained (add `-SelfContained`) or
> framework-dependent, and keep it consistent across releases and the README download notes.
> Self-contained is recommended for the simplest end-user install.

Optionally also attach the Burn bootstrapper `AndroidTreeView-1.0.0-<arch>-setup.exe`
(from `Bundle.wxs`) when shipping framework-dependent builds — see `packaging.md` §5.

---

## 3. Manual release checklist (when a release is authorized)

1. **Align the version** in all three places (must be identical):
   * `src/AndroidTreeView.Core/AppInfo.cs` → `AppInfo.Version`
   * `src/AndroidTreeView.App/*.csproj` → `<Version>` / `<InformationalVersion>`
   * the tag name minus the leading `v` (tag `v1.0.0` ⇒ version `1.0.0`)
2. **Green CI** on the target commit (build + tests pass).
3. **Build + smoke-test the MSIs locally** (see `packaging.md`); install on a clean VM and
   confirm the app launches and the shortcut works.
4. **Write release notes** (Chinese-first, English section) — highlights, install/runtime
   notes, checksums.
5. **Create and push the tag**, which triggers `publish.yml`:

   ```bash
   git tag -a v1.0.0 -m "AndroidTreeView 1.0.0"
   git push origin v1.0.0
   ```

6. **Watch the workflow** to completion; confirm all four (or six) assets and checksums are
   attached to the Release.

### Fully manual fallback (no Actions)

If you must cut a release by hand:

```powershell
./packaging/build-msi.ps1 -Arch x64
./packaging/build-msi.ps1 -Arch x86
gh release create v1.0.0 `
    artifacts/AndroidTreeView-1.0.0-x64.msi `
    artifacts/AndroidTreeView-1.0.0-x64.msi.sha256 `
    artifacts/AndroidTreeView-1.0.0-x86.msi `
    artifacts/AndroidTreeView-1.0.0-x86.msi.sha256 `
    --title "AndroidTreeView 1.0.0" --notes-file RELEASE_NOTES.md
```

---

## 4. Interaction with the in-app update checker

`AndroidTreeView.Infrastructure.Update.GitHubUpdateService` (`IUpdateService`) queries
`https://api.github.com/repos/Birditch/AndroidTreeView/releases/latest` and compares the
latest tag against `AppInfo.Version` with a lenient SemVer comparison (a leading `v` is
tolerated). For the update banner and "Check for Updates" button to work correctly:

* Tag releases as `vMAJOR.MINOR.PATCH` (e.g. `v1.0.0`); the comparer strips the `v`.
* Publish the release as a **normal (non-draft, non-prerelease)** release so
  `/releases/latest` returns it.
* The release page URL surfaced to users is `AppInfo.ReleasesUrl`
  (`https://github.com/Birditch/AndroidTreeView/releases`); "View Release" opens the specific
  release URL returned by the API.

No auto-download / auto-install happens in v1 — the app only informs the user and opens the
release page in the browser.
