from __future__ import annotations

import hashlib
import json
import stat
import zipfile
from argparse import Namespace
from pathlib import Path

import pytest

from satli.bkv import achievement_preview
from satli.errors import IntegrityError, PreflightError, TransactionError, UsageError
from satli.schema_command import command_schema_apply
from satli.schema_edit import (
    EditHistoryStore,
    apply_schema,
    export_schema,
    inspect_schema,
    render_schema,
    restore_schema,
)


def _string(name: str, value: str) -> bytes:
    return b"\x01" + name.encode() + b"\0" + value.encode("utf-8") + b"\0"


def _object(name: str, *children: bytes) -> bytes:
    return b"\x00" + name.encode() + b"\0" + b"".join(children) + b"\x08"


def _schema() -> bytes:
    achievements = []
    for index in range(2):
        achievements.append(
            _object(
                str(index),
                _string("name", f"ACH_{index}"),
                _object(
                    "display",
                    _object(
                        "name",
                        _string("token", f"ACH_{index}_NAME"),
                        _string("english", f"Name {index}"),
                        _string("schinese", f"名称 {index}"),
                    ),
                    _object(
                        "desc",
                        _string("token", f"ACH_{index}_DESC"),
                        _string("english", f"Description {index}"),
                        _string("schinese", f"说明 {index}"),
                    ),
                ),
            )
        )
    return _object("UserGameStatsSchema", _object("bits", *achievements)) + b"\x08"


def _fixture(tmp_path: Path) -> tuple[Path, Path, Path]:
    source = tmp_path / "Steam" / "appcache" / "stats" / "UserGameStatsSchema_123.bin"
    source.parent.mkdir(parents=True)
    source.write_bytes(_schema())
    data_dir = tmp_path / "data"
    edits = tmp_path / "edits.json"
    _write_edits(source, edits, "japanese", [("ACH_0", "名 0", "説 0"), ("ACH_1", "", "")])
    return source, data_dir, edits


def _write_edits(
    source: Path,
    path: Path,
    language: str,
    rows: list[tuple[str, str, str]],
) -> None:
    path.write_text(
        json.dumps(
            {
                "version": 1,
                "app_id": "123",
                "source_sha256": hashlib.sha256(source.read_bytes()).hexdigest(),
                "target_language": language,
                "rows": [
                    {"api_name": api_name, "name": name, "description": description}
                    for api_name, name, description in rows
                ],
            },
            ensure_ascii=False,
        ),
        encoding="utf-8",
    )


def test_inspect_reports_hash_content_and_restore_state(tmp_path: Path) -> None:
    source, data_dir, _ = _fixture(tmp_path)
    report = inspect_schema(source, "123", data_dir)

    assert report["roundtrip_equal"] is True
    assert report["achievement_count"] == 2
    assert report["source_sha256"] == hashlib.sha256(_schema()).hexdigest()
    assert report["can_restore"] is False


def test_apply_and_restore_preserve_read_only_schema(tmp_path: Path) -> None:
    source, data_dir, edits = _fixture(tmp_path)
    source.chmod(stat.S_IREAD)

    apply_schema(source, "123", "japanese", edits, data_dir, allow_incomplete=True)
    assert not source.stat().st_mode & stat.S_IWRITE

    restore_schema(source, "123", data_dir, force=False)
    assert not source.stat().st_mode & stat.S_IWRITE


def test_render_changes_only_target_language_and_allows_incomplete(tmp_path: Path) -> None:
    source, _, edits = _fixture(tmp_path)
    localized, report = render_schema(
        source, "123", "japanese", edits, allow_incomplete=True
    )
    preview = achievement_preview(localized)

    assert report["missing_names"] == 1
    assert report["missing_descriptions"] == 1
    assert preview["rows"][0]["translations"]["japanese"] == {
        "name": "名 0",
        "description": "説 0",
    }
    assert preview["rows"][0]["translations"]["english"]["name"] == "Name 0"
    assert b"ACH_0_NAME" in localized


def test_render_rejects_incomplete_stale_and_unsafe_edits(tmp_path: Path) -> None:
    source, _, edits = _fixture(tmp_path)
    with pytest.raises(PreflightError, match="内容不完整"):
        render_schema(source, "123", "japanese", edits, allow_incomplete=False)

    raw = json.loads(edits.read_text(encoding="utf-8"))
    raw["rows"][0]["name"] = "bad\ntext"
    edits.write_text(json.dumps(raw), encoding="utf-8")
    with pytest.raises(UsageError, match="控制字符"):
        render_schema(source, "123", "japanese", edits, allow_incomplete=True)

    raw["rows"][0]["name"] = "ok"
    edits.write_text(json.dumps(raw), encoding="utf-8")
    source.write_bytes(source.read_bytes() + b"\x00")
    with pytest.raises(IntegrityError, match="已变化"):
        render_schema(source, "123", "japanese", edits, allow_incomplete=True)


def test_render_rejects_duplicate_target_language_nodes(tmp_path: Path) -> None:
    source, _, edits = _fixture(tmp_path)
    duplicated = source.read_bytes().replace(
        _string("schinese", "名称 0"),
        _string("schinese", "名称 0") + _string("schinese", "重复名称"),
        1,
    )
    source.write_bytes(duplicated)
    _write_edits(
        source,
        edits,
        "schinese",
        [("ACH_0", "新名称 0", "新说明 0"), ("ACH_1", "新名称 1", "新说明 1")],
    )

    with pytest.raises(PreflightError, match="重复节点"):
        render_schema(source, "123", "schinese", edits, allow_incomplete=False)


def test_export_writes_verified_bin_and_single_member_zip(tmp_path: Path) -> None:
    source, _, edits = _fixture(tmp_path)
    bin_path = tmp_path / "UserGameStatsSchema_123.localized.bin"
    zip_path = tmp_path / "UserGameStatsSchema_123.zip"

    export_schema(
        source, "123", "japanese", edits, bin_path, "bin", allow_incomplete=True
    )
    export_schema(
        source, "123", "japanese", edits, zip_path, "zip", allow_incomplete=True
    )

    assert achievement_preview(bin_path.read_bytes())["achievement_count"] == 2
    with zipfile.ZipFile(zip_path) as archive:
        assert archive.namelist() == ["UserGameStatsSchema_123.bin"]
        assert archive.read(archive.namelist()[0]) == bin_path.read_bytes()


def test_apply_and_restore_support_consecutive_undo(tmp_path: Path) -> None:
    source, data_dir, edits = _fixture(tmp_path)
    original = source.read_bytes()
    first = apply_schema(
        source, "123", "japanese", edits, data_dir, allow_incomplete=True
    )
    first_bytes = source.read_bytes()
    assert first["source_sha256"] != first["output_sha256"]

    _write_edits(
        source,
        edits,
        "japanese",
        [("ACH_0", "二回目 0", "二回目の説明 0"), ("ACH_1", "二回目 1", "二回目の説明 1")],
    )
    apply_schema(source, "123", "japanese", edits, data_dir, allow_incomplete=False)

    restored_second = restore_schema(source, "123", data_dir, force=False)
    assert source.read_bytes() == first_bytes
    assert restored_second["can_restore"] is True
    restored_first = restore_schema(source, "123", data_dir, force=False)
    assert source.read_bytes() == original
    assert restored_first["can_restore"] is False


def test_restore_rejects_external_change_and_force_archives_it(tmp_path: Path) -> None:
    source, data_dir, edits = _fixture(tmp_path)
    original = source.read_bytes()
    apply_schema(source, "123", "japanese", edits, data_dir, allow_incomplete=True)
    externally_changed = source.read_bytes().replace(b"\xe5\x90\x8d 0", b"\xe5\x90\x8d X")
    source.write_bytes(externally_changed)

    with pytest.raises(TransactionError, match="发生变化"):
        restore_schema(source, "123", data_dir, force=False)
    result = restore_schema(source, "123", data_dir, force=True)

    assert source.read_bytes() == original
    assert result["forced_archive"]
    assert Path(result["forced_archive"]).read_bytes() == externally_changed


def test_apply_rolls_back_when_history_cannot_be_saved(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    source, data_dir, edits = _fixture(tmp_path)
    original = source.read_bytes()

    def fail_history(
        self: EditHistoryStore, app_id: str, transaction: dict[str, object]
    ) -> None:
        raise TransactionError("simulated history failure")

    monkeypatch.setattr(EditHistoryStore, "add", fail_history)
    with pytest.raises(TransactionError, match="已回滚"):
        apply_schema(source, "123", "japanese", edits, data_dir, allow_incomplete=True)

    assert source.read_bytes() == original
    assert not list(source.parent.glob(f".{source.name}.*.tmp"))


def test_schema_apply_refuses_to_write_while_steam_is_running(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr("satli.schema_command.is_steam_running", lambda: True)

    with pytest.raises(PreflightError, match="Steam 正在运行"):
        command_schema_apply(Namespace(app_id="123", yes=True))
