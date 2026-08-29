# C# 运行时架构

Issue #12 的最终实现已完全移除 Python 依赖。SATLI 现在由两个自包含的
.NET 组件组成：

- `Satli.Core`：Catalog v2/v1、Steam 发现、Binary KeyValues、SHA-256、
  下载源、事务备份/恢复、本地导入、编辑与修订记录；
- `Satli.Cli`：稳定的公开命令行与 GUI JSONL 协议入口。

WinUI 正常操作启动同目录 `cli` 子目录下的单文件 `satli.exe`；需要管理员权限的写操作
继续通过原有命名管道启动提升权限的 SATLI 进程，再执行同一套 C# 核心。
发布包不再包含 `_runtime`、`python.exe`、`satli.pyz`、Dulwich 或 urllib3。

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
417 ms。迁移后的基准脚本 `scripts/benchmark-runtime.ps1` 直接测量单文件
C# CLI 的启动、Catalog 刷新、离线云端扫描与修订仓库校验。脚本只使用临时
数据目录，并在结束时将其移入回收站。

2026-08-29 同机迁移后实测：单文件 CLI 43,983,452 bytes，首次启动
562.66 ms，后续中位 161.89 ms；Catalog v2 刷新 1,787.94 ms，409 项离线
云端扫描 203.58 ms，空修订仓库校验 149.44 ms。网络刷新结果受链路状态影响，
本表用于确认迁移后的量级，不作为跨设备性能承诺。

```powershell
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\benchmark-runtime.ps1 `
  -CliPath .\src\Satli.Cli\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\publish\satli.exe
```
