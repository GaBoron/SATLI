<p align="center">
  <img src="src/Satl.Gui/Assets/AppIcon.preview.png" width="112" alt="Steam 成就翻译管理器图标">
</p>

# Steam 成就翻译管理器

一款面向 Windows 10/11 的 Steam 成就翻译管理工具。它可以自动找到本机游戏，从社区翻译库选择合适版本，并安全完成安装、状态检查与恢复。

[![最新版本](https://img.shields.io/github/v/release/GaBoron/steam-achievement-translation-installer?label=最新版本)](https://github.com/GaBoron/steam-achievement-translation-installer/releases/latest)
[![系统](https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4)](#系统要求)
[![许可证](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

## 下载与安装

请从 [GitHub Releases](https://github.com/GaBoron/steam-achievement-translation-installer/releases/latest) 下载。

下载后，运行安装程序，之后从开始菜单打开。
直接覆盖安装新版即可；安装程序会在复制新文件前清理旧版遗留的运行组件。

本项目暂未提供代码签名。Windows SmartScreen 可能首次显示安全提醒；请核对下载来源和 Release 中的 SHA-256 后再运行。

## 快速开始

1. 打开软件，等待它扫描本机 Steam 游戏。
2. 勾选需要翻译的游戏，确认翻译版本。
3. 点击“安装所选”，逐页检查每个游戏即将写入的 BIN 成就表格，再确认执行。

如果译本由 [Steam Achievement Localizer Skill](https://github.com/GaBoron/steam-achievement-localizer-skill) 生成，可在“本地”页点击“导入本地翻译”，直接选择其 `final/` 目录中的标准 BIN 或 ZIP；应用会先校验和预览，再沿用相同的备份、安装与恢复流程。

“已管理”页面会显示 SATL 管理的游戏、安装来源、当前状态和已安装版本。社区译本与本地导入译本都可以直接查看当前成就文本或恢复安装前文件；如果文件之后被其他程序修改，软件会先拒绝普通恢复，只有在你明确确认后才执行强制恢复并归档当前文件。

## 主要功能

### 1. 扫描与查找

- 自动检测 Steam 目录和本机成就缓存，也可手动指定 Steam 路径。
- 将本地游戏与社区译本分开显示，并支持按游戏名或 App ID 搜索。
- 可选使用 Steam Web API 补全账号拥有、但本机还没有成就缓存的游戏。
- 在线补全游戏名称；离线时继续使用已有缓存。

### 2. 安装与恢复

- 安装前预览译本、目标语言和 BIN 中的成就内容。
- 支持导入规范命名的本地 `UserGameStatsSchema_<app_id>.bin`，以及根目录仅含该 BIN 的标准 ZIP。
- 本地导入会校验 ZIP 结构、Binary KeyValues 字节级 roundtrip 与预览 SHA-256，确认后文件如有变化将拒绝安装。
- “已管理”页区分社区译本和本地导入译本，并为每项提供当前内容查看与恢复入口。
- 支持批量安装、重复安装，以及同一游戏的多个候选译本。
- 自动标记正常、可更新、缺失、已修改和缺少备份等状态。
- 写入前自动备份；可普通恢复，也可在归档当前文件后强制恢复。

### 3. 本地成就编辑

- 在“本地”页点击“编辑”，按成就 API ID 修改目标语言的名称和说明。
- 支持搜索、选择只读对照语言，以及编辑现有语言或新增合法的 Steam 语言。
- 自动保存编辑草稿；源文件和成就列表没有变化时，再次进入会恢复草稿。
- 保存前检查源文件、成就集合和文本内容；允许不完整翻译，但会明确提示缺失项。
- 关闭 Steam 后可安全写回本机，并可连续恢复最近几次编辑。
- 可导出校验后的 BIN，或根目录仅含同名 BIN 的标准投稿 ZIP。

### 4. 云端协作

- 从 [Steam 成就翻译库](https://github.com/GaBoron/steam-achievement-translation-library) 下载并安装社区译本。
- 云端文件有问题时，可绑定自己的 GitHub 账号并在应用内提交报告。
- 没有译本时可生成翻译请愿内容；完成翻译后可生成标准投稿 ZIP。

### 5. 日常使用

- 切换“本地”和“云端”页面时复用已加载结果，需要时再手动刷新。
- 支持离线浏览最近成功加载的云端列表。
- 支持系统代理、HTTP/SOCKS 代理、自定义 DNS、镜像和网络测试。
- 提供更新检查、下载校验、实时日志、深浅色主题和常用键盘操作。

## 请求或贡献翻译

没有云端译本时，点击搜索页的“未找到游戏？”，填写 Steam App ID 并导出原始 schema ZIP，然后使用“提交翻译请愿”前往翻译库表单。

如果你已经完成翻译，可从编辑页或投稿流程导出标准 ZIP，再点击“贡献翻译”提交。ZIP 根目录只包含一个 `UserGameStatsSchema_<app_id>.bin`，文件名、App ID、文件结构和语言内容会在生成或投稿时校验。

导出不会修改 Steam 文件。如果找不到原始文件，请先启动对应游戏，让 Steam 生成一次成就缓存。

## 使用 Localizer Skill 制作并导入翻译

“本地”页的“制作翻译”会打开 [Steam Achievement Localizer Skill](https://github.com/GaBoron/steam-achievement-localizer-skill)。该 skill 负责研究多语言语境、制作翻译、无损写入和验证，并在项目的 `final/` 目录生成：

- `UserGameStatsSchema_<app_id>.bin`
- `UserGameStatsSchema_<app_id>.zip`
- `report.json`

返回安装器后点击“导入本地翻译”，选择 BIN 或 ZIP 即可。安装器不会修改所选源文件；它会检查文件名与 App ID、ZIP 单文件结构、schema roundtrip、成就内容和 SHA-256，显示逐项预览，并在确认后写入 Steam。写入前仍会创建 SATL 备份，因此可以在“已管理”页查看当前译文，或通过逐项及批量入口恢复。

## 报告云端文件问题

首次使用时，按应用提示绑定自己的 GitHub 账号即可。

1. 在“云端”页找到游戏并点击“报告”。
2. 首次使用时点击“绑定 GitHub”，按提示在浏览器中完成授权。
3. 选择“文件可能过期”或“文件可能不生效”，填写问题说明。
4. 检查最终预览并提交。

提交成功后会显示对应的 GitHub Issue 链接。提交失败时，当前填写内容会保留，方便检查网络或重新绑定后再次提交。

报告只包含游戏名、App ID、商店链接、问题类型、说明和可选参考来源，不会上传本机成就文件。你可以随时在设置中解绑；解绑会删除本机保存的凭据。如需同时撤销 GitHub 端授权，可打开设置中提供的授权管理页面。

## 更新

“设置 → 软件更新”可以检查稳定版、查看发布说明、下载安装包并校验 SHA-256。软件不会静默下载；只有点击“下载并安装”后才会开始。无法访问 GitHub 时，更新检查失败不会影响扫描、安装、编辑或恢复。

## 数据与隐私

默认数据目录：

```text
%LOCALAPPDATA%\SteamAchievementTranslationInstaller\
```

这里会保存设置、窗口位置、游戏名称与云端列表缓存、安装和编辑历史、编辑草稿、备份及可选日志。本地编辑只修改你选择的目标语言，不会把本机 schema 上传到网络。

Steam Web API Key、GitHub 访问令牌和刷新令牌都使用 Windows DPAPI CurrentUser 加密保存，仅限当前 Windows 用户解密。日志不会记录这些密钥、令牌、设备码、完整授权响应或成就文件正文。

日志可在设置中关闭，并可选择普通、详尽或临时 Debug 级别。Debug 可能包含本机目录、Steam 路径、App ID 和游戏名等诊断信息；向他人发送日志前请先检查内容。

## 系统要求

- Windows 10 版本 2004（Build 19041）或更高版本，推荐 Windows 11。
- x64 处理器。
- 已安装 Steam；使用离线缓存时可暂时不联网。

发布包包含所需的 .NET、Windows App SDK 与 Python 官方嵌入式运行文件，无需另外安装运行库。

## 常见问题

### 1. 找不到本地游戏

先启动一次对应游戏，让 Steam 生成成就缓存，然后返回应用重新扫描。仍未找到时，请在设置中确认 Steam 路径。也可以配置 Steam Web API 来补全账号游戏库，但没有本机成就文件的游戏暂时不能编辑或安装。

### 2. 游戏名称没有正常显示

名称查询需要网络。检查代理或镜像设置后重新获取；离线时应用会显示已有缓存或 App ID，不影响已经可用的本地文件。

### 3. 云端列表加载失败

使用设置页的网络测试检查 GitHub、代理或镜像连接。应用会尽量显示最近一次成功加载的云端列表；恢复网络后可手动刷新。

### 4. 无法绑定 GitHub

确认网络可以访问 GitHub，并及时在浏览器中完成授权。如果授权被取消、过期或原有会话已失效，请返回应用重新绑定。

### 5. 报告提交失败

检查网络和报告预览。如果 GitHub 授权已撤销或登录状态失效，应用会提示重新绑定；重新绑定后可继续使用当前报告内容。

### 6. 编辑草稿没有恢复

草稿只适用于同一个源文件和同一组成就。游戏更新、Steam 重建文件或成就列表变化后，旧草稿不会自动套用，以免把内容写到错误的成就上。

### 7. 为什么无法保存本地编辑

保存前必须完全退出 Steam。若源文件在编辑期间发生变化，应用会拒绝覆盖，请重新进入编辑页并确认最新内容。

### 8. 安装后翻译消失

Steam 或游戏更新可能重新生成成就缓存。重新扫描并再次安装即可；如果云端译本已经过期或不再生效，可以直接使用“报告”提交问题。

### 9. 为什么恢复被拒绝

目标文件在操作后发生了变化。为避免覆盖其他程序或 Steam 的新数据，普通恢复会停止。确认需要覆盖时可使用强制恢复，当前文件会先被归档。

## 相关项目

- [Steam 成就翻译库](https://github.com/GaBoron/steam-achievement-translation-library)：查找和提交社区翻译数据。
- [Steam Achievement Localizer Skill](https://github.com/GaBoron/steam-achievement-localizer-skill)：使用 Codex 辅助制作与审核翻译。
- [SteamAchievementLocalizer](https://github.com/PanVena/SteamAchievementLocalizer)：本地可视化翻译编辑器。

## 开发与构建

开发环境需要 Windows 10/11 x64、Python 3.13、.NET 10 SDK、WinUI 3 工具链和 Inno Setup 6。

```powershell
python -m venv .venv
.\.venv\Scripts\pip.exe install -e ".[dev]"
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

构建结果位于 `dist\release\`：安装版 EXE 和 `SHA256SUMS.txt`。

## 统计

![Alt](https://repobeats.axiom.co/api/embed/ce4cc9d3800947708218fb165750a35fb9f37083.svg "Repobeats analytics image")

## 许可证

程序代码采用 [MIT License](LICENSE)。第三方组件和翻译数据的权利说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
