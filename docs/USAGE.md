# 使用指南

[返回项目首页](../README.md)

本指南介绍 SATLI 的安装渠道、主要操作、更新方式和常见问题。

## 目录

- [安装与首次运行](#安装与首次运行)
- [查找和安装社区译本](#查找和安装社区译本)
- [管理与恢复](#管理与恢复)
- [编辑本地成就](#编辑本地成就)
- [制作、导入与贡献翻译](#制作导入与贡献翻译)
- [报告云端文件问题](#报告云端文件问题)
- [开发与自动化命令](#开发与自动化命令)
- [软件更新与网络](#软件更新与网络)
- [常见问题](#常见问题)

## 安装与首次运行

本指南适用于 SATLI 桌面客户端。只需在 Steam 中下载、切换和显示社区译本时，可选择独立的 [SATLI lite Millennium 插件](https://github.com/GaBoron/SATLI-lite)，按其安装说明操作，无需安装本体。需要制作 BIN、导出投稿 ZIP 或管理本机原文件时，请使用以下桌面客户端安装渠道。

### Microsoft Store（强烈推荐）

Microsoft Store 是 SATLI 的首选安装方式。从 [Microsoft Store](https://apps.microsoft.com/detail/9PB7V9S03K80) 打开产品页面，选择“获取”或“安装”。Microsoft Store 会负责安装应用及其 Windows App Runtime 共享框架，并在有新版时自动提供更新。商店版已经确认不会触发目前已知的安全软件行为误报；如果独立安装版被安全软件拦截，请优先改用商店版。

产品可用性可能因市场而异；页面显示当前市场不可用时，请改用独立安装版。

### 独立安装版

无法使用 Microsoft Store，或希望手动管理安装程序时，可从 [GitHub Releases](https://github.com/GaBoron/SATLI/releases/latest) 下载安装程序。安装器会检查 .NET 10 Desktop Runtime 和 Windows App Runtime 2.2；只有本机缺少对应运行库时，才会从微软官方下载并校验后静默安装，因此首次安装可能需要联网。独立安装版暂未提供代码签名，Windows SmartScreen 可能在首次运行时显示安全提醒；请核对下载来源后再运行。

独立安装版可以直接覆盖安装新版，也可以使用“设置 → 软件更新”获取更新。

### 切换安装渠道

两个渠道提供相同的翻译、编辑、备份和恢复功能，但不会相互安装对方的更新包。通常只安装其中一个版本，不建议同时运行两个版本。

需要更换渠道时，请先退出并卸载当前版本，再安装另一版本。保存在本机数据目录中的设置、缓存、草稿、历史和备份不会因为更换安装渠道而主动删除。

### 首次运行与权限

首次打开后，应用会自动检测 Steam 目录并扫描本机成就缓存；首屏扫描完成后，会以最多两个并发任务在后台预加载本地与云端游戏清单，打开对应页面时直接复用结果。也可以在设置中手动指定 Steam 路径，或选择配置 Steam Web API，以补全账号拥有但本机尚无缓存的游戏。

应用以普通用户权限启动。扫描、预览、设置、日志、编辑草稿和导出文件不会触发 UAC；只有确认安装、恢复、写回本地编辑或激活历史修订时，才会请求管理员权限。独立安装版安装软件更新时也会请求管理员权限；Microsoft Store 版的软件更新由 Store 处理。取消 UAC 后，本次操作会停止，应用仍可继续使用。

## 查找和安装社区译本

1. 打开“云端”页，按名称或 Steam App ID 查找全部已发布译本；没有本机 schema 的游戏也可以查看。
2. 点击“查看成就”预览各版本；点击“安装”选择 Variant、语言和安装方式。“可管理”页的“安装所选”也提供相同选择。
3. 有 BIN+JSON 时，可首次直接选择“JSON（lite）”，也可选择“BIN”。JSON-only 只提供 JSON 路线。
4. 仅需查看时点击“查看成就”，直接读取 JSON 并复用已校验缓存，无需 Millennium、lite 或 Steam 本地 BIN。列表中的 BIN、JSON 徽标表示可用文件格式。

JSON 格式 1 按目录的大小、ID、语言、成就数和源 BIN 标识校验，不要求下载源 BIN；格式 2 按 JSON 自身的大小和哈希校验。两种格式都检查结构与成就 API name。校验失败会停止应用，不会改扩展名后写入 BIN。

选择 BIN 时，保留原有预览、关闭 Steam、备份、完整性检查及恢复机制，不要求安装 lite。选择 JSON 时，按下方流程连接 lite，不执行 BIN 安装或建立虚假的备份记录。两条路线不会自动互相切换。

在线列表与已校验的 JSON 保留缓存；离线时仍可浏览最近一次结果和缓存预览。获取新文件、首次安装 lite 需要联网。

## 管理与恢复

“已管理”页分别显示 BIN 安装/编辑记录与 lite 返回的译文状态。BIN 记录标记以下状态：

- 正常
- 可更新
- 文件缺失
- 文件已被修改
- 缺少备份

BIN 记录可查看当前成就文本或恢复安装前文件，JSON 安装没有对应的 BIN 恢复操作。目标文件若被 Steam 或其他程序修改，普通恢复会停止，以免覆盖新数据；只有在明确确认后才能强制恢复，且当前文件会先归档。

### 通过 SATLI-lite 管理

[SATLI-lite](https://github.com/GaBoron/SATLI-lite) 负责 Steam 内的译文显示，桌面端不再部署自己的显示桥。JSON 首次安装无需已有 BIN 安装、已管理或锁定记录。

**独立安装入口：** 打开“设置 → Steam 译文显示 → 安装 / 管理 SATLI-lite”，即可检查安装、按需下载和查看启用指引，无需先选择游戏或发送译文。未安装 Millennium 时只提供官方指南，不会捆绑或自动安装它。安装 lite 会从其官方最新正式 Release 获取 `satli-lite.star`，核对资产、下载地址、大小和完整性后原子保存到当前 Steam 目录的 `millennium/plugins`。已有插件会原样保留，后续更新由 lite 自身负责；安装成功不代表已应用译文。

1. 从在线译本选择 JSON 路线，核对目标语言与成就文本并确认。
2. 发送前会核对 Millennium、lite 安装、协议能力和当前心跳。缺少 Millennium 时提供[官方安装指南](https://docs.steambrew.app/users/getting-started/installation)，SATLI 不捆绑安装 Millennium。
3. 缺少 lite 时可确认下载官方正式 Release 的 `satli-lite.star`。安装文件会校验大小和完整性，必要时请求管理员权限。已有插件不会被重复下载或强制覆盖。
4. 在 **Steam → Millennium → Plugins** 启用 SATLI-lite，并完全退出、重启 Steam。返回 SATLI 重试连接；文件存在不代表插件已经可通信。
5. lite 已有译本时，SATLI 会先查询状态，并单独确认是否覆盖，包含本地编辑时会明确说明。状态在确认后变化会被拒绝，需要重新查询和确认。
6. 查看返回结果；目录版本比较单独显示，含 lite 本地编辑或来源不明时提示核对。请求已投递、等待处理、处理失败、已保存和显示配置已启用是不同状态。实际名称和描述仍需在 Steam 中查看。SATLI 可以退出，无需常驻。

“已管理 → SATLI-lite”显示经 SATLI 查询或导入的 lite 译文，包括从未安装过 BIN 的游戏。启动时的历史结果会标为“上次处理结果”，可选择“刷新 lite 状态”查询当前状态；插件尚无公开的全部译本枚举接口，尚未由 SATLI 查询的其他游戏可从云端列表主动查询。游戏和全局显示开关在 Steam 的 SATLI-lite 中管理。

Steam 未运行或 lite 暂无有效心跳时，可选择“排队发送”，之后启动 Steam 并启用插件即可继续处理，SATLI 无需常驻。排队要求已安装的 lite 至少运行过一次，留下兼容的公开协议；尚未初始化时先提示启用插件。离线模式也可发送已缓存的 JSON 或本地译文。未连接时不授予覆盖权限：lite 若已有译本，会返回冲突，需要连接后重新查询和确认。

状态分别显示“已保存待发送”“已投递”“lite 已接收”“处理成功”和失败错误码。超时不会当作失败，刷新沿用原请求 ID；只有原请求结束后，才会建立新的查询或发送请求。状态查询也会保留最近一次发送的结果。协议或 JSON 格式不兼容时停止提交并提示升级，本地内容和缓存保留。

BIN 被 Steam 或其他程序修改后，可以在“被修改”页选择“通过 SATLI-lite 管理”，复用经过哈希验证的最后一次译文快照并转换为 JSON，不必继续修改 BIN。找不到可信译文时会要求重新选择或下载，不会将被覆盖的本地原文当作译文发送。BIN 恢复、编辑、历史版本和投稿继续独立可用，也不会自动更改 lite 的译本。

本地译文也可独立发送，无需先写入 Steam BIN：

- 在“本地”页选择“发送文件到 lite”，选择标准命名的 BIN 或 ZIP，校验后选择语言并确认发送。
- 在编辑器中选择“发送到 SATLI-lite”，发送当前目标语言的编辑内容，包括尚未写回 BIN 的修改。发送不会清除草稿，也不会自动保存或反向同步 lite 的修改。

BIN 转换保留源文件标识与各语言文本，缺失文本保留为空；编辑器当前文本使用无需 BIN 来源标识的 JSON 格式 2。重复 API name、语言映射冲突、无效文本或超出协议限制会中止发送。

已有 `SATLI Achievement Display Bridge` 时，列表会提示对应游戏的旧锁定记录，但不会把它当作 lite 的导入状态。可先从设置中的独立入口完成切换：正常退出 Steam，确认将旧 `satli.star` 移入回收站，必要时确认 UAC，再下载、启用 lite 并重启 Steam。发送译文时也提供相同迁移入口。移除操作只处理旧插件安装包，保留旧快照、本地译文和 BIN 备份；无法回收时停止，不强制删除。旧快照只用于检测迁移记录，不作为 lite 的导入文件。SATLI 不再生成或刷新 `satli-bridge-v1.json`，两个客户端的本地编辑也不会自动同步。

在 **Steam → Millennium → Plugins** 的 SATLI lite 插件设置中管理译本、语言和显示开关。若设置页已发现插件文件但没有外部协议，请先启用并重启 Steam，再在 lite 中检查更新；不要把缺少协议当作未安装而反复覆盖插件。

## 编辑本地成就

在“本地”页点击“编辑”，即可按成就 API ID 修改目标语言的名称和说明。

- 可搜索成就，并选择只读语言作为对照。
- 可编辑现有语言或新增合法的 Steam 语言。
- 编辑草稿会自动保存；源文件和成就集合未变化时，再次进入可继续编辑。
- 允许保存不完整翻译，但会明确提示缺失项。
- 保存前会再次检查源文件、成就集合和文本内容。
- 可导出校验后的 BIN，或根目录仅含该 BIN 的标准投稿 ZIP。
- “导出翻译 JSON”可直接导出当前编辑结果或已恢复的草稿，无需先“保存到本机”。导出会重新核对源文件，并保留原有语言；JSON 导出本身不会写回 Steam、创建备份或记录新修订。

写回本机前必须完全退出 Steam。连续编辑可恢复最近几次修改。

## 制作、导入与贡献翻译

### 使用 Localizer Skill 制作

“本地”页的“制作翻译”会打开 [Steam Achievement Localizer Skill](https://github.com/GaBoron/steam-achievement-localizer-skill)。它负责研究多语言语境、制作翻译、无损写入和验证，并在任务的 `final/` 目录生成：

- `UserGameStatsSchema_<app_id>.bin`
- `UserGameStatsSchema_<app_id>.zip`
- `report.json`

### 导入本地翻译

点击“导入本地翻译”，选择规范命名的 BIN，或根目录仅含该 BIN 的标准 ZIP。应用会检查：

- 文件名与 App ID
- ZIP 单文件结构
- Binary KeyValues 字节级 roundtrip
- 成就内容与 SHA-256

管理器不会修改所选源文件。确认预览后，它会使用与社区译本相同的备份、安装与恢复流程。

### 独立导出翻译 JSON

以下入口使用同一套翻译库格式 2 校验：

- 编辑页的“导出翻译 JSON”：包括当前尚未写回 BIN 的编辑内容。
- “本地”页的“文件导出为 JSON”：从规范命名的 BIN/ZIP 转换，无需先安装文件，也不要求本机存在该游戏的 Steam 缓存。
- 已管理 BIN 译本的“更多 → 导出翻译 JSON”：使用经完整性校验的管理译文；本机 BIN 被修改时优先使用可信快照，找不到可信来源时停止。
- 修订历史的“导出 JSON”：使用所选修订内容，不激活该修订。

选择保存位置前可明确填写 `variant_id`。它表示译本版本，与目标语言无关；默认使用当前记录的版本，未记录时为 `default`。文件名必须为 `UserGameStatsSchema_<app_id>.json`。

导出保留全部可用语言，不自动补译或丢弃缺失语言。每项成就的名称与描述分别检查：该字段在所有声明语言中都有内容，或在所有语言中全部为空。若只有部分语言缺失，导出会停止并报告对应成就 API name、字段与缺失语言，请补齐后重试。未通过校验的内容不会覆盖已有导出文件。

JSON 使用 UTF-8、LF 和结尾换行；相同内容与版本 ID 可重复生成相同字节。独立格式 2 仅含显示文本，不含 `source_sha256`，不能取代完整 Steam BIN，也不包含解锁状态、进度或图标。BIN 和 ZIP 导出继续保留。

### 请求或贡献翻译

没有云端译本时，可在搜索页点击“未找到游戏？”。本地游戏的成就 schema 能正常读取、没有简体或繁体中文且翻译库也未收录时，游戏卡片还会直接显示“请求翻译”。

填写目标语言和可选备注后，应用会导出并校验原始 schema ZIP，只自动填写 GitHub 翻译请愿中已经确定的游戏名、App ID、目标语言和备注，并在资源管理器中选中 ZIP。应用不会自动上传文件或提交 Issue；请把选中的 ZIP 拖到网页表单的上传区域，检查内容后自行提交。

如果已经完成翻译，可从编辑页导出独立 JSON 或标准 ZIP，并在导出后选择打开新投稿或更新投稿表单。JSON 会再次核对格式、语言覆盖和附件内容，并按文件中的 `variant_id` 填写更新版本。请自行上传附件、检查表单并确认提交；应用不会自动投稿。ZIP 根目录只能包含一个 `UserGameStatsSchema_<app_id>.bin`。完整规范见翻译库的[翻译 JSON 格式](https://github.com/GaBoron/steam-achievement-translation-library/blob/main/docs/translation-json.md)。

导出不会修改 Steam 文件。找不到原始文件时，请先启动对应游戏，让 Steam 生成一次成就缓存。

## 报告云端文件问题

1. 在“云端”页找到游戏并点击“报告”。
2. 选择“文件可能过期”或“文件可能不生效”，填写说明和可选参考来源。
3. 应用会在浏览器中打开已经预填的 GitHub 网页表单。
4. 检查网页中的内容，登录 GitHub 后自行提交。

预填内容只包含游戏名、App ID、问题类型、说明和可选参考来源，不会上传本机成就文件。SATLI 不会登录 GitHub、保存 GitHub 凭据或代替用户提交 Issue。

## 开发与自动化命令

源码中的 `Satli.Cli.Host` 提供可选命令入口，不随桌面安装包分发。以下命令可从仓库目录运行；`<Steam目录>` 和 `<SATLI数据目录>` 应与桌面端设置一致。

```powershell
dotnet run --project src/Satli.Cli.Host -- lite-install --steam-dir '<Steam目录>' --yes --jsonl
dotnet run --project src/Satli.Cli.Host -- lite retire-legacy --steam-dir '<Steam目录>' --yes --jsonl
dotnet run --project src/Satli.Cli.Host -- lite inspect --steam-dir '<Steam目录>' --data-dir '<SATLI数据目录>' --jsonl
dotnet run --project src/Satli.Cli.Host -- lite import 570 --source '<译文JSON或标准BIN/ZIP>' --language schinese --steam-dir '<Steam目录>' --data-dir '<SATLI数据目录>' --yes --jsonl
dotnet run --project src/Satli.Cli.Host -- lite status 570 --steam-dir '<Steam目录>' --data-dir '<SATLI数据目录>' --jsonl
dotnet run --project src/Satli.Cli.Host -- lite resume 570 --steam-dir '<Steam目录>' --data-dir '<SATLI数据目录>' --jsonl
```

`lite-install` 的 `already_installed: true` 表示原样保留已有安装，没有重新下载或校验现有包；首次安装仍需联网。`lite retire-legacy` 要求已退出 Steam，只回收旧插件安装包，不清理任何译文或快照。

导入来源三选一：`--source FILE`、`--catalog --variant ID` 或 `--managed`（哈希验证的已管理 BIN 快照）。导入前可用 `--dry-run --preview-content` 校验并预览。默认拒绝覆盖；只有查询后确认覆盖时，才提供 `--replace-revision REVISION --yes`，含本地编辑还需 `--allow-local-edits`。状态变化导致 `STALE_REVISION` 时必须重新查询和确认。`status` 在已有待处理请求时只刷新原请求，避免重复导入；`resume` 不会新建查询。退出码 0 也可能只是排队成功，必须检查返回的 `state`、`pending`、`succeeded` 和 `app.effective_enabled`；这些都不代表 Steam 页面渲染验收。

独立 JSON 导出也可从源码调用：

```powershell
dotnet run --project src/Satli.Cli.Host -- schema export 570 --source '<BIN或ZIP路径>' --format json --variant-id default --output '<输出目录>\UserGameStatsSchema_570.json' --jsonl

dotnet run --project src/Satli.Cli.Host -- schema export 570 --edits-file '<编辑数据JSON>' --target-language schinese --steam-dir '<Steam目录>' --format json --variant-id default --output '<输出目录>\UserGameStatsSchema_570.json' --jsonl

dotnet run --project src/Satli.Cli.Host -- schema revisions export 570 '<修订ID>' --data-dir '<SATLI数据目录>' --format json --variant-id default --output '<输出目录>\UserGameStatsSchema_570.json' --jsonl
```

不指定 `--source` 时读取本机 BIN；加 `--managed` 则读取可信管理译文。`--source` 与 `--managed` 互斥。编辑数据仍使用编辑器现有的 version 1 结构，包含 `app_id`、真实 `source_sha256`、`target_language` 和按 `api_name` 对应的 `rows`；导出前检查源文件一致性。`--allow-incomplete` 不会绕过 JSON 的正式译文覆盖校验。JSON 导出不要求 `--yes`，不会创建修订；原有 BIN/ZIP 导出行为不变。

## 软件更新与网络

### 软件更新

- **Microsoft Store 版：** Microsoft Store 仍会按系统设置自动提供更新；也可在“设置 → 软件更新”开启启动检查或手动检查。发现新版时，应用会显示对应版本的发布说明，并可直接打开 Store 产品页完成更新。
- **独立安装版：** 在“设置 → 软件更新”中检查 GitHub Releases 稳定版、查看发布说明并下载安装包。软件不会静默下载，只有点击“下载并安装”后才会开始。

两个渠道使用同一公开版本号，但不会相互安装对方的更新包。云端译本列表和译本内容的更新独立于软件版本，两个渠道使用相同来源。

### 翻译数据与网络

网络设置支持：

- 系统代理、HTTP 代理和 SOCKS 代理
- 自定义 DNS 与下载镜像
- 连接测试
- 离线模式和最近成功加载的云端列表

“设置 → 下载源”可分别拖动调整翻译目录与翻译 BIN/JSON 文件的来源顺序。每个目录源会先请求 `index-v2.json`，不可用或不兼容时再回退同源 `index.json`，然后继续下一个来源。默认目录顺序为 GitHub Raw、jsDelivr、jsDelivr Fastly、StaticDelivr；默认文件顺序为 jsDelivr、jsDelivr Fastly、GitHub Raw。StaticDelivr 只提供目录候选，因为实际验证中其二进制文件响应不适合作为安装器下载源。两个列表可分别恢复默认顺序。

本地游戏扫描不以在线翻译目录为前提。无网络且没有目录缓存时，本地页仍会列出扫描到的游戏，并将收录状态显示为“未知”；只有已确认目录未收录的游戏才会显示请求翻译入口。V2 译本下载固定限制为 32 MiB，并在预览、安装和更新判断前使用 SHA-256 校验内容。

独立安装版无法访问 GitHub 时，软件更新检查失败不会影响扫描、安装、编辑或恢复。Microsoft Store 版的软件更新状态可在 Store 中查看。

## 常见问题

### 安全软件报警怎么办

部分安全软件可能根据程序启动后的行为，把独立安装版的 `SATLI.exe` 误判为木马或其他恶意程序并阻止启动。通用检测名称通常只表示某些行为触发了规则，不代表已经确认存在某一种具体病毒；但只看到通用名称时也不能直接断定文件安全。

请按下面的顺序处理：

1. **优先改用 Microsoft Store 版。** 这是解决独立安装版被拦截的首要方案，且已经确认不会触发目前已知的安全软件行为误报。退出并卸载独立安装版后，从 [Microsoft Store](https://apps.microsoft.com/detail/9PB7V9S03K80) 安装即可；本机设置、缓存、草稿、历史和备份不会被主动删除。
2. **无法使用 Store 时，先确认下载来源。** 只使用本仓库的 [GitHub Releases](https://github.com/GaBoron/SATLI/releases/latest)。从网盘、群文件或第三方下载站取得的副本不要恢复运行。
3. **确认被拦截的是当前主程序。** 新版的正常路径通常是 `C:\Program Files\SATLI\SATLI.exe`，不会再运行旧版的 `cli\satli.exe`。如果告警仍指向 `cli\satli.exe`，请安装最新版本；安装器会清理旧 CLI 目录。
4. **核对文件指纹。** 在 PowerShell 中运行下面的命令，并把结果与对应 GitHub Release 发布说明中的 SHA-256 比较。两者必须完全一致；不一致时请删除该安装包并从官方渠道重新下载。

   ```powershell
   Get-FileHash '.\SATLI-Setup-vX.Y.Z.exe' -Algorithm SHA256
   ```

5. **更新安全软件后提交复核。** 更新安全软件及其检测数据库后重新扫描。仍然报警时，通过该安全软件厂商官网的“误报申诉”“文件分析”或“重新检测”入口提交安装包或 `SATLI.exe`，并等待厂商复核。

在安全软件厂商给出复核结果前，不建议关闭行为防护，也不要把整个 `C:\Program Files\SATLI` 加入排除项。反馈问题时，请附上 SATLI 版本、告警时间、对象完整路径、检测名称和 SHA-256；这些信息足够定位文件，不需要发送 Steam 凭据、代理密码或成就文件内容。

### 找不到本地游戏

先启动一次对应游戏，让 Steam 生成成就缓存，然后返回应用重新扫描。仍未找到时，在设置中确认 Steam 路径。Steam Web API 可以补全账号游戏库，没有本机成就文件时暂时不能编辑本地 BIN，但仍可从云端列表预览和应用 JSON。

### 游戏名称没有正常显示

名称查询需要网络。检查代理或镜像设置后重新获取；离线时应用会显示已有缓存或 App ID，不影响本地文件。

### 云端列表加载失败

使用设置页的网络测试检查 GitHub、代理或镜像连接。应用会尽量显示最近一次成功加载的列表，恢复网络后可手动刷新。

### GitHub 表单没有打开

确认系统已有默认浏览器且网络可以访问 GitHub，然后重新点击报告、请愿或投稿入口。SATLI 只负责打开预填表单；GitHub 登录、附件上传和最终提交都在浏览器中完成。

### 编辑草稿没有恢复

草稿只适用于同一个源文件和同一组成就。游戏更新、Steam 重建文件或成就列表变化后，旧草稿不会自动套用。

### 无法保存本地编辑

保存前必须完全退出 Steam。若源文件在编辑期间发生变化，应用会拒绝覆盖，请重新进入编辑页并确认最新内容。

### 安装后翻译消失

Steam 或游戏更新可能重新生成成就缓存。可以选择通过 SATLI-lite 的独立 JSON 链路管理显示，或重新检查 BIN 译本；如果云端译本已经过期或不再生效，请使用“报告”提交问题。

### 恢复被拒绝

目标文件在操作后发生了变化。普通恢复会停止，以避免覆盖其他程序或 Steam 的新数据。确认需要覆盖时可使用强制恢复，当前文件会先被归档。
