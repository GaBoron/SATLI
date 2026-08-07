from __future__ import annotations

import argparse
import hashlib
import zipfile
from pathlib import Path

from satli.bkv import achievement_preview
from satli.cli_protocol import emit_jsonl
from satli.cli_validation import confirm
from satli.errors import PreflightError
from satli.schema_edit import (
    EditHistoryStore,
    apply_schema,
    apply_schema_payload,
    export_schema,
    inspect_schema,
    render_schema,
    restore_schema,
)
from satli.schema_revisions import SchemaRevisionRepository
from satli.steam import find_steam_dir, is_steam_running, schema_target


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
    repository = SchemaRevisionRepository(Path(args.data_dir))
    _migrate_legacy_edit_history(repository, args.app_id)
    report = export_schema(
        schema_target(steam_dir, args.app_id),
        args.app_id,
        args.target_language,
        Path(args.edits_file),
        Path(args.output),
        args.format,
        allow_incomplete=args.allow_incomplete,
    )
    payload = _exported_payload(Path(args.output), args.format, args.app_id)
    _capture_revision(
        report,
        Path(args.data_dir),
        args.app_id,
        payload,
        action="export",
        game_name=getattr(args, "game_name", None),
        target_language=args.target_language,
        variant_id=getattr(args, "variant_id", None),
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
    _migrate_legacy_edit_history(
        SchemaRevisionRepository(Path(args.data_dir)), args.app_id
    )
    report = apply_schema(
        schema_target(steam_dir, args.app_id),
        args.app_id,
        args.target_language,
        Path(args.edits_file),
        Path(args.data_dir),
        allow_incomplete=args.allow_incomplete,
        game_name=getattr(args, "game_name", None),
    )
    _capture_revision(
        report,
        Path(args.data_dir),
        args.app_id,
        Path(report["target"]).read_bytes(),
        action="apply",
        game_name=getattr(args, "game_name", None),
        target_language=args.target_language,
        variant_id=getattr(args, "variant_id", None),
    )
    if args.jsonl:
        emit_jsonl("schema-apply", "item-succeeded", report)
        emit_jsonl("schema-apply", "completed", {"count": 1, "exit_code": 0})
    else:
        print(f"已写回：{report['target']}；备份：{report['backup']}")
    return 0


def command_schema_draft(args: argparse.Namespace) -> int:
    steam_dir = find_steam_dir(args.steam_dir)
    repository = SchemaRevisionRepository(Path(args.data_dir))
    _migrate_legacy_edit_history(repository, args.app_id)
    payload, report = render_schema(
        schema_target(steam_dir, args.app_id),
        args.app_id,
        args.target_language,
        Path(args.edits_file),
        allow_incomplete=args.allow_incomplete,
    )
    _capture_revision(
        report,
        Path(args.data_dir),
        args.app_id,
        payload,
        action="draft",
        game_name=getattr(args, "game_name", None),
        target_language=args.target_language,
        variant_id=getattr(args, "variant_id", None),
    )
    if args.jsonl:
        emit_jsonl("schema-draft", "item-succeeded", report)
        emit_jsonl("schema-draft", "completed", {"count": 1, "exit_code": 0})
    else:
        print(f"已记录 App ID {args.app_id} 的草稿修订。")
    return 0


def command_schema_restore(args: argparse.Namespace) -> int:
    confirm(f"确认恢复 App ID {args.app_id} 的上一次本地编辑？", args.yes)
    if is_steam_running():
        raise PreflightError("Steam 正在运行。请从系统托盘正常退出 Steam 后重试。")
    steam_dir = find_steam_dir(args.steam_dir)
    _migrate_legacy_edit_history(
        SchemaRevisionRepository(Path(args.data_dir)), args.app_id
    )
    report = restore_schema(
        schema_target(steam_dir, args.app_id),
        args.app_id,
        Path(args.data_dir),
        force=args.force,
    )
    _capture_revision(
        report,
        Path(args.data_dir),
        args.app_id,
        Path(report["target"]).read_bytes(),
        action="restore",
    )
    if args.jsonl:
        emit_jsonl("schema-restore", "item-succeeded", report)
        emit_jsonl("schema-restore", "completed", {"count": 1, "exit_code": 0})
    else:
        print(f"已恢复：{report['target']}")
    return 0


def command_schema_revisions_list(args: argparse.Namespace) -> int:
    repository = SchemaRevisionRepository(Path(args.data_dir))
    _migrate_legacy_edit_history(repository, args.app_id)
    revisions = repository.list(args.app_id)
    current_sha256 = _current_schema_sha256(args)
    records = [
        revision.record()
        | {"is_current": revision.schema_sha256 == current_sha256, "available": True}
        for revision in revisions
    ]
    records.extend(
        _legacy_unavailable_records(
            Path(args.data_dir),
            args.app_id,
            {revision.schema_sha256 for revision in revisions},
        )
    )
    records.sort(key=lambda record: str(record.get("created_at") or ""), reverse=True)
    if args.jsonl:
        emit_jsonl("schema-revisions-list", "plan", {"count": len(records)})
        for record in records:
            emit_jsonl("schema-revisions-list", "item-succeeded", record)
        emit_jsonl("schema-revisions-list", "completed", {"count": len(records), "exit_code": 0})
    else:
        for record in records:
            marker = "*" if record["is_current"] else " "
            print(
                f"{marker} {record['short_commit']}  {record['created_at']}  "
                f"{record['action']}  {record['schema_sha256']}"
            )
    return 0


def command_schema_revisions_show(args: argparse.Namespace) -> int:
    revision = SchemaRevisionRepository(Path(args.data_dir)).get(args.app_id, args.revision)
    record = revision.record(include_schema=True)
    current_payload = _current_schema_payload(args)
    current_sha256 = hashlib.sha256(current_payload).hexdigest() if current_payload else ""
    record["is_current"] = revision.schema_sha256 == current_sha256
    if current_payload:
        record["current_preview"] = achievement_preview(current_payload)
    if args.jsonl:
        emit_jsonl("schema-revisions-show", "item-succeeded", record)
        emit_jsonl("schema-revisions-show", "completed", {"count": 1, "exit_code": 0})
    else:
        print(
            f"{revision.commit_id}：{revision.achievement_count} 个成就，"
            f"SHA-256 {revision.schema_sha256}"
        )
    return 0


def command_schema_revisions_export(args: argparse.Namespace) -> int:
    repository = SchemaRevisionRepository(Path(args.data_dir))
    revision = repository.export(args.app_id, args.revision, Path(args.output), args.format)
    record = revision.record() | {"output": str(Path(args.output).expanduser().resolve())}
    if args.jsonl:
        emit_jsonl("schema-revisions-export", "item-succeeded", record)
        emit_jsonl("schema-revisions-export", "completed", {"count": 1, "exit_code": 0})
    else:
        print(f"已导出修订：{record['output']}")
    return 0


def command_schema_revisions_activate(args: argparse.Namespace) -> int:
    confirm(f"确认把 App ID {args.app_id} 的所选修订设为当前版本？", args.yes)
    if is_steam_running():
        raise PreflightError("Steam 正在运行。请从系统托盘正常退出 Steam 后重试。")
    repository = SchemaRevisionRepository(Path(args.data_dir))
    revision = repository.get(args.app_id, args.revision)
    steam_dir = find_steam_dir(args.steam_dir)
    report = apply_schema_payload(
        schema_target(steam_dir, args.app_id),
        args.app_id,
        revision.schema,
        Path(args.data_dir),
        game_name=revision.game_name,
        target_language=revision.target_language,
    )
    committed = repository.record(
        args.app_id,
        revision.schema,
        action="activate",
        game_name=revision.game_name,
        target_language=revision.target_language,
        achievement_count=revision.achievement_count,
        variant_id=revision.variant_id,
    )
    report.update(committed.record())
    if args.jsonl:
        emit_jsonl("schema-revisions-activate", "item-succeeded", report)
        emit_jsonl("schema-revisions-activate", "completed", {"count": 1, "exit_code": 0})
    else:
        print(f"已设为当前版本：{committed.commit_id[:12]}")
    return 0


def command_schema_revisions_verify(args: argparse.Namespace) -> int:
    report = SchemaRevisionRepository(Path(args.data_dir)).verify(args.app_id)
    if args.jsonl:
        emit_jsonl("schema-revisions-verify", "item-succeeded", report)
        emit_jsonl("schema-revisions-verify", "completed", {"count": 1, "exit_code": 0})
    else:
        print(f"已验证 {report['verified']} 个修订。")
    return 0


def _capture_revision(
    report: dict,
    data_dir: Path,
    app_id: str,
    payload: bytes,
    *,
    action: str,
    game_name: str | None = None,
    target_language: str | None = None,
    variant_id: str | None = None,
) -> None:
    try:
        repository = SchemaRevisionRepository(data_dir)
        revision = repository.record(
            app_id,
            payload,
            action=action,
            game_name=game_name or "",
            target_language=target_language or "",
            achievement_count=int(report.get("achievement_count") or 0),
            changed_names=int(report.get("changed_names") or 0),
            changed_descriptions=int(report.get("changed_descriptions") or 0),
            variant_id=variant_id or "",
        )
        report["revision_commit"] = revision.commit_id
    except Exception as exc:  # The primary file operation already succeeded.
        report["revision_warning"] = f"操作已完成，但无法写入 Git 修订历史：{exc}"


def _exported_payload(path: Path, output_format: str, app_id: str) -> bytes:
    output = path.expanduser().resolve()
    if output_format == "bin":
        return output.read_bytes()
    with zipfile.ZipFile(output) as archive:
        return archive.read(f"UserGameStatsSchema_{app_id}.bin")


def _current_schema_sha256(args: argparse.Namespace) -> str:
    payload = _current_schema_payload(args)
    return hashlib.sha256(payload).hexdigest() if payload else ""


def _current_schema_payload(args: argparse.Namespace) -> bytes:
    try:
        target = schema_target(find_steam_dir(args.steam_dir), args.app_id)
        return target.read_bytes() if target.is_file() else b""
    except (OSError, PreflightError):
        return b""


def _migrate_legacy_edit_history(
    repository: SchemaRevisionRepository,
    app_id: str,
) -> None:
    """Best-effort, append-only import of recoverable pre-Git edit history."""
    try:
        if repository.list(app_id):
            return
        known_hashes: set[str] = set()
        data_dir = repository.data_dir
        transactions = EditHistoryStore(data_dir).transactions(app_id)
        for transaction in transactions:
            snapshot_value = transaction.get("snapshot")
            if not isinstance(snapshot_value, str) or not snapshot_value:
                continue
            snapshot = (data_dir / snapshot_value).resolve()
            try:
                snapshot.relative_to(data_dir)
            except ValueError:
                continue
            expected = str(transaction.get("original_sha256") or "")
            if not snapshot.is_file():
                continue
            payload = snapshot.read_bytes()
            if (
                not expected
                or hashlib.sha256(payload).hexdigest() != expected
                or expected in known_hashes
            ):
                continue
            revision = repository.record(
                app_id,
                payload,
                action="legacy-import",
                game_name=str(transaction.get("game_name") or ""),
                target_language=str(transaction.get("target_language") or ""),
            )
            known_hashes.add(revision.schema_sha256)

        if not transactions:
            return
        latest = transactions[-1]
        target_value = latest.get("target")
        edited_hash = str(latest.get("edited_sha256") or "")
        if not isinstance(target_value, str) or not target_value or not edited_hash:
            return
        target = Path(target_value).expanduser().resolve()
        if target.is_file():
            payload = target.read_bytes()
            if hashlib.sha256(payload).hexdigest() == edited_hash and edited_hash not in known_hashes:
                revision = repository.record(
                    app_id,
                    payload,
                    action="legacy-import",
                    game_name=str(latest.get("game_name") or ""),
                    target_language=str(latest.get("target_language") or ""),
                )
                known_hashes.add(revision.schema_sha256)
    except Exception:
        # Migration is deliberately best-effort. Strict repository operations
        # still report corruption through their normal reads and writes.
        return


def _legacy_unavailable_records(
    data_dir: Path,
    app_id: str,
    available_hashes: set[str],
) -> list[dict]:
    try:
        root = data_dir.expanduser().resolve()
        transactions = EditHistoryStore(root).transactions(app_id)
        records: list[dict] = []
        for index, transaction in enumerate(transactions):
            edited_hash = str(transaction.get("edited_sha256") or "")
            if not edited_hash or edited_hash in available_hashes:
                continue
            recoverable = False
            if index + 1 < len(transactions):
                next_transaction = transactions[index + 1]
                if str(next_transaction.get("original_sha256") or "") == edited_hash:
                    recoverable = _valid_legacy_snapshot(root, next_transaction, edited_hash)
            target_value = transaction.get("target")
            if not recoverable and isinstance(target_value, str) and target_value:
                target = Path(target_value).expanduser().resolve()
                try:
                    recoverable = target.is_file() and hashlib.sha256(target.read_bytes()).hexdigest() == edited_hash
                except OSError:
                    recoverable = False
            if recoverable:
                continue
            transaction_id = str(transaction.get("id") or index)
            records.append(
                {
                    "commit": f"legacy-unavailable-{transaction_id}",
                    "short_commit": "不可用",
                    "app_id": app_id,
                    "game_name": str(transaction.get("game_name") or ""),
                    "target_language": str(transaction.get("target_language") or ""),
                    "action": "legacy-unavailable",
                    "created_at": str(transaction.get("edited_at") or ""),
                    "schema_sha256": edited_hash,
                    "parent_schema_sha256": str(transaction.get("original_sha256") or ""),
                    "achievement_count": 0,
                    "changed_names": 0,
                    "changed_descriptions": 0,
                    "variant_id": "",
                    "is_current": False,
                    "available": False,
                }
            )
        return records
    except Exception:
        return []


def _valid_legacy_snapshot(
    data_dir: Path,
    transaction: dict,
    expected_hash: str,
) -> bool:
    snapshot_value = transaction.get("snapshot")
    if not isinstance(snapshot_value, str) or not snapshot_value:
        return False
    snapshot = (data_dir / snapshot_value).resolve()
    try:
        snapshot.relative_to(data_dir)
        return snapshot.is_file() and hashlib.sha256(snapshot.read_bytes()).hexdigest() == expected_hash
    except (OSError, ValueError):
        return False
