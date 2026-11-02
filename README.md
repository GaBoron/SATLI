<p align="center">
  <img src="src/Satli.Gui/Assets/AppIcon.source.svg" width="112" height="112" alt="SATLI 图标">
</p>

<h1 align="center">SATLI</h1>
<p align="center">
  <strong>Steam 成就翻译管理器</strong><br>
  <sub>Steam Achievement Translation &amp; Localization Integrator</sub>
</p>

<p align="center">在 Windows 上查找、安装、编辑和管理 Steam 成就翻译。</p>

<p align="center">
  <a href="https://github.com/GaBoron/SATLI/releases/latest"><img alt="最新版本" src="https://img.shields.io/github/v/release/GaBoron/SATLI?display_name=tag&amp;sort=semver&amp;label=%E6%9C%80%E6%96%B0%E7%89%88%E6%9C%AC&amp;cacheSeconds=300"></a>
  <img alt="Windows 10 / 11" src="https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4">
  <a href="LICENSE"><img alt="MIT License" src="https://img.shields.io/badge/License-MIT-green.svg"></a>
</p>

<p align="center">
  <a href="https://get.microsoft.com/installer/download/9pb7v9s03k80?referrer=appbadge" target="_self"><img src="https://get.microsoft.com/images/zh-cn%20dark.svg" width="200" alt="从 Microsoft Store 获取"></a>
</p>

<p align="center">
  <a href="docs/USAGE.md">📖 使用指南</a> ·
  <a href="https://github.com/GaBoron/SATLI/issues/new/choose">💬 反馈问题</a> ·
  <a href="https://www.ifdian.net/a/gaboron">❤️ 赞助开发者</a> ·
  <a href="mailto:SATLI.support@proton.me">📧 获取支持</a>
</p>

> [!IMPORTANT]
> Steam 更新可能覆盖本机 BIN 成就缓存。如果只是希望在 Steam 中显示译文，建议使用独立的 [SATLI lite](https://github.com/GaBoron/SATLI-lite) 插件，无需改写 BIN；SATLI 桌面版也能管理 lite 译文，并保留 BIN 编辑、备份和恢复能力。

## 🚀 快速开始

1. 从 Microsoft Store 安装 SATLI，或在 [Releases](https://github.com/GaBoron/SATLI/releases/latest) 下载独立安装版。
2. 打开软件，在“云端”页搜索游戏并预览译本。
3. 选择“JSON（lite）”在 Steam 中显示译文，或选择“BIN”管理本机成就文件。首次使用 lite 时按界面指引安装并启用插件。

需要 Windows 10 2004 或更新版本、x64 处理器及 Steam。安装渠道、权限与操作说明见 [使用指南](docs/USAGE.md)。

## ✨ 主要功能

| 功能 | 说明 |
| --- | --- |
| 译本查找 | 搜索社区译本，预览不同语言与版本，支持离线缓存 |
| 安装与恢复 | 校验并安装 BIN，自动备份、检查状态和恢复文件 |
| lite 集成 | 安装和连接 SATLI lite，发送 JSON 译文、查看状态并编辑已有译文 |
| 翻译制作 | 编辑 BIN 或创建无 BIN 的 JSON 工程，支持 AI 参考翻译、导入和导出 |
| 社区协作 | 请求译本、导出投稿文件及报告过期或无效译本 |

## 🤝 翻译与项目生态

[SATLI lite](https://github.com/GaBoron/SATLI-lite) 负责 Steam 内的译文显示；[Steam 成就翻译库](https://github.com/GaBoron/steam-achievement-translation-library) 提供社区译本及投稿入口；[Steam Achievement Localizer Skill](https://github.com/GaBoron/steam-achievement-localizer-skill) 可辅助研究与制作译文。具体投稿步骤见 [使用指南](docs/USAGE.md#请求或贡献翻译)。

## 📚 文档

[使用指南](docs/USAGE.md) · [开发与命令行](docs/DEVELOPMENT.md) · [数据与隐私](docs/PRIVACY.md) · [第三方声明](THIRD_PARTY_NOTICES.md)

程序代码采用 [MIT License](LICENSE)。第三方组件及翻译数据的权利归属见 [第三方声明](THIRD_PARTY_NOTICES.md)。
