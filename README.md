<p align="center">
  <img src="src/Satli.Gui/Assets/AppIcon.preview.png" width="112" alt="Steam 成就翻译管理器图标">
</p>

<h1 align="center">SATLI</h1>
<p align="center">
  <strong>Steam 成就翻译管理器</strong><br>
  <sub>Steam Achievement Translation &amp; Localization Integrator</sub>
</p>

<p align="center">
  在 Windows 上获取、导入、编辑、安装、恢复和管理 Steam 成就翻译。
</p>

<p align="center">
  <a href="https://github.com/GaBoron/SATLI/releases/latest"><img alt="最新版本" src="https://img.shields.io/github/v/release/GaBoron/SATLI?display_name=tag&amp;sort=semver&amp;label=%E6%9C%80%E6%96%B0%E7%89%88%E6%9C%AC&amp;cacheSeconds=300"></a>
  <img alt="Windows 10 / 11" src="https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4">
  <a href="LICENSE"><img alt="MIT License" src="https://img.shields.io/badge/License-MIT-green.svg"></a>
</p>

<p align="center">
  <a href="https://get.microsoft.com/installer/download/9pb7v9s03k80?referrer=appbadge" target="_self" >
	  <img src="https://get.microsoft.com/images/zh-cn%20dark.svg" width="200"/>
  </a>
</p>

<p align="center">
  <a href="docs/USAGE.md">📖 使用指南</a>
  ·
  <a href="https://github.com/GaBoron/SATLI/issues/new/choose">💬 反馈问题</a>
  ·
  <a href="https://www.ifdian.net/a/gaboron">❤️ 赞助开发者</a>
  ·
  <a href="mailto:SATLI.support@proton.me">📧 获取支持</a>
</p>

> [!IMPORTANT]
> **Steam 更新后，直接替换本地 `.bin` 成就缓存的方式已不稳定**，修改可能被 Steam 重新获取的成就数据覆盖，无法保证译文始终生效。
>
> 如果没有手动修改成就文本、管理原始 BIN 文件的需求，**建议优先使用 [SATLI lite](https://github.com/GaBoron/SATLI-lite)**。它通过 Millennium 插件直接替换 Steam 中显示的成就文字，不需要安装 SATLI 桌面版，也无需改写本地 `.bin` 文件。需要编辑或导出 BIN、制作投稿 ZIP、备份及恢复原始文件时，再使用本项目的桌面版。

## 下载与安装

SATLI 桌面版适合需要直接管理 BIN、制作投稿 ZIP、编辑原始成就文件或恢复本机文件的用户。若只需在 Steam 中显示成就译文，建议使用 [SATLI lite](https://github.com/GaBoron/SATLI-lite#前置要求)，并按其 README 中的安装说明操作。

> [!TIP]
> **强烈推荐从 [Microsoft Store](https://apps.microsoft.com/detail/9PB7V9S03K80) 安装。** 这是 SATLI 的首选安装方式：Microsoft Store 会负责安装并自动提供软件更新，应用设置页也支持主动检查 Store 新版、查看发布说明并打开更新页面。商店版还已确认不会触发目前已知的安全软件行为误报。

Microsoft Store 的产品可用性可能因市场而异。产品页面在当前市场不可用、无法使用 Microsoft Store，或希望手动管理安装程序时，可从 [GitHub Releases](https://github.com/GaBoron/SATLI/releases/latest) 获取独立安装版。独立安装器会检查 .NET 10 Desktop Runtime 和 Windows App Runtime，仅在缺少时从微软官方下载并安装；独立安装版通过应用内的 GitHub Releases 更新功能获取新版。

两个渠道提供相同的翻译管理功能并使用相同的数据格式，但不会相互安装对方的更新包。通常只需安装其中一个版本；切换渠道前请先退出并卸载当前版本。更多安装与更新说明见 [使用指南](docs/USAGE.md#安装与首次运行)。

> [!IMPORTANT]
> 部分安全软件可能把独立安装版的 `SATLI.exe` 误判为恶意程序并阻止启动。遇到这种情况，**首要解决方案是改用 Microsoft Store 版**；不要关闭安全防护，也不要排除整个安装目录。无法使用 Store 时，再按照 [安全软件报警怎么办](docs/USAGE.md#安全软件报警怎么办) 核对文件并向安全软件厂商提交误报复核。

## 快速开始

1. 打开应用，等待它扫描本机 Steam 游戏。
2. 在“云端”页点击游戏的“安装”，选择译本、语言和安装方式；本机已发现的游戏也可从“可管理”页进入。
3. 有 JSON 时可首次直接选择“JSON（lite）”；有 BIN 时也可独立选择“BIN”。

BIN 安装会在确认后关闭 Steam、校验译本并备份原文件。JSON 路线只向 lite 的公开本机接口导入译文并读取结果，不写入 Steam BIN；浏览、下载和预览不要求安装插件。首次安装 lite 或写入受保护目录时会请求管理员权限。

> [!IMPORTANT]
> Steam 可能重新获取成就数据并还原本地 BIN 修改。可选择通过 [SATLI-lite](https://github.com/GaBoron/SATLI-lite) 的独立 JSON 链路管理显示；这也是首次安装即可选择的正常路线。旧显示桥已退役，迁移步骤见[使用指南](docs/USAGE.md#通过-satli-lite-管理)。

> [!TIP]
> 找不到游戏时，请先启动一次游戏，让 Steam 生成成就缓存，再返回应用重新扫描。

## 核心能力

| | 能力 | 说明 |
| --- | --- | --- |
| 🔎 | 自动发现 | 扫描 Steam 目录和本机成就缓存，支持按名称或 App ID 搜索 |
| 🛡️ | 安全安装 | 安装前预览与校验，自动备份，支持批量安装、状态检查和安全恢复 |
| 🔒 | JSON 显示管理 | 浏览 BIN+JSON 与 JSON-only 译本，按需安装 SATLI-lite、发送译文并查询真实状态，SATLI 无需后台运行 |
| ✍️ | 本地编辑 | 按成就编辑目标语言，自动保存草稿，并导出标准 BIN 或投稿 ZIP |
| ☁️ | 社区协作 | 获取社区译本、请求新翻译、贡献成果，并报告过期或无效文件 |
| 🌐 | 稳定连接 | 支持离线缓存、系统代理、HTTP/SOCKS 代理、自定义 DNS 与镜像 |

社区译本和本地导入使用同一套校验、备份与恢复流程。详细操作见 [使用指南](docs/USAGE.md)。

## 系统要求

- Windows 10 版本 2004（Build 19041）或更高版本，推荐 Windows 11
- x64 处理器
- 已安装 Steam；使用离线缓存时可暂时不联网

无需手动安装 .NET、Windows App SDK 或 Git。Microsoft Store 会处理商店版的 Windows App Runtime 共享框架；独立安装器会在需要时联网补齐微软运行库。

## 请求或贡献翻译

- **没有现成译本：** 在应用中点击“未找到游戏？”，导出原始 schema ZIP，并前往翻译库提交翻译请愿。
- **已经完成翻译：** 从编辑页导出标准投稿 ZIP，再通过翻译库的投稿入口提交。
- **希望使用 Codex 制作：** 打开 [Steam Achievement Localizer Skill](https://github.com/GaBoron/steam-achievement-localizer-skill)，完成翻译后将其 `final/` 目录导入管理器。

详细流程和文件要求见 [制作、导入与贡献翻译](docs/USAGE.md#制作导入与贡献翻译)。

## 项目生态

以下项目提供 Steam 成就译本的制作、分发和使用工具：

| 项目 | 定位 | 适合场景 |
| --- | --- | --- |
| **Steam 成就翻译管理器**（当前项目） | Windows 图形化客户端 | 查找、安装、编辑和恢复翻译 |
| [SATLI lite](https://github.com/GaBoron/SATLI-lite) | 独立 Millennium 插件 | 在 Steam 内下载、切换、编辑和显示社区译本 |
| [Steam 成就翻译库](https://github.com/GaBoron/steam-achievement-translation-library) | 社区翻译数据仓库 | 查找、请求、提交和维护译本 |
| [Steam Achievement Localizer Skill](https://github.com/GaBoron/steam-achievement-localizer-skill) | Codex 翻译与审核工作流 | 研究多语言语境并制作可验证译本 |

Localizer Skill 生成的标准 BIN/ZIP 可直接导入管理器，也可提交到翻译库供社区使用。偏好独立桌面编辑器时，可了解第三方项目 [SteamAchievementLocalizer](https://github.com/PanVena/SteamAchievementLocalizer)。

SATLI 与 SATLI-lite 使用同一社区翻译库，分别保存设置和本地编辑。桌面端可主动发送在线 JSON 或可信的本地 BIN 译文到 lite；保存编辑不会自动覆盖 lite 中的内容。JSON 只包含显示文本，不能当作完整 BIN 安装。

## 文档

| 文档 | 内容 |
| --- | --- |
| [使用指南](docs/USAGE.md) | 安装、编辑、恢复、投稿、网络设置与常见问题 |
| [数据与隐私](docs/PRIVACY.md) | 本地数据、凭据、日志和网络请求说明 |
| [第三方声明](THIRD_PARTY_NOTICES.md) | 随软件分发的第三方组件和权利说明 |

## 联系与支持

遇到使用问题时，可通过 [GitHub Issues](https://github.com/GaBoron/SATLI/issues/new/choose) 反馈，或发送邮件至 [SATLI.support@proton.me](mailto:SATLI.support@proton.me)。

## 许可证

程序代码采用 [MIT License](LICENSE)。第三方组件和翻译数据的权利说明见 [第三方声明](THIRD_PARTY_NOTICES.md)。
