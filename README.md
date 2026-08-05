<p align="center">
  <img src="src/Satl.Gui/Assets/AppIcon.preview.png" width="112" alt="Steam 成就翻译管理器图标">
</p>

<h1 align="center">Steam 成就翻译管理器</h1>

<p align="center">
  在 Windows 上查找、编辑、安装并安全恢复 Steam 成就翻译。
</p>

<p align="center">
  <a href="https://github.com/GaBoron/steam-achievement-translation-installer/releases/latest"><img alt="最新版本" src="https://img.shields.io/github/v/release/GaBoron/steam-achievement-translation-installer?label=%E6%9C%80%E6%96%B0%E7%89%88%E6%9C%AC"></a>
  <img alt="Windows 10 / 11" src="https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4">
  <a href="LICENSE"><img alt="MIT License" src="https://img.shields.io/badge/License-MIT-green.svg"></a>
</p>

<p align="center">
  <a href="https://github.com/GaBoron/steam-achievement-translation-installer/releases/latest">⬇️ 下载独立安装版</a>
  ·
  <a href="docs/USAGE.md">📖 使用指南</a>
  ·
  <a href="https://github.com/GaBoron/steam-achievement-translation-installer/issues/new/choose">💬 反馈问题</a>
</p>

## 🚀 快速开始

1. 选择一种安装渠道：在 Microsoft Store 搜索 `SATLI`，或从 [GitHub Releases](https://github.com/GaBoron/steam-achievement-translation-installer/releases/latest) 下载独立安装版。
2. 打开应用，等待它扫描本机 Steam 游戏。
3. 选择游戏和译本，预览成就文本后点击“安装所选”。

Microsoft Store 版与独立安装版提供相同的翻译管理功能，并使用相同的数据格式。通常只需安装其中一种：Microsoft Store 版由 Store 更新，独立安装版通过应用内的 GitHub Releases 更新。两个版本各自只接收所属渠道的软件更新，不会交叉更新。

安装翻译前会自动校验并备份原文件；之后可在“已管理”页查看状态或恢复。浏览、扫描和编辑草稿不会请求管理员权限；确认安装、恢复或写回本地编辑时，Windows 才会显示 UAC。独立安装版首次运行若遇到 Windows SmartScreen 提示，请核对下载来源及 Release 中的 SHA-256。

> [!TIP]
> 找不到游戏时，请先启动一次游戏，让 Steam 生成成就缓存，再返回应用重新扫描。

## ✨ 核心能力

| | 能力 | 说明 |
| --- | --- | --- |
| 🔎 | 自动发现 | 扫描 Steam 目录和本机成就缓存，支持按名称或 App ID 搜索 |
| 🛡️ | 安全安装 | 安装前预览与校验，自动备份，支持批量安装、状态检查和安全恢复 |
| ✍️ | 本地编辑 | 按成就编辑目标语言，自动保存草稿，并导出标准 BIN 或投稿 ZIP |
| ☁️ | 社区协作 | 获取社区译本、请求新翻译、贡献成果，并报告过期或无效文件 |
| 🌐 | 稳定连接 | 支持离线缓存、系统代理、HTTP/SOCKS 代理、自定义 DNS 与镜像 |

社区译本和本地导入使用同一套校验、备份与恢复流程。更完整的操作说明见 [使用指南](docs/USAGE.md)。

## 🧩 项目生态

三个项目共同覆盖 Steam 成就翻译的完整流程：

**制作与审核译本** → **社区收录与分发** → **安装、编辑与恢复**

| 项目 | 定位 | 适合场景 |
| --- | --- | --- |
| **Steam 成就翻译管理器**（当前项目） | Windows 图形化客户端 | 查找、安装、编辑和恢复翻译 |
| [Steam 成就翻译库](https://github.com/GaBoron/steam-achievement-translation-library) | 社区翻译数据仓库 | 查找、请求、提交和维护译本 |
| [Steam Achievement Localizer Skill](https://github.com/GaBoron/steam-achievement-localizer-skill) | Codex 翻译与审核工作流 | 研究多语言语境并制作可验证译本 |

Localizer Skill 生成的标准 BIN/ZIP 可直接导入管理器，也可提交到翻译库供社区使用。偏好独立桌面编辑器时，可了解第三方项目 [SteamAchievementLocalizer](https://github.com/PanVena/SteamAchievementLocalizer)。

## 🤝 请求或贡献翻译

- **没有现成译本：** 在应用中点击“未找到游戏？”，导出原始 schema ZIP，并前往翻译库提交翻译请愿。
- **已经完成翻译：** 从编辑页导出标准投稿 ZIP，再通过翻译库的投稿入口提交。
- **希望使用 Codex 制作：** 打开 [Localizer Skill](https://github.com/GaBoron/steam-achievement-localizer-skill)，完成翻译后将其 `final/` 目录导入管理器。

详细流程和文件要求见 [制作、导入与贡献翻译](docs/USAGE.md#制作导入与贡献翻译)。

## 📚 文档

| 文档 | 内容 |
| --- | --- |
| [📖 使用指南](docs/USAGE.md) | 安装、编辑、恢复、投稿、网络设置与常见问题 |
| [🔐 数据与隐私](docs/PRIVACY.md) | 本地数据、凭据、日志和网络请求说明 |
| [📦 第三方声明](THIRD_PARTY_NOTICES.md) | 随软件分发的第三方组件和权利说明 |

## 🖥️ 系统要求

- Windows 10 版本 2004（Build 19041）或更高版本，推荐 Windows 11
- x64 处理器
- 已安装 Steam；使用离线缓存时可暂时不联网

发布包已包含所需运行组件，无需另外安装 .NET、Windows App SDK 或 Python。

## 📄 许可证

程序代码采用 [MIT License](LICENSE)。第三方组件和翻译数据的权利说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
