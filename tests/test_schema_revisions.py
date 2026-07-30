from __future__ import annotations

import json
import hashlib
import subprocess
import zipfile
from pathlib import Path

import pytest

from satl.errors import TransactionError
from satl.cli import main
from satl.schema_command import _legacy_unavailable_records, _migrate_legacy_edit_history
from satl.schema_edit import EditHistoryStore
from satl.schema_revisions import METADATA_VERSION, SchemaRevisionRepository
from test_managed_games import _schema


def test_bare_repository_records_deduplicates_and_lists_by_game(tmp_path: Path) -> None:
    repository = SchemaRevisionRepository(tmp_path / "data")
    first = repository.record(
        "123",
        _schema("第一版"),
        action="export",
        game_name="Revision Game",
        target_language="schinese",
    )
    duplicate = repository.record(
        "123",
        _schema("第一版"),
        action="apply",
        game_name="Revision Game",
        target_language="schinese",
    )
    second = repository.record(
        "123",
        _schema("第二版"),
        action="apply",
        game_name="Revision Game",
        target_language="schinese",
    )
    repository.record("456", _schema("其他游戏"), action="export")
    activated = repository.record(
        "123",
        first.schema,
        action="activate",
        game_name="Revision Game",
        target_language="schinese",
    )

    assert first.commit_id == duplicate.commit_id
    assert second.commit_id != first.commit_id
    assert activated.commit_id != first.commit_id
    history = repository.list("123")
    assert [item.schema_sha256 for item in history] == [
        first.schema_sha256,
        second.schema_sha256,
        first.schema_sha256,
    ]
    assert history[0].parent_schema_sha256 == second.schema_sha256
    assert repository.verify("123")["verified"] == 3
    assert repository.verify()["verified"] == 4


def test_revision_export_and_standard_git_can_read_repository(tmp_path: Path) -> None:
    repository = SchemaRevisionRepository(tmp_path / "data")
    revision = repository.record(
        "123",
        _schema("可导出版本"),
        action="export",
        target_language="schinese",
    )
    output = tmp_path / "UserGameStatsSchema_123.zip"

    repository.export("123", revision.commit_id[:12], output, "zip")

    with zipfile.ZipFile(output) as archive:
        assert archive.read("UserGameStatsSchema_123.bin") == revision.schema
    result = subprocess.run(
        ["git", f"--git-dir={repository.path}", "show", "main:games/123/metadata.json"],
        check=True,
        capture_output=True,
        text=True,
        encoding="utf-8",
    )
    assert json.loads(result.stdout)["version"] == METADATA_VERSION
    assert subprocess.run(
        ["git", f"--git-dir={repository.path}", "remote"],
        check=True,
        capture_output=True,
        text=True,
    ).stdout == ""


def test_best_effort_migration_imports_snapshot_and_current_file(tmp_path: Path) -> None:
    data_dir = tmp_path / "data"
    source = tmp_path / "UserGameStatsSchema_123.bin"
    original = _schema("Original")
    edited = _schema("Edited")
    source.write_bytes(edited)
    snapshot = data_dir / "edit-backups" / "123" / "old" / "original.bin"
    snapshot.parent.mkdir(parents=True)
    snapshot.write_bytes(original)
    EditHistoryStore(data_dir).add(
        "123",
        {
            "id": "old",
            "edited_at": "2026-01-01T00:00:00Z",
            "game_name": "Test Game",
            "target": str(source),
            "target_language": "schinese",
            "original_sha256": hashlib.sha256(original).hexdigest(),
            "edited_sha256": hashlib.sha256(edited).hexdigest(),
            "snapshot": snapshot.relative_to(data_dir).as_posix(),
        },
    )
    repository = SchemaRevisionRepository(data_dir)

    _migrate_legacy_edit_history(repository, "123")
    _migrate_legacy_edit_history(repository, "123")

    revisions = repository.list("123")
    assert [item.schema for item in reversed(revisions)] == [original, edited]
    assert all(item.action == "legacy-import" for item in revisions)


def test_lost_legacy_edit_is_reported_as_unavailable(tmp_path: Path) -> None:
    data_dir = tmp_path / "data"
    lost_hash = hashlib.sha256(b"lost-result").hexdigest()
    EditHistoryStore(data_dir).add(
        "123",
        {
            "id": "lost",
            "edited_at": "2026-01-01T00:00:00Z",
            "game_name": "Lost Game",
            "target": str(tmp_path / "missing.bin"),
            "target_language": "schinese",
            "original_sha256": hashlib.sha256(b"original").hexdigest(),
            "edited_sha256": lost_hash,
            "snapshot": "edit-backups/123/lost/original.bin",
        },
    )

    records = _legacy_unavailable_records(data_dir, "123", set())

    assert len(records) == 1
    assert records[0]["available"] is False
    assert records[0]["schema_sha256"] == lost_hash


def test_ref_conflict_is_reported_without_overwriting_history(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    repository = SchemaRevisionRepository(tmp_path / "data")
    first = repository.record("123", _schema("First"), action="export")
    refs_type = type(repository._open(create=False).refs)
    monkeypatch.setattr(refs_type, "set_if_equals", lambda *args, **kwargs: False)

    with pytest.raises(TransactionError, match="并发变化"):
        repository.record("123", _schema("Second"), action="export")

    assert repository.list("123")[0].commit_id == first.commit_id


def test_corrupt_repository_is_not_reinitialized(tmp_path: Path) -> None:
    repository = SchemaRevisionRepository(tmp_path / "data")
    repository.path.mkdir(parents=True)
    marker = repository.path / "do-not-delete.txt"
    marker.write_text("corrupt", encoding="utf-8")

    with pytest.raises(TransactionError, match="无法打开修订仓库"):
        repository.record("123", _schema("First"), action="export")

    assert marker.read_text(encoding="utf-8") == "corrupt"


def test_show_reports_target_and_current_previews(tmp_path: Path, capsys) -> None:
    steam = tmp_path / "Steam"
    steam.mkdir()
    (steam / "steam.exe").touch()
    target = steam / "appcache" / "stats" / "UserGameStatsSchema_123.bin"
    target.parent.mkdir(parents=True)
    target.write_bytes(_schema("Current"))
    data_dir = tmp_path / "data"
    revision = SchemaRevisionRepository(data_dir).record(
        "123", _schema("Target"), action="export"
    )

    assert main(
        [
            "schema", "revisions", "show", "123", revision.commit_id,
            "--steam-dir", str(steam),
            "--data-dir", str(data_dir),
            "--jsonl",
        ]
    ) == 0

    events = [json.loads(line) for line in capsys.readouterr().out.splitlines()]
    payload = next(event["payload"] for event in events if event["event"] == "item-succeeded")
    assert payload["preview"]["rows"][0]["translations"]["schinese"]["name"] == "Target"
    assert payload["current_preview"]["rows"][0]["translations"]["schinese"]["name"] == "Current"


def test_draft_records_rendered_content_without_writing_steam_file(
    tmp_path: Path, capsys
) -> None:
    steam = tmp_path / "Steam"
    steam.mkdir()
    (steam / "steam.exe").touch()
    target = steam / "appcache" / "stats" / "UserGameStatsSchema_123.bin"
    target.parent.mkdir(parents=True)
    original = _schema("Original")
    target.write_bytes(original)
    edits = tmp_path / "edits.json"
    edits.write_text(
        json.dumps(
            {
                "version": 1,
                "app_id": "123",
                "source_sha256": hashlib.sha256(original).hexdigest(),
                "target_language": "schinese",
                "rows": [
                    {
                        "api_name": "ACH_FIRST",
                        "name": "Draft",
                        "description": "完成目标",
                    }
                ],
            }
        ),
        encoding="utf-8",
    )
    data_dir = tmp_path / "data"

    assert main(
        [
            "schema", "draft", "123",
            "--steam-dir", str(steam),
            "--data-dir", str(data_dir),
            "--target-language", "schinese",
            "--edits-file", str(edits),
            "--game-name", "Draft Game",
            "--jsonl",
        ]
    ) == 0

    events = [json.loads(line) for line in capsys.readouterr().out.splitlines()]
    payload = next(event["payload"] for event in events if event["event"] == "item-succeeded")
    revisions = SchemaRevisionRepository(data_dir).list("123")
    assert payload["revision_commit"] == revisions[0].commit_id
    assert revisions[0].action == "draft"
    assert revisions[0].schema != original
    assert target.read_bytes() == original
