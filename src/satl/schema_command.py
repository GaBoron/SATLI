from __future__ import annotations

import argparse
from pathlib import Path

from satl.cli_protocol import emit_jsonl
from satl.cli_validation import confirm
from satl.errors import PreflightError
from satl.schema_edit import (
    apply_schema,
    export_schema,
    inspect_schema,
    restore_schema,
)
from satl.steam import find_steam_dir, is_steam_running, schema_target


def command_schema_inspect(args: argparse.Namespace) -> int:
    steam_dir = find_steam_dir(args.steam_dir)
    report = inspect_schema(
        schema_target(steam_dir, args.app_id),
        args.app_id,
        Path(args.data_dir),
    )
    if args.jsonl:
        emit_jsonl("schema-inspect", "item-succeeded", report)
        emit_jsonl("schema-inspect", "completed", {"count": 1, "exit_code": 0})
    else:
        print(
            f"{args.app_id}：{report['achievement_count']} 个成就，"
            f"语言 {', '.join(report['languages']) or '无'}，SHA-256 {report['source_sha256']}"
        )
    return 0


def command_schema_export(args: argparse.Namespace) -> int:
    steam_dir = find_steam_dir(args.steam_dir)
    report = export_schema(
        schema_target(steam_dir, args.app_id),
        args.app_id,
        args.target_language,
        Path(args.edits_file),
        Path(args.output),
        args.format,
        allow_incomplete=args.allow_incomplete,
    )
    if args.jsonl:
        emit_jsonl("schema-export", "item-succeeded", report)
        emit_jsonl("schema-export", "completed", {"count": 1, "exit_code": 0})
    else:
        print(f"已导出：{report['output']}")
    return 0


def command_schema_apply(args: argparse.Namespace) -> int:
    confirm(f"确认写回 App ID {args.app_id} 的本地成就文件？", args.yes)
    if is_steam_running():
        raise PreflightError("Steam 正在运行。请从系统托盘正常退出 Steam 后重试。")
    steam_dir = find_steam_dir(args.steam_dir)
    report = apply_schema(
        schema_target(steam_dir, args.app_id),
        args.app_id,
        args.target_language,
        Path(args.edits_file),
        Path(args.data_dir),
        allow_incomplete=args.allow_incomplete,
        game_name=getattr(args, "game_name", None),
    )
    if args.jsonl:
        emit_jsonl("schema-apply", "item-succeeded", report)
        emit_jsonl("schema-apply", "completed", {"count": 1, "exit_code": 0})
    else:
        print(f"已写回：{report['target']}；备份：{report['backup']}")
    return 0


def command_schema_restore(args: argparse.Namespace) -> int:
    confirm(f"确认恢复 App ID {args.app_id} 的上一次本地编辑？", args.yes)
    if is_steam_running():
        raise PreflightError("Steam 正在运行。请从系统托盘正常退出 Steam 后重试。")
    steam_dir = find_steam_dir(args.steam_dir)
    report = restore_schema(
        schema_target(steam_dir, args.app_id),
        args.app_id,
        Path(args.data_dir),
        force=args.force,
    )
    if args.jsonl:
        emit_jsonl("schema-restore", "item-succeeded", report)
        emit_jsonl("schema-restore", "completed", {"count": 1, "exit_code": 0})
    else:
        print(f"已恢复：{report['target']}")
    return 0
