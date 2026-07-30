from __future__ import annotations

import argparse
import os
from pathlib import Path

from satl import __version__
from satl.cache_command import command_cache_refresh
from satl.install_command import command_install
from satl.local_import_command import command_local_import
from satl.petition_command import command_petition_export
from satl.restore_command import command_restore
from satl.schema_command import (
    command_schema_apply,
    command_schema_export,
    command_schema_inspect,
    command_schema_revisions_activate,
    command_schema_revisions_export,
    command_schema_revisions_list,
    command_schema_revisions_show,
    command_schema_revisions_verify,
    command_schema_restore,
)
from satl.scan_command import command_scan
from satl.status_command import command_status


def default_data_dir() -> Path:
    base = os.environ.get("LOCALAPPDATA")
    if base:
        return Path(base) / "SteamAchievementTranslationInstaller"
    return Path.home() / "AppData" / "Local" / "SteamAchievementTranslationInstaller"


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="satl",
        description="安全安装和恢复 Steam 成就翻译库中的本地化文件。",
    )
    parser.add_argument("--version", action="version", version=f"satl {__version__}")
    subparsers = parser.add_subparsers(dest="command", required=True)

    scan = subparsers.add_parser("scan", help="扫描本机游戏并匹配可用翻译")
    _add_data_dir(scan)
    _add_steam_dir(scan)
    _add_offline(scan)
    scan.add_argument(
        "--catalog-cache-only",
        action="store_true",
        help="仅从本地缓存读取 index，同时保留其他已配置的联网数据源",
    )
    scan.add_argument("--account", help="仅使用指定的本地 SteamID64 账号缓存")
    scan.add_argument(
        "--owned-account",
        help="Steam Web API 游戏库补全使用的本地 SteamID64",
    )
    scan.add_argument(
        "--include-owned-games",
        action="store_true",
        help="使用环境变量 SATL_STEAM_WEB_API_KEY 补全指定账号拥有的游戏",
    )
    scan.add_argument(
        "--scope",
        choices=("manageable", "local", "cloud"),
        default="manageable",
        help="列出可管理、本地或云端游戏（默认：manageable）",
    )
    scan.add_argument("--json", action="store_true", help="输出稳定的 JSON 记录")
    scan.add_argument("--jsonl", action="store_true", help="输出供桌面应用使用的 JSON Lines 事件")
    scan.set_defaults(handler=command_scan)

    install = subparsers.add_parser("install", help="安装一个或多个翻译")
    _add_data_dir(install)
    _add_steam_dir(install)
    _add_offline(install)
    install.add_argument("app_ids", nargs="*", metavar="APP_ID")
    install.add_argument("--matched", action="store_true", help="安装扫描到的所有可用翻译")
    install.add_argument("--account", help="与 --matched 一起使用的 SteamID64")
    install.add_argument(
        "--variant",
        action="append",
        default=[],
        metavar="APP_ID=VARIANT",
        help="选择非默认版本，可重复指定",
    )
    install.add_argument("--allow-outdated", action="store_true", help="允许安装非 current 条目")
    install.add_argument("--yes", action="store_true", help="跳过交互确认")
    install.add_argument("--dry-run", action="store_true", help="仅显示计划，不下载或写入")
    install.add_argument(
        "--preview-content",
        action="store_true",
        help="在 JSONL dry-run 中读取并输出待安装 schema 的成就内容",
    )
    install.add_argument("--jsonl", action="store_true", help="输出供桌面应用使用的 JSON Lines 事件")
    install.set_defaults(handler=command_install)

    local_import = subparsers.add_parser(
        "local-import", help="导入 Localizer Skill 生成的本地 BIN 或 ZIP"
    )
    _add_data_dir(local_import)
    _add_steam_dir(local_import)
    local_import.add_argument("source", type=Path, metavar="BIN_OR_ZIP")
    local_import.add_argument("--yes", action="store_true", help="跳过交互确认")
    local_import.add_argument("--dry-run", action="store_true", help="仅校验和显示计划，不写入")
    local_import.add_argument(
        "--preview-content",
        action="store_true",
        help="在 JSONL dry-run 中输出本地 schema 的成就内容",
    )
    local_import.add_argument(
        "--expected-sha256",
        help="要求导入内容与预览得到的 SHA-256 一致",
    )
    local_import.add_argument("--jsonl", action="store_true", help="输出供桌面应用使用的 JSON Lines 事件")
    local_import.set_defaults(handler=command_local_import)

    status = subparsers.add_parser("status", help="检查 SATL 管理的安装状态")
    _add_data_dir(status)
    _add_offline(status)
    status.add_argument("app_ids", nargs="*", metavar="APP_ID")
    status.add_argument("--json", action="store_true", help="输出稳定的 JSON 记录")
    status.add_argument("--jsonl", action="store_true", help="输出供桌面应用使用的 JSON Lines 事件")
    status.set_defaults(handler=command_status)

    restore = subparsers.add_parser("restore", help="恢复安装前的 schema")
    _add_data_dir(restore)
    _add_steam_dir(restore)
    restore.add_argument("app_ids", nargs="*", metavar="APP_ID")
    restore.add_argument("--all", action="store_true", help="恢复所有尚未恢复的安装")
    restore.add_argument("--force", action="store_true", help="归档已变化的目标后强制恢复")
    restore.add_argument("--yes", action="store_true", help="跳过交互确认")
    restore.add_argument("--dry-run", action="store_true", help="仅显示计划，不写入")
    restore.add_argument(
        "--preview-content",
        action="store_true",
        help="在 JSONL dry-run 中读取并输出待恢复 schema 的成就内容",
    )
    restore.add_argument("--jsonl", action="store_true", help="输出供桌面应用使用的 JSON Lines 事件")
    restore.set_defaults(handler=command_restore)

    cache = subparsers.add_parser("cache", help="管理本地 catalog/schema 缓存")
    cache_subparsers = cache.add_subparsers(dest="cache_command", required=True)
    refresh = cache_subparsers.add_parser("refresh", help="刷新 index.json 缓存")
    _add_data_dir(refresh)
    refresh.add_argument("--jsonl", action="store_true", help="输出供桌面应用使用的 JSON Lines 事件")
    refresh.set_defaults(handler=command_cache_refresh)

    petition = subparsers.add_parser("petition", help="导出并提交翻译请愿")
    petition_subparsers = petition.add_subparsers(dest="petition_command", required=True)
    petition_export = petition_subparsers.add_parser(
        "export", help="按翻译请愿模板导出原始 schema ZIP"
    )
    _add_steam_dir(petition_export)
    petition_export.add_argument("app_id", metavar="APP_ID")
    petition_export.add_argument("--output", type=Path, required=True, help="ZIP 保存路径")
    petition_export.add_argument("--overwrite", action="store_true", help="覆盖已确认的目标文件")
    petition_export.add_argument(
        "--jsonl", action="store_true", help="输出供桌面应用使用的 JSON Lines 事件"
    )
    petition_export.set_defaults(handler=command_petition_export)

    schema = subparsers.add_parser("schema", help="检查、编辑、导出和恢复本地成就 schema")
    schema_subparsers = schema.add_subparsers(dest="schema_command", required=True)

    schema_inspect = schema_subparsers.add_parser("inspect", help="读取本地 schema 成就内容")
    _add_data_dir(schema_inspect)
    _add_steam_dir(schema_inspect)
    schema_inspect.add_argument("app_id", metavar="APP_ID")
    schema_inspect.add_argument("--jsonl", action="store_true")
    schema_inspect.set_defaults(handler=command_schema_inspect)

    schema_export = schema_subparsers.add_parser("export", help="导出编辑后的 BIN 或投稿 ZIP")
    _add_data_dir(schema_export)
    _add_steam_dir(schema_export)
    _add_schema_edit_arguments(schema_export)
    schema_export.add_argument("--format", choices=("bin", "zip"), required=True)
    schema_export.add_argument("--output", type=Path, required=True)
    schema_export.add_argument("--game-name")
    schema_export.add_argument("--variant-id")
    schema_export.set_defaults(handler=command_schema_export)

    schema_apply = schema_subparsers.add_parser("apply", help="安全写回编辑后的本地 schema")
    _add_data_dir(schema_apply)
    _add_steam_dir(schema_apply)
    _add_schema_edit_arguments(schema_apply)
    schema_apply.add_argument("--game-name", help="记录本地编辑对应的游戏名称")
    schema_apply.add_argument("--variant-id")
    schema_apply.add_argument("--yes", action="store_true")
    schema_apply.set_defaults(handler=command_schema_apply)

    schema_restore = schema_subparsers.add_parser("restore", help="恢复上一次本地 schema 编辑")
    _add_data_dir(schema_restore)
    _add_steam_dir(schema_restore)
    schema_restore.add_argument("app_id", metavar="APP_ID")
    schema_restore.add_argument("--force", action="store_true")
    schema_restore.add_argument("--yes", action="store_true")
    schema_restore.add_argument("--jsonl", action="store_true")
    schema_restore.set_defaults(handler=command_schema_restore)

    revisions = schema_subparsers.add_parser("revisions", help="管理本地 Git schema 修订历史")
    revision_subparsers = revisions.add_subparsers(dest="revision_command", required=True)

    revisions_list = revision_subparsers.add_parser("list", help="列出游戏修订")
    _add_data_dir(revisions_list)
    _add_steam_dir(revisions_list)
    revisions_list.add_argument("app_id", metavar="APP_ID")
    revisions_list.add_argument("--jsonl", action="store_true")
    revisions_list.set_defaults(handler=command_schema_revisions_list)

    revisions_show = revision_subparsers.add_parser("show", help="查看一个修订")
    _add_data_dir(revisions_show)
    _add_steam_dir(revisions_show)
    revisions_show.add_argument("app_id", metavar="APP_ID")
    revisions_show.add_argument("revision", metavar="COMMIT")
    revisions_show.add_argument("--jsonl", action="store_true")
    revisions_show.set_defaults(handler=command_schema_revisions_show)

    revisions_export = revision_subparsers.add_parser("export", help="导出一个修订")
    _add_data_dir(revisions_export)
    revisions_export.add_argument("app_id", metavar="APP_ID")
    revisions_export.add_argument("revision", metavar="COMMIT")
    revisions_export.add_argument("--format", choices=("bin", "zip"), required=True)
    revisions_export.add_argument("--output", type=Path, required=True)
    revisions_export.add_argument("--jsonl", action="store_true")
    revisions_export.set_defaults(handler=command_schema_revisions_export)

    revisions_activate = revision_subparsers.add_parser("activate", help="把一个修订设为当前版本")
    _add_data_dir(revisions_activate)
    _add_steam_dir(revisions_activate)
    revisions_activate.add_argument("app_id", metavar="APP_ID")
    revisions_activate.add_argument("revision", metavar="COMMIT")
    revisions_activate.add_argument("--force", action="store_true")
    revisions_activate.add_argument("--yes", action="store_true")
    revisions_activate.add_argument("--jsonl", action="store_true")
    revisions_activate.set_defaults(handler=command_schema_revisions_activate)

    revisions_verify = revision_subparsers.add_parser("verify", help="验证 Git 修订仓库")
    _add_data_dir(revisions_verify)
    revisions_verify.add_argument("app_id", metavar="APP_ID", nargs="?")
    revisions_verify.add_argument("--jsonl", action="store_true")
    revisions_verify.set_defaults(handler=command_schema_revisions_verify)
    return parser


def _add_data_dir(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--data-dir", type=Path, default=default_data_dir(), help="覆盖 SATL 数据目录")


def _add_steam_dir(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--steam-dir", type=Path, help="覆盖自动检测的 Steam 目录")


def _add_offline(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--offline", action="store_true", help="仅使用已验证的本地缓存")


def _add_schema_edit_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("app_id", metavar="APP_ID")
    parser.add_argument("--target-language", required=True)
    parser.add_argument("--edits-file", type=Path, required=True)
    parser.add_argument("--allow-incomplete", action="store_true")
    parser.add_argument("--jsonl", action="store_true")
