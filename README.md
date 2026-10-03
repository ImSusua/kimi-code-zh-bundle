# Kimi Code CLI 中文汉化离线安装包

一键安装 **Kimi Code CLI（月之暗面 Moonshot AI 的终端 AI 编程智能体）中文汉化版**：
**界面全量简体中文**（菜单 / 对话框 / 状态栏 / 帮助 / CLI 帮助），对话回复默认简体中文。

> ⬇️ **下载安装包**：见 [Releases](https://github.com/ImSusua/kimi-code-zh-bundle/releases) —— 下载 zip → 解压 → 双击 `安装.cmd`，全程可离线。

## 汉化说明

Kimi Code 官方目前没有终端界面 i18n（原生 exe 为编译产物，无法修改）。本包基于官方 npm 发行版的未压缩 JS bundle（`dist/main.mjs`）做**纯字符串原位替换**：

- 共替换约 **1770 处**用户可见字符串；对 `case` / `===` / 对象键等身份比较上下文的字符串做了系统性排除，**不改动任何程序逻辑**
- 覆盖范围：信任对话框、权限/计划模式菜单、模型/供应商选择器、插件管理、技能、目标（Goal）/Swarm/Tower、后台任务、`/help`、`kimi --help`、状态栏、更新流程、错误提示等
- 对话回复语言通过 `~/.kimi-code/AGENTS.md` 幂等标记块固定为简体中文（可自行修改/删除）
- 已知保留英文：首页服务端推送的动态公告（如 Desktop 推广横幅）不走本地字符串

实际界面效果（node-pty 抓屏验证）：

```
╭ 状态 ──────────────────────────────────────────╮
│ >_ Kimi Code (v2.1.1)                          │
│   模型          未设置                          │
│   目录          C:\...\your-project             │
│   权限          总是询问                         │
│   计划模式        关                             │
│   会话          无                              │
│ 上下文窗口                                       │
│   暂无上下文窗口数据。                            │
╰────────────────────────────────────────────────╯
```

## 内嵌组件

| 组件 | 版本 | 说明 |
|---|---|---|
| kimi-code | 2.1.1（npm JS 发行版） | 汉化版依赖树，SHA256 校验后离线展开 |
| Node.js | 22.23.3 便携版 | 内嵌运行时，仅为本包服务，不影响系统 Node |
| PortableGit | 2.56.0 | 仅当目标机没有 Git Bash 时静默解压（kimi-code 在 Windows 硬依赖 Git Bash），并设置官方支持的 `KIMI_SHELL_PATH` |

## 安装方法

1. 从 [Releases](https://github.com/ImSusua/kimi-code-zh-bundle/releases) 下载 zip，解压到任意本地目录
2. 双击 **`安装.cmd`**（全部用户级安装，**不需要管理员权限**），或：
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
   ```
3. **新开终端**，`kimi --version` 验证；进入项目目录运行 `kimi`
4. 首次使用在 kimi 内 **`/login`** 登录（Kimi 账号 OAuth 或 API Key）

安装位置遵循官方约定 `%USERPROFILE%\.kimi-code`；默认写 `mainland-cn` 区域标记（决定首次登录的 OAuth 端点）。

### 可选参数

| 参数 | 作用 |
|---|---|
| `-Repair` | 忽略已安装的同版本，强制重装 |
| `-OfflineOnly` | 禁止任何联网下载（内嵌资源损坏时直接报错） |
| `-GlobalRegion` | 区域标记写为 `global`（海外用户） |
| `-NoPath` | 不修改用户 PATH |

### 离线优先与镜像回退

默认全程使用内嵌资源**不联网**；内嵌资源缺失/校验失败时按序回退：
npmmirror（npm / Node / Git for Windows 二进制镜像）→ npmjs / nodejs.org 官方 → GitHub 加速代理（ghfast.top → ghproxy.net）→ 官方源。

## 常见问题

- **报 "Git Bash not found"**：重开终端让 `KIMI_SHELL_PATH` 生效；或手动安装 [Git for Windows](https://gitforwindows.org/)。
- **如何升级**：下载新版安装包重跑 `安装.cmd`。若用 `npm i -g @moonshot-ai/kimi-code@latest` 升级会得到**英文原版**。
- **想恢复英文原版**：`卸载.cmd` 后运行官方安装器 `irm https://code.kimi.com/kimi-code/install.ps1 | iex`。

## 卸载

双击 **`卸载.cmd`**：默认保留用户数据（配置 / 会话 / 登录态）；彻底清除加 `-PurgeAll`（含本包安装的 PortableGit）。

## 为新版本重新汉化

[`patch-pipeline/`](./patch-pipeline) 保留了完整的汉化补丁管线（提取 → 风险过滤 → 应用补丁 → pty 抓屏验证）。Kimi Code 发新版本后，可按 [patch-pipeline/README.md](./patch-pipeline/README.md) 重新生成补丁。

## 许可

- 本仓库脚本以 [MIT](./LICENSE) 提供；汉化补丁仅替换字符串，kimi-code 版权归 Moonshot AI（[MIT](https://github.com/MoonshotAI/kimi-code)）
- 内嵌组件版权归各自作者：Node.js（MIT）、Git for Windows（GPLv2）、PortableGit 镜像来自 npmmirror
