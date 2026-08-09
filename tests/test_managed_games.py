from __future__ import annotations

import hashlib
import json
import stat
from pathlib import Path

from satli.managed_games import ManagedGameRegistry
from satli.models import SchemaVariant
from satli.schema_edit import apply_schema
from satli.transaction import TransactionManager


def test_existing_local_edit_without_stored_name_uses_steam_manifest(tmp_path: Path) -> None:
    target, data_dir, _ = _fixture(tmp_path)
    edits = _write_edits(target, tmp_path / "edits.json", "本地名称")
    apply_schema(target, "123", "schinese", edits, data_dir, allow_incomplete=False)

    record = ManagedGameRegistry(data_dir).record("123")

    assert record.installed_source == "local-edit"
    assert record.game_name == "本地清单游戏"


def test_local_edit_layer_restores_before_underlying_catalog_install(tmp_path: Path) -> None:
    target, data_dir, original = _fixture(tmp_path)
    installed = original.replace("原始名称".encode(), "社区名称".encode())
    _install(target, data_dir, installed)
    edits = _write_edits(target, tmp_path / "edits.json", "本地名称")
    apply_schema(target, "123", "schinese", edits, data_dir, allow_incomplete=False)

    registry = ManagedGameRegistry(data_dir)
    assert registry.record("123").installed_source == "local-edit"

    registry.restore("123", target, force=False)

    assert target.read_bytes() == installed
    restored_record = registry.record("123")
    assert restored_record.installed_source == "catalog"
    assert restored_record.installed_state == "installed"


def test_catalog_install_layer_restores_to_underlying_local_edit(tmp_path: Path) -> None:
    target, data_dir, _ = _fixture(tmp_path)
    edits = _write_edits(target, tmp_path / "edits.json", "本地名称")
    apply_schema(target, "123", "schinese", edits, data_dir, allow_incomplete=False)
    edited = target.read_bytes()
    installed = edited.replace("本地名称".encode(), "社区名称".encode())
    _install(target, data_dir, installed)

    registry = ManagedGameRegistry(data_dir)
    assert registry.record("123").installed_source == "catalog"

    registry.restore("123", target, force=False)

    assert target.read_bytes() == edited
    restored_record = registry.record("123")
    assert restored_record.installed_source == "local-edit"
    assert restored_record.installed_state == "installed"


def test_managed_record_reports_read_only_schema(tmp_path: Path) -> None:
    target, data_dir, original = _fixture(tmp_path)
    _install(target, data_dir, original.replace("原始名称".encode(), "社区名称".encode()))
    target.chmod(stat.S_IREAD)

    record = ManagedGameRegistry(data_dir).record("123")

    assert record.file_read_only is True


def _fixture(tmp_path: Path) -> tuple[Path, Path, bytes]:
    target = tmp_path / "Steam" / "appcache" / "stats" / "UserGameStatsSchema_123.bin"
    target.parent.mkdir(parents=True)
    manifest = tmp_path / "Steam" / "steamapps" / "appmanifest_123.acf"
    manifest.parent.mkdir(parents=True)
    manifest.write_text(
        '"AppState" { "appid" "123" "name" "本地清单游戏" }',
        encoding="utf-8",
    )
    original = _schema("原始名称")
    target.write_bytes(original)
    return target, tmp_path / "data", original


def _install(target: Path, data_dir: Path, payload: bytes) -> None:
    source = data_dir / "catalog.bin"
    source.parent.mkdir(parents=True, exist_ok=True)
    source.write_bytes(payload)
    TransactionManager(data_dir).install(
        "123",
        target,
        source,
        SchemaVariant(
            variant_id="default",
            primary=True,
            schema_file="UserGameStatsSchema_123.bin",
            sha256=hashlib.sha256(payload).hexdigest(),
            file_size_bytes=len(payload),
        ),
        game_name="Layered Game",
    )


def _write_edits(source: Path, output: Path, name: str) -> Path:
    output.write_text(
        json.dumps(
            {
                "version": 1,
                "app_id": "123",
                "source_sha256": hashlib.sha256(source.read_bytes()).hexdigest(),
                "target_language": "schinese",
                "rows": [
                    {"api_name": "ACH_FIRST", "name": name, "description": "完成目标"}
                ],
            },
            ensure_ascii=False,
        ),
        encoding="utf-8",
    )
    return output


def _schema(name: str) -> bytes:
    def string(key: str, value: str) -> bytes:
        return b"\x01" + key.encode() + b"\0" + value.encode("utf-8") + b"\0"

    def object_node(key: str, *children: bytes) -> bytes:
        return b"\x00" + key.encode() + b"\0" + b"".join(children) + b"\x08"

    achievement = object_node(
        "0",
        string("name", "ACH_FIRST"),
        object_node(
            "display",
            object_node("name", string("english", "First"), string("schinese", name)),
            object_node(
                "desc",
                string("english", "Do it"),
                string("schinese", "完成目标"),
            ),
        ),
    )
    return object_node("UserGameStatsSchema", object_node("bits", achievement)) + b"\x08"
