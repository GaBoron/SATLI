from __future__ import annotations

import argparse
import re
import tempfile
from pathlib import Path

from satl.cli_protocol import emit_jsonl
from satl.cli_validation import confirm
from satl.errors import IntegrityError, PreflightError, UsageError
from satl.local_import import LocalImportArtifact, read_local_import
from satl.models import SchemaVariant
from satl.steam import discover_installed_games, find_steam_dir, is_steam_running, schema_target
from satl.transaction import TransactionManager


def command_local_import(args: argparse.Namespace) -> int:
    if args.preview_content and (not args.dry_run or not args.jsonl):
        raise UsageError("--preview-content 必须与 --dry-run --jsonl 一起使用")

    artifact = read_local_import(Path(args.source))
    if args.expected_sha256 and re.fullmatch(r"[0-9a-fA-F]{64}", args.expected_sha256) is None:
        raise UsageError("--expected-sha256 必须是 64 位十六进制 SHA-256")
    if args.expected_sha256 and artifact.sha256 != args.expected_sha256.casefold():
        raise IntegrityError(
            "本地导入文件自预览后已变化："
            f"期望 {args.expected_sha256.casefold()}，实际 {artifact.sha256}"
        )
    steam_dir = find_steam_dir(args.steam_dir)
    game_name = discover_installed_games(steam_dir).get(artifact.app_id) or f"App ID {artifact.app_id}"
    variant = _local_variant(artifact)

    if args.jsonl:
        emit_jsonl(
            "local-import",
            "plan",
            {
                "count": 1,
                "items": [
                    {
                        "app_id": artifact.app_id,
                        "game_name": game_name,
                        "variant_id": variant.variant_id,
                        "source": str(artifact.source),
                        "schema_sha256": artifact.sha256,
                    }
                ],
            },
        )
    else:
        print(f"本地导入：{game_name}（{artifact.app_id}） <- {artifact.source}")

    if args.dry_run:
        if args.preview_content:
            emit_jsonl(
                "local-import",
                "item-preview",
                {
                    "app_id": artifact.app_id,
                    "game_name": game_name,
                    "variant_id": variant.variant_id,
                    "action": "replace",
                    "source": str(artifact.source),
                    "schema_sha256": artifact.sha256,
                    **artifact.preview,
                },
            )
        if args.jsonl:
            emit_jsonl(
                "local-import",
                "completed",
                {"succeeded": 0, "failed": 0, "dry_run": True, "exit_code": 0},
            )
        else:
            print("dry-run：未创建备份、未写入 Steam 文件。")
        return 0

    confirm(f"确认导入 App ID {artifact.app_id} 的本地翻译？", args.yes)
    if is_steam_running():
        raise PreflightError("Steam 正在运行。请从系统托盘正常退出 Steam 后重试。")

    if args.jsonl:
        emit_jsonl(
            "local-import",
            "item-started",
            {
                "app_id": artifact.app_id,
                "game_name": game_name,
                "variant_id": variant.variant_id,
            },
        )
    with tempfile.TemporaryDirectory(prefix="satl-local-import-") as temporary:
        staged = Path(temporary) / artifact.schema_name
        try:
            staged.write_bytes(artifact.payload)
        except OSError as exc:
            raise PreflightError(f"无法暂存本地导入 schema：{exc}") from exc
        TransactionManager(Path(args.data_dir)).install(
            artifact.app_id,
            schema_target(steam_dir, artifact.app_id),
            staged,
            variant,
        )
    if args.jsonl:
        emit_jsonl(
            "local-import",
            "item-succeeded",
            {
                "app_id": artifact.app_id,
                "game_name": game_name,
                "variant_id": variant.variant_id,
                "schema_sha256": artifact.sha256,
            },
        )
        emit_jsonl(
            "local-import",
            "completed",
            {"succeeded": 1, "failed": 0, "exit_code": 0},
        )
    else:
        print(f"已导入：{artifact.app_id} / {variant.variant_id}")
    return 0


def _local_variant(artifact: LocalImportArtifact) -> SchemaVariant:
    return SchemaVariant(
        variant_id=f"local-{artifact.sha256[:12]}",
        primary=True,
        schema_file=artifact.schema_name,
        sha256=artifact.sha256,
        file_size_bytes=len(artifact.payload),
        note_zh="本地导入",
        achievement_count=int(artifact.preview["achievement_count"]),
    )
