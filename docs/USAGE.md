# 使用指南

[返回项目首页](../README.md)

SATLI 用于管理 Windows 本机成就文件、制作翻译，以及通过 SATLI lite 在 Steam 中显示社区译文。如果只需要在 Steam 内使用译文，也可以直接安装 [SATLI lite](https://github.com/GaBoron/SATLI-lite)。

## 安装与首次运行

**推荐使用 [Microsoft Store](https://apps.microsoft.com/detail/9PB7V9S03K80)**，应用和更新由商店管理。无法使用商店时，可从 [GitHub Releases](https://github.com/GaBoron/SATLI/releases/latest) 下载独立安装版；安装器会在缺少依赖时从微软下载运行库。独立安装版未签名，初次运行可能遇到 SmartScreen 提示，请先核对下载来源。

两种安装渠道功能相同，建议只保留一个。切换渠道前先卸载旧版本；本地设置、草稿和备份不会主动删除。

首次启动会自动扫描 Steam 及本机成就缓存。未找到 Steam 时可在设置中指定目录；可选的 Steam Web API 用于补充游戏列表。通常无需管理员权限，只有向受保护目录写入、恢复文件或安装相关组件时才会请求 UAC 授权。

## 查找与使用社区译本

1. 在“云端”页搜索游戏名或 App ID，点击“查看成就”预览语言与版本。
2. 点击“安装”，选择译本、目标语言及安装方式；“可管理”页也提供批量入口。
3. 选择 **JSON（lite）** 时，SATLI 将译文交给 SATLI lite 显示；选择 **BIN** 时，SATLI 校验并备份本机文件，再安装所选译本。

JSON 路线无需修改 Steam BIN，BIN 路线也不依赖 lite。只浏览与预览译文时不需要安装 lite。已加载的列表和译文可以离线查看，首次下载仍需联网。

Steam 可能在更新后重建 BIN 缓存，因此更推荐使用 lite 显示译文。具体兼容性以网站和下载项标注为准。

## 使用 SATLI lite

可以在“设置 → Steam 译文显示 → 安装 / 管理 SATLI-lite”检查或安装插件，也可在选择 JSON 安装时按提示操作。SATLI 从 [lite 官方 Release](https://github.com/GaBoron/SATLI-lite/releases/latest) 获取插件；**Millennium 需要按其[官方指南](https://docs.steambrew.app/users/getting-started/installation)另行安装**。

安装后，在 **Steam → Millennium → Plugins** 启用 SATLI lite，并彻底退出、重新启动 Steam，再回到 SATLI 连接。

- 在“已管理 → JSON”查看 lite 当前译文，或打开为本地草稿编辑，再选择“发送到 SATLI-lite”。显示语言与开关在 lite 中设置。
- 覆盖 lite 已有译文前需要确认；已有本地编辑时会特别提示。未连接或状态变化时，可能需要重新查询再发送。
- Steam 未启动时，可以把已确认的请求排队，之后由 lite 处理。首次排队前，插件必须至少成功启动过一次。SATLI 无需常驻。
- 发送结果分为待处理、已接收、成功或失败等状态；实际显示效果仍请在 Steam 中确认。

“本地”页也能直接发送符合要求的 BIN/ZIP，编辑器可以发送当前草稿，无需先写回 Steam。曾使用旧版 **SATLI Achievement Display Bridge** 的用户，可通过设置中的迁移入口移除旧插件后启用 lite；迁移不会主动删除原有译文快照或 BIN 备份。

## 管理、编辑与恢复

“已管理 → BIN”提供安装记录、状态检查及恢复；“已管理 → JSON”显示 lite 的译文和请求状态。BIN 文件被 Steam 或其他软件改动后，普通恢复会拒绝覆盖；确认强制恢复时，会先归档当前文件。JSON 安装不创建 BIN 备份，也不使用 BIN 恢复操作。

在“本地”页选择游戏并点击“编辑”，可修改成就名称和说明、添加目标语言、对照其他语言，并自动保存草稿。写回本机前必须退出 Steam；如果源文件或成就集合已变化，应用会阻止直接覆盖。需要投稿时，可以导出 BIN、标准 ZIP 或独立 JSON。

## 制作、导入与贡献翻译

### 无 BIN 的翻译工程

在侧栏“翻译制作”中，可使用 Steam 公开成就数据、导入翻译库格式 1/2 JSON，或从在线译本创建工程。公开数据可能缺少隐藏成就；无法获取时需要补充来源。

JSON 工程支持翻译与校对，完成后可导出独立 JSON 或发送给 lite，不要求安装游戏或写入 Steam BIN。JSON 工程不支持直接写回 BIN。

### AI 参考翻译

在“设置 → AI 翻译”选择 OpenAI Responses、OpenAI Chat Completions 或 Anthropic Messages，填写服务地址、模型和 API Key。可在编辑器中生成待审译文，按字段接受后再人工校对。

模型测试及翻译可能产生服务商费用。SATLI 会向选定的服务发送游戏与成就相关文本，不上传完整 BIN 或 Steam 凭据；API Key 仅在本机加密保存。是否支持网页搜索取决于所选接口和模型，AI 输出不会自动安装或投稿。详细数据说明见 [数据与隐私](PRIVACY.md)。

### 导入与导出

“本地 → 导入本地翻译”支持标准命名的 BIN，或根目录仅含该 BIN 的 ZIP。编辑器、已管理译本及修订历史均可导出独立 JSON；“本地”页也可以直接将 BIN/ZIP 转换为 JSON，且无需先安装到 Steam。

导出的 JSON 使用 `UserGameStatsSchema_<app_id>.json` 命名，支持选择 `variant_id`，导出时会校验译文完整性。JSON 导出不会写回 BIN 或自动投稿。独立格式要求见翻译库的 [JSON 格式说明](https://github.com/GaBoron/steam-achievement-translation-library/blob/main/docs/translation-json.md)。

也可使用 [Steam Achievement Localizer Skill](https://github.com/GaBoron/steam-achievement-localizer-skill) 制作译本，再将其生成的文件导入 SATLI。

### 请求或贡献翻译

- **请求翻译：** 在云端查找不到译本时选择“未找到游戏？”，按提示填写语言、导出原始 schema ZIP，并在打开的 GitHub 表单中自行上传和提交。
- **贡献翻译：** 导出独立 JSON 或标准 ZIP，通过翻译库提供的新投稿或更新入口提交。
- **报告问题：** 在“云端”页选择游戏并点击“报告”，填写过期或不生效的情况，然后在浏览器中确认提交。

SATLI 只负责准备文件和打开表单，不会代替用户登录 GitHub、上传附件或提交 Issue。完整投稿条件见 [翻译库贡献指南](https://github.com/GaBoron/steam-achievement-translation-library/blob/main/docs/CONTRIBUTING.md)。

## 更新与网络

Microsoft Store 版通过商店更新；独立安装版可在“设置 → 软件更新”检查和下载安装包。两种渠道不会交叉安装更新。社区译本的更新与软件版本独立。

“设置”支持系统代理、HTTP/SOCKS 代理、自定义 DNS、下载镜像、连接测试和离线模式，也可以调整翻译目录与文件下载源顺序。网络不可用时，本地扫描、编辑及已有缓存仍可使用。

## 常见问题

**独立安装版被安全软件拦截？** 优先使用 Microsoft Store 版；无法使用时，从官方 [Releases](https://github.com/GaBoron/SATLI/releases/latest) 下载，并使用 `Get-FileHash '.\SATLI-Setup-vX.Y.Z.exe' -Algorithm SHA256` 核对发布说明中的哈希。不要仅凭通用检测名称判断文件安全，也不建议关闭防护或将整个安装目录加入排除项；仍有告警可向安全软件厂商提交复核。

**找不到本地游戏？** 先启动一次游戏，让 Steam 创建成就缓存，再重新扫描；必要时检查 Steam 路径。没有本机 BIN 时，仍可使用云端预览或 JSON 译文。

**云端列表或游戏名称无法加载？** 检查网络、代理和镜像设置，使用连接测试后重新获取。已有缓存仍可离线使用。

**GitHub 投稿或报告页面没有打开？** 检查默认浏览器与 GitHub 网络连接。表单需要用户自行登录和提交。

**草稿未恢复或无法保存编辑？** 草稿只适用于原有源文件与成就集合；Steam 修改文件后不能直接套用旧草稿。写回前请完全退出 Steam。

**安装后译文消失或恢复被拒绝？** Steam 可能重建 BIN 缓存，可改用 lite 或重新安装适用译本。恢复被拒绝通常说明目标文件已变化，应先确认差异；强制恢复前会归档当前文件。

面向源码和自动化使用者的命令行入口见 [开发与命令行](DEVELOPMENT.md)。
