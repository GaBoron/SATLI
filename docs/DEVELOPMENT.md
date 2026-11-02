# 开发与命令行

[返回项目首页](../README.md) · [使用指南](USAGE.md)

`src/Satli.Cli.Host` 是面向源码开发与自动化的命令入口，不随桌面安装包分发。普通使用者请使用图形界面。

在仓库根目录执行以下示例，按需替换 Steam 目录、SATLI 数据目录和输入文件：

```powershell
dotnet run --project src/Satli.Cli.Host -- lite-install --steam-dir '<Steam目录>' --yes --jsonl
dotnet run --project src/Satli.Cli.Host -- lite inspect --steam-dir '<Steam目录>' --data-dir '<SATLI数据目录>' --jsonl
dotnet run --project src/Satli.Cli.Host -- lite import 570 --source '<译文JSON或BIN/ZIP>' --language schinese --steam-dir '<Steam目录>' --data-dir '<SATLI数据目录>' --yes --jsonl
dotnet run --project src/Satli.Cli.Host -- lite status 570 --steam-dir '<Steam目录>' --data-dir '<SATLI数据目录>' --jsonl
dotnet run --project src/Satli.Cli.Host -- lite resume 570 --steam-dir '<Steam目录>' --data-dir '<SATLI数据目录>' --jsonl
```

导入源可以选 `--source FILE`、`--catalog --variant ID` 或 `--managed`。建议先使用 `--dry-run --preview-content` 检查译文。覆盖需要先查询版本，再显式提供 `--replace-revision REVISION --yes`；覆盖 lite 的本地编辑还需要 `--allow-local-edits`。遇到 `STALE_REVISION` 必须重新查询。

`status` 和 `resume` 会跟踪已有请求，不代表 Steam 中已经显示译文。退出码为 0 也可能表示成功排队，应检查 JSONL 中的状态及处理结果。旧桥迁移命令为 `lite retire-legacy`，要求 Steam 已退出，只回收旧插件安装包。

## 导出翻译 JSON

```powershell
dotnet run --project src/Satli.Cli.Host -- schema export 570 --source '<BIN或ZIP路径>' --format json --variant-id default --output '<输出目录>\UserGameStatsSchema_570.json' --jsonl

dotnet run --project src/Satli.Cli.Host -- schema revisions export 570 '<修订ID>' --data-dir '<SATLI数据目录>' --format json --variant-id default --output '<输出目录>\UserGameStatsSchema_570.json' --jsonl
```

也可使用 `--edits-file` 与 `--target-language` 导出未写回 BIN 的编辑内容；不指定 `--source` 时使用本机 BIN，`--managed` 则使用已验证的管理译文。`--source` 与 `--managed` 互斥。导出会校验文件和语言覆盖，不会创建 BIN 修订或自动投稿。

译文文件规范、投稿条件和版本格式以 [翻译库贡献指南](https://github.com/GaBoron/steam-achievement-translation-library/blob/main/docs/CONTRIBUTING.md) 与 [JSON 格式说明](https://github.com/GaBoron/steam-achievement-translation-library/blob/main/docs/translation-json.md) 为准。
