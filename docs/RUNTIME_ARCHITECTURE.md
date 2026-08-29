# C# 运行时架构

Issue #12 的最终实现已完全移除 Python 依赖。SATLI 现在由两个自包含的
.NET 组件组成：

- `Satli.Core`：Catalog v2/v1、Steam 发现、Binary KeyValues、SHA-256、
  下载源、事务备份/恢复、本地导入、编辑与修订记录；
- `Satli.Cli`：稳定的公开命令行与 GUI JSONL 协议入口。

WinUI 正常操作启动同目录 `cli` 子目录下的 `satli.exe`。CLI 使用标准的
自包含目录部署，托管程序集与原生运行时保持为普通文件，进程启动时不再从压缩的
单文件包向内存解压程序集。需要管理员权限的写操作继续通过原有命名管道启动提升
权限的 SATLI 进程，再执行同一套 C# 核心。发布包不再包含 `_runtime`、
`python.exe`、`satli.pyz`、Dulwich 或 urllib3。

CLI 扫描会并行准备在线/缓存翻译目录与本机 Steam 游戏发现，并保持输出顺序稳定。
首屏扫描完成后，GUI 以最多两个并发任务预加载本地和云端只读清单；预加载复用已验证的
目录缓存，页面打开时消费快照，失败时再前台重试。安装、恢复、文件保护、状态与历史写入、
备份和回滚仍严格串行。

## 数据兼容与安全边界

- 安装状态继续使用 version 1 `state.json`；
- 本地编辑继续读取 version 1 `edit-history.json`；
- Catalog 优先读取 v2，并在下载源尚未同步时回退 v1；
- schema 下载保持 32 MiB 上限、可选文件大小校验和强制 SHA-256；
- 写入继续采用暂存、备份、校验、替换和失败回滚；
- 所有清理操作仍移入 Windows 回收站。

新的修订记录存储在 `schema-revisions`，使用 schema SHA-256 和内容寻址的
commit ID；读取、导出和激活不再需要 Dulwich 或系统 Git。

## 基线与验证

迁移前基线（v2.0.0）中的 Embedded Python 运行时为 23.18 MiB，热启动约
417 ms。迁移后的基准脚本 `scripts/benchmark-runtime.ps1` 直接测量 C# CLI
的启动、Catalog 刷新、离线云端扫描与修订仓库校验，并同时报告完整运行时目录
的文件数与大小。脚本只使用临时数据目录，并在结束时将其移入回收站。

2026-08-29 初次 C# 迁移的压缩单文件基线为 43,983,452 bytes，首次启动
562.66 ms，后续中位 161.89 ms。改为可检查的目录部署后，未裁剪 CLI 为
194 个发布文件、105,980,262 bytes；启用完整裁剪与 ReadyToRun，并用源生成
JSON 元数据消除裁剪风险后，降为 60 个发布文件、31,972,198 bytes，其中
`satli.exe` 为 162,304 bytes。最终 15 次启动样本中首次为 72.74 ms，热启动
中位 66.09 ms；409 项离线云端扫描 7 次中位 110.99 ms，未裁剪版本为
122.93 ms。完整 WinUI 发布目录也从 171.18 MiB 降至 100.49 MiB。网络刷新
结果受链路状态影响，本表只用于确认同机优化量级，不作为跨设备性能承诺。

```powershell
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\benchmark-runtime.ps1 `
  -CliPath .\src\Satli.Cli\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\publish\satli.exe
```
