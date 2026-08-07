from __future__ import annotations

import hashlib
import json
import os
import re
import shutil
import uuid
import zipfile
from datetime import UTC, datetime
from pathlib import Path
from typing import Any, Iterator

from satli.bkv import (
    BinaryKeyValuesNode,
    achievement_preview,
    parse_binary_keyvalues,
    serialize_binary_keyvalues,
)
from satli.errors import IntegrityError, PreflightError, TransactionError, UsageError


LANGUAGE_RE = re.compile(r"^[a-z][a-z0-9_]{1,31}$")
EDITS_VERSION = 1
HISTORY_VERSION = 1


def sha256_bytes(payload: bytes) -> str:
    return hashlib.sha256(payload).hexdigest()


def sha256_path(path: Path) -> str:
    digest = hashlib.sha256()
    with Path(path).open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def inspect_schema(path: Path, app_id: str, data_dir: Path) -> dict[str, Any]:
    source = _validated_schema_path(path, app_id)
    try:
        payload = source.read_bytes()
    except OSError as exc:
        raise PreflightError(f"无法读取本地成就文件：{source}：{exc}") from exc
    preview = achievement_preview(payload)
    return {
        "app_id": app_id,
        "source_path": str(source),
        "source_sha256": sha256_bytes(payload),
        "can_restore": EditHistoryStore(data_dir).active(app_id) is not None,
        **preview,
    }


def render_schema(
    source_path: Path,
    app_id: str,
    target_language: str,
    edits_path: Path,
    *,
    allow_incomplete: bool,
) -> tuple[bytes, dict[str, Any]]:
    source = _validated_schema_path(source_path, app_id)
    language = _validate_language(target_language)
    try:
        original = source.read_bytes()
    except OSError as exc:
        raise PreflightError(f"无法读取本地成就文件：{source}：{exc}") from exc
    original_hash = sha256_bytes(original)
    edits = _load_edits(edits_path, app_id, language, original_hash)
    nodes = parse_binary_keyvalues(original)
    if serialize_binary_keyvalues(nodes) != original:
        raise IntegrityError("原始 Binary KeyValues 文件未通过字节级 roundtrip 校验")

    achievements = list(_achievement_nodes(nodes))
    by_id = {api_name: (name_node, desc_node) for api_name, name_node, desc_node in achievements}
    if len(by_id) != len(achievements):
        raise PreflightError("原始 schema 包含重复的成就 API ID，拒绝编辑")
    edit_rows = edits["rows"]
    expected_ids = set(by_id)
    actual_ids = set(edit_rows)
    if expected_ids != actual_ids:
        missing = sorted(expected_ids - actual_ids)
        extra = sorted(actual_ids - expected_ids)
        raise UsageError(
            "编辑内容的成就 ID 集合与源文件不一致"
            + (f"；缺少：{', '.join(missing)}" if missing else "")
            + (f"；多余：{', '.join(extra)}" if extra else "")
        )

    missing_names = 0
    missing_descriptions = 0
    changed_fields = 0
    changed_names = 0
    changed_descriptions = 0
    for api_name, (name_node, desc_node) in by_id.items():
        row = edit_rows[api_name]
        name = _validate_text(row["name"], api_name, "名称")
        description = _validate_text(row["description"], api_name, "说明")
        missing_names += not bool(name)
        missing_descriptions += not bool(description)
        name_changed = _set_language_value(name_node, language, name)
        description_changed = _set_language_value(desc_node, language, description)
        changed_names += name_changed
        changed_descriptions += description_changed
        changed_fields += name_changed + description_changed

    if (missing_names or missing_descriptions) and not allow_incomplete:
        raise PreflightError(
            f"目标语言内容不完整：缺少名称 {missing_names} 项，缺少说明 {missing_descriptions} 项；"
            "确认风险后使用 --allow-incomplete"
        )
    localized = serialize_binary_keyvalues(nodes)
    localized_preview = achievement_preview(localized)
    if localized_preview["achievement_count"] != len(achievements):
        raise IntegrityError("编辑后成就数量发生变化，拒绝输出")
    if serialize_binary_keyvalues(parse_binary_keyvalues(localized)) != localized:
        raise IntegrityError("编辑后的 Binary KeyValues 文件未通过字节级 roundtrip 校验")
    return localized, {
        "app_id": app_id,
        "target_language": language,
        "source_sha256": original_hash,
        "output_sha256": sha256_bytes(localized),
        "achievement_count": len(achievements),
        "changed_fields": changed_fields,
        "changed_names": changed_names,
        "changed_descriptions": changed_descriptions,
        "missing_names": missing_names,
        "missing_descriptions": missing_descriptions,
        "incomplete": bool(missing_names or missing_descriptions),
        "roundtrip_equal": True,
        "complete_languages": _complete_languages(localized_preview),
    }


def export_schema(
    source_path: Path,
    app_id: str,
    target_language: str,
    edits_path: Path,
    output_path: Path,
    output_format: str,
    *,
    allow_incomplete: bool,
) -> dict[str, Any]:
    payload, report = render_schema(
        source_path,
        app_id,
        target_language,
        edits_path,
        allow_incomplete=allow_incomplete,
    )
    output = Path(output_path).expanduser().resolve()
    source = Path(source_path).resolve()
    if output == source:
        raise UsageError("导出路径不能覆盖 Steam 当前使用的原始文件")
    if output_format == "bin":
        if output.suffix.lower() != ".bin":
            raise UsageError("BIN 导出路径必须使用 .bin 扩展名")
        _atomic_write(output, payload)
    elif output_format == "zip":
        if output.suffix.lower() != ".zip":
            raise UsageError("ZIP 导出路径必须使用 .zip 扩展名")
        _atomic_zip(output, f"UserGameStatsSchema_{app_id}.bin", payload)
    else:
        raise UsageError(f"不支持的导出格式：{output_format}")
    return {**report, "output": str(output), "format": output_format}


def _complete_languages(preview: dict[str, Any]) -> list[str]:
    rows = preview.get("rows")
    languages = preview.get("languages")
    if not isinstance(rows, list) or not isinstance(languages, list):
        return []
    complete: list[str] = []
    for language in languages:
        if not isinstance(language, str):
            continue
        if rows and all(
            isinstance(row, dict)
            and isinstance(row.get("translations"), dict)
            and isinstance(row["translations"].get(language), dict)
            and bool(row["translations"][language].get("name"))
            and bool(row["translations"][language].get("description"))
            for row in rows
        ):
            complete.append(language)
    return complete


class EditHistoryStore:
    def __init__(self, data_dir: Path) -> None:
        self.data_dir = Path(data_dir).expanduser().resolve()
        self.path = self.data_dir / "edit-history.json"

    def load(self) -> dict[str, Any]:
        if not self.path.is_file():
            return {"version": HISTORY_VERSION, "apps": {}}
        try:
            raw = json.loads(self.path.read_text(encoding="utf-8"))
        except (OSError, UnicodeError, json.JSONDecodeError) as exc:
            raise TransactionError(f"无法读取编辑历史：{self.path}：{exc}") from exc
        if not isinstance(raw, dict) or raw.get("version") != HISTORY_VERSION:
            raise TransactionError(f"不支持的编辑历史版本：{self.path}")
        if not isinstance(raw.get("apps"), dict):
            raise TransactionError("编辑历史 apps 字段无效")
        return raw

    def save(self, state: dict[str, Any]) -> None:
        payload = (json.dumps(state, ensure_ascii=False, indent=2, sort_keys=True) + "\n").encode()
        self.data_dir.mkdir(parents=True, exist_ok=True)
        _atomic_write(self.path, payload)

    def add(self, app_id: str, transaction: dict[str, Any]) -> None:
        state = self.load()
        app = state["apps"].setdefault(app_id, {"transactions": []})
        transactions = app.get("transactions")
        if not isinstance(transactions, list):
            raise TransactionError(f"{app_id} 的编辑事务记录无效")
        transactions.append(transaction)
        self.save(state)

    def active(self, app_id: str) -> dict[str, Any] | None:
        for transaction in reversed(self.transactions(app_id)):
            if isinstance(transaction, dict) and not transaction.get("restored_at"):
                return transaction
        return None

    def transactions(self, app_id: str) -> list[dict[str, Any]]:
        state = self.load()
        app = state["apps"].get(app_id)
        if app is None:
            return []
        if not isinstance(app, dict):
            raise TransactionError(f"{app_id} 的编辑历史无效")
        transactions = app.get("transactions")
        if not isinstance(transactions, list) or not all(
            isinstance(transaction, dict) for transaction in transactions
        ):
            raise TransactionError(f"{app_id} 的编辑事务记录无效")
        return list(transactions)

    def managed_app_ids(self) -> tuple[str, ...]:
        state = self.load()
        return tuple(sorted((str(key) for key in state["apps"]), key=lambda value: int(value)))

    def mark_restored(
        self,
        app_id: str,
        transaction_id: str,
        restored_at: str,
        forced_archive: str | None,
    ) -> None:
        state = self.load()
        transactions = state["apps"].get(app_id, {}).get("transactions", [])
        for transaction in transactions:
            if transaction.get("id") == transaction_id:
                transaction["restored_at"] = restored_at
                if forced_archive:
                    transaction["forced_archive"] = forced_archive
                self.save(state)
                return
        raise TransactionError(f"找不到编辑事务：{app_id}/{transaction_id}")


def apply_schema(
    source_path: Path,
    app_id: str,
    target_language: str,
    edits_path: Path,
    data_dir: Path,
    *,
    allow_incomplete: bool,
    game_name: str | None = None,
) -> dict[str, Any]:
    source = _validated_schema_path(source_path, app_id)
    localized, report = render_schema(
        source,
        app_id,
        target_language,
        edits_path,
        allow_incomplete=allow_incomplete,
    )
    return apply_schema_payload(
        source,
        app_id,
        localized,
        data_dir,
        game_name=game_name,
        target_language=target_language,
        report=report,
    )


def apply_schema_payload(
    source_path: Path,
    app_id: str,
    payload: bytes,
    data_dir: Path,
    *,
    game_name: str | None = None,
    target_language: str | None = None,
    report: dict[str, Any] | None = None,
) -> dict[str, Any]:
    source = _validated_schema_path(source_path, app_id)
    try:
        current = source.read_bytes()
    except OSError as exc:
        raise PreflightError(f"无法读取本地成就文件：{source}：{exc}") from exc
    current_sha256 = sha256_bytes(current)
    output_sha256 = sha256_bytes(payload)
    if current_sha256 == output_sha256:
        raise PreflightError("目标版本与当前文件完全相同，无需写回")
    preview = achievement_preview(payload)
    effective_report = {
        "app_id": app_id,
        "target_language": (target_language or "").strip().lower(),
        "source_sha256": current_sha256,
        "output_sha256": output_sha256,
        "achievement_count": preview["achievement_count"],
        "changed_fields": 0,
        "changed_names": 0,
        "changed_descriptions": 0,
        "missing_names": 0,
        "missing_descriptions": 0,
        "incomplete": False,
        "roundtrip_equal": True,
        "complete_languages": _complete_languages(preview),
        **(report or {}),
    }
    effective_report["source_sha256"] = current_sha256
    effective_report["output_sha256"] = output_sha256

    store = EditHistoryStore(data_dir)
    transaction_id = uuid.uuid4().hex
    backup_dir = store.data_dir / "edit-backups" / app_id / transaction_id
    snapshot = backup_dir / "original.bin"
    stage = source.with_name(f".{source.name}.{transaction_id}.tmp")
    replaced = False
    try:
        _copy_fsync(source, snapshot)
        if sha256_path(snapshot) != current_sha256:
            raise IntegrityError(f"编辑前备份校验失败：{snapshot}")
        _write_fsync_new(stage, payload)
        if sha256_path(stage) != output_sha256:
            raise IntegrityError("编辑暂存文件 SHA-256 校验失败")
        achievement_preview(stage.read_bytes())
        os.replace(stage, source)
        replaced = True
        transaction = {
            "id": transaction_id,
            "edited_at": _utc_now(),
            "game_name": game_name.strip() if game_name and game_name.strip() else None,
            "target": str(source),
            "target_language": (target_language or effective_report["target_language"]),
            "original_sha256": current_sha256,
            "edited_sha256": output_sha256,
            "snapshot": snapshot.relative_to(store.data_dir).as_posix(),
        }
        try:
            store.add(app_id, transaction)
        except TransactionError as exc:
            _copy_fsync(snapshot, source)
            raise TransactionError(f"保存编辑历史失败，已回滚本地文件：{exc}") from exc
        return {**effective_report, "target": str(source), "backup": str(snapshot)}
    except (OSError, IntegrityError, TransactionError) as exc:
        if isinstance(exc, (IntegrityError, TransactionError)):
            raise
        raise TransactionError(f"写回本地成就文件失败：{exc}") from exc
    finally:
        stage.unlink(missing_ok=True)
        if not replaced:
            shutil.rmtree(backup_dir, ignore_errors=True)


def restore_schema(source_path: Path, app_id: str, data_dir: Path, *, force: bool) -> dict[str, Any]:
    source = _validated_schema_path(source_path, app_id)
    store = EditHistoryStore(data_dir)
    transaction = store.active(app_id)
    if transaction is None:
        raise TransactionError(f"{app_id} 没有可恢复的本地编辑记录")
    if Path(str(transaction.get("target") or "")).resolve() != source:
        raise TransactionError("编辑历史中的 Steam 文件路径与当前 Steam 目录不一致")
    expected = str(transaction.get("edited_sha256") or "")
    actual = sha256_path(source)
    if actual != expected and not force:
        raise TransactionError(
            f"当前文件已在编辑后发生变化（当前 {actual}，预期 {expected}），拒绝普通恢复"
        )
    snapshot_value = str(transaction.get("snapshot") or "")
    snapshot = (store.data_dir / snapshot_value).resolve()
    try:
        snapshot.relative_to(store.data_dir)
    except ValueError as exc:
        raise TransactionError("编辑历史中的备份路径越界") from exc
    if not snapshot.is_file():
        raise TransactionError(f"找不到编辑前备份：{snapshot}")
    original_hash = str(transaction.get("original_sha256") or "")
    if sha256_path(snapshot) != original_hash:
        raise IntegrityError(f"编辑前备份 SHA-256 不匹配：{snapshot}")

    backup_dir = snapshot.parent
    restore_id = uuid.uuid4().hex
    rollback = backup_dir / f"restore-rollback-{restore_id}.bin"
    forced = backup_dir / f"forced-current-{restore_id}.bin"
    _copy_fsync(source, rollback)
    try:
        if force and actual != expected:
            _copy_fsync(source, forced)
        _copy_fsync(snapshot, source)
        if sha256_path(source) != original_hash:
            raise IntegrityError("恢复后的本地文件 SHA-256 校验失败")
        achievement_preview(source.read_bytes())
        forced_value = forced.relative_to(store.data_dir).as_posix() if forced.is_file() else None
        try:
            store.mark_restored(app_id, str(transaction["id"]), _utc_now(), forced_value)
        except TransactionError as exc:
            _copy_fsync(rollback, source)
            raise TransactionError(f"保存恢复状态失败，已回滚：{exc}") from exc
        return {
            "app_id": app_id,
            "target": str(source),
            "restored_sha256": original_hash,
            "forced_archive": str(forced) if forced.is_file() else None,
            "can_restore": store.active(app_id) is not None,
        }
    finally:
        rollback.unlink(missing_ok=True)


def _load_edits(path: Path, app_id: str, language: str, source_hash: str) -> dict[str, Any]:
    try:
        raw = json.loads(Path(path).read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise UsageError(f"无法读取编辑内容：{path}：{exc}") from exc
    if not isinstance(raw, dict) or raw.get("version") != EDITS_VERSION:
        raise UsageError("编辑内容必须使用 version 1")
    if str(raw.get("app_id") or "") != app_id:
        raise UsageError("编辑内容的 App ID 与命令参数不一致")
    if str(raw.get("source_sha256") or "").lower() != source_hash:
        raise IntegrityError("本地成就文件已变化，请重新加载后再输出")
    if str(raw.get("target_language") or "").lower() != language:
        raise UsageError("编辑内容的目标语言与命令参数不一致")
    rows = raw.get("rows")
    if not isinstance(rows, list):
        raise UsageError("编辑内容 rows 必须是数组")
    result: dict[str, dict[str, str]] = {}
    for item in rows:
        if not isinstance(item, dict):
            raise UsageError("编辑内容包含无效行")
        api_name = str(item.get("api_name") or "").strip()
        if not api_name or api_name in result:
            raise UsageError(f"编辑内容包含空白或重复成就 ID：{api_name or '<空>'}")
        name = item.get("name")
        description = item.get("description")
        if not isinstance(name, str) or not isinstance(description, str):
            raise UsageError(f"{api_name} 的名称和说明必须是字符串")
        result[api_name] = {"name": name, "description": description}
    return {"rows": result}


def _achievement_nodes(
    nodes: list[BinaryKeyValuesNode],
) -> Iterator[tuple[str, BinaryKeyValuesNode, BinaryKeyValuesNode]]:
    for node in _walk(nodes):
        if node.type_id != 0 or node.name != "bits":
            continue
        for achievement in node.children:
            if achievement.type_id != 0:
                continue
            api_name = _first_string(achievement, "name")
            display = _first_object(achievement, "display")
            name_node = _first_object(display, "name") if display else None
            desc_node = _first_object(display, "desc") if display else None
            if api_name and name_node is not None and desc_node is not None:
                yield api_name, name_node, desc_node


def _walk(nodes: list[BinaryKeyValuesNode]) -> Iterator[BinaryKeyValuesNode]:
    for node in nodes:
        yield node
        yield from _walk(node.children)


def _first_object(node: BinaryKeyValuesNode, name: str) -> BinaryKeyValuesNode | None:
    return next(
        (child for child in node.children if child.type_id == 0 and child.name == name),
        None,
    )


def _first_string(node: BinaryKeyValuesNode, name: str) -> str:
    child = next(
        (child for child in node.children if child.type_id == 1 and child.name == name),
        None,
    )
    return child.value or "" if child else ""


def _set_language_value(node: BinaryKeyValuesNode, language: str, value: str) -> int:
    matches = [
        child
        for child in node.children
        if child.type_id == 1 and child.name.casefold() == language.casefold()
    ]
    if len(matches) > 1:
        raise PreflightError(f"目标语言 {language} 在同一字段中出现重复节点，拒绝编辑")
    encoded = value.encode("utf-8")
    if matches:
        changed = matches[0].raw_value != encoded
        matches[0].raw_value = encoded
        matches[0].value = value
        return int(changed)
    node.children.append(
        BinaryKeyValuesNode(type_id=1, name=language, value=value, raw_value=encoded)
    )
    return 1


def _validate_language(value: str) -> str:
    language = str(value or "").strip().lower()
    if language in {"token", "tokens"} or not LANGUAGE_RE.fullmatch(language):
        raise UsageError(f"无效的 Steam 语言代码：{value}")
    return language


def _validate_text(value: str, api_name: str, label: str) -> str:
    if any(char in value for char in ("\0", "\r", "\n", "\t")) or any(
        ord(char) < 32 for char in value
    ):
        raise UsageError(f"{api_name} 的{label}包含 NUL、换行、制表符或控制字符")
    return value


def _validated_schema_path(path: Path, app_id: str) -> Path:
    source = Path(path).expanduser().resolve()
    expected = f"UserGameStatsSchema_{app_id}.bin"
    if not app_id.isdigit() or source.name != expected:
        raise UsageError(f"schema 文件名必须是 {expected}")
    if not source.is_file():
        raise PreflightError(f"找不到本地成就文件：{source}")
    return source


def _write_fsync_new(path: Path, payload: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("xb") as handle:
        handle.write(payload)
        handle.flush()
        os.fsync(handle.fileno())


def _atomic_write(path: Path, payload: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(f".{path.name}.{uuid.uuid4().hex}.tmp")
    try:
        _write_fsync_new(temporary, payload)
        os.replace(temporary, path)
    except OSError as exc:
        raise TransactionError(f"无法写入文件：{path}：{exc}") from exc
    finally:
        temporary.unlink(missing_ok=True)


def _atomic_zip(path: Path, member_name: str, payload: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(f".{path.name}.{uuid.uuid4().hex}.tmp")
    try:
        with zipfile.ZipFile(temporary, "x", compression=zipfile.ZIP_DEFLATED) as archive:
            archive.writestr(member_name, payload)
        os.replace(temporary, path)
    except (OSError, zipfile.BadZipFile) as exc:
        raise TransactionError(f"无法写入投稿 ZIP：{path}：{exc}") from exc
    finally:
        temporary.unlink(missing_ok=True)


def _copy_fsync(source: Path, destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_name(f".{destination.name}.{uuid.uuid4().hex}.tmp")
    try:
        with source.open("rb") as reader, temporary.open("xb") as writer:
            shutil.copyfileobj(reader, writer, length=1024 * 1024)
            writer.flush()
            os.fsync(writer.fileno())
        os.replace(temporary, destination)
    except OSError as exc:
        raise TransactionError(f"无法复制文件：{source} -> {destination}：{exc}") from exc
    finally:
        temporary.unlink(missing_ok=True)


def _utc_now() -> str:
    return datetime.now(UTC).replace(microsecond=0).isoformat().replace("+00:00", "Z")
