from __future__ import annotations

import hashlib
import json
import zipfile
from pathlib import Path

import pytest

from satli.cli import main
from satli.errors import PreflightError, UsageError
from satli.local_import import read_local_import


def _string(name: str, value: str) -> bytes:
    return b"\x01" + name.encode() + b"\0" + value.encode("utf-8") + b"\0"


def _object(name: str, *children: bytes) -> bytes:
    return b"\x00" + name.encode() + b"\0" + b"".join(children) + b"\x08"


def schema_bytes() -> bytes:
    achievement = _object(
        "0",
        _string("name", "ACH_LOCAL"),
        _object(
            "display",
            _object("name", _string("english", "Local"), _string("schinese", "本地")),
            _object("desc", _string("english", "Import it"), _string("schinese", "导入它")),
        ),
    )
    return _object("UserGameStatsSchema", _object("bits", achievement)) + b"\x08"


def make_steam(tmp_path: Path) -> Path:
    steam = tmp_path / "Steam"
    (steam / "steamapps").mkdir(parents=True)
    (steam / "steam.exe").write_bytes(b"")
    (steam / "steamapps" / "appmanifest_123.acf").write_text(
        '"AppState" { "appid" "123" "name" "Local Game" }', encoding="utf-8"
    )
    return steam


def jsonl_events(output: str) -> list[dict[str, object]]:
    return [json.loads(line) for line in output.splitlines() if line.strip()]


def test_read_local_import_accepts_canonical_bin_and_zip(tmp_path: Path) -> None:
    payload = schema_bytes()
    bin_path = tmp_path / "UserGameStatsSchema_123.bin"
    bin_path.write_bytes(payload)
    zip_path = tmp_path / "UserGameStatsSchema_123.zip"
    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        archive.writestr(bin_path.name, payload)

    for path in (bin_path, zip_path):
        artifact = read_local_import(path)
        assert artifact.app_id == "123"
        assert artifact.payload == payload
        assert artifact.sha256 == hashlib.sha256(payload).hexdigest()
        assert artifact.preview["achievement_count"] == 1


def test_read_local_import_rejects_noncanonical_names_and_archives(tmp_path: Path) -> None:
    renamed = tmp_path / "translated.bin"
    renamed.write_bytes(schema_bytes())
    with pytest.raises(UsageError, match="UserGameStatsSchema"):
        read_local_import(renamed)

    archive_path = tmp_path / "UserGameStatsSchema_123.zip"
    with zipfile.ZipFile(archive_path, "w") as archive:
        archive.writestr("nested/UserGameStatsSchema_123.bin", schema_bytes())
    with pytest.raises(PreflightError, match="根目录"):
        read_local_import(archive_path)


def test_local_import_preview_emits_verified_content_without_writes(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    steam = make_steam(tmp_path)
    source = tmp_path / "UserGameStatsSchema_123.bin"
    source.write_bytes(schema_bytes())
    data_dir = tmp_path / "data"

    result = main(
        [
            "local-import",
            str(source),
            "--dry-run",
            "--preview-content",
            "--jsonl",
            "--steam-dir",
            str(steam),
            "--data-dir",
            str(data_dir),
        ]
    )

    assert result == 0
    events = jsonl_events(capsys.readouterr().out)
    preview = next(event for event in events if event["event"] == "item-preview")
    assert preview["payload"]["game_name"] == "Local Game"
    assert preview["payload"]["roundtrip_equal"] is True
    assert preview["payload"]["rows"][0]["api_name"] == "ACH_LOCAL"
    assert not (steam / "appcache").exists()
    assert not data_dir.exists()


def test_local_import_installs_snapshot_and_records_transaction(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    steam = make_steam(tmp_path)
    payload = schema_bytes()
    source = tmp_path / "UserGameStatsSchema_123.zip"
    with zipfile.ZipFile(source, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        archive.writestr("UserGameStatsSchema_123.bin", payload)
    data_dir = tmp_path / "data"
    monkeypatch.setattr("satli.local_import_command.is_steam_running", lambda: False)

    result = main(
        [
            "local-import",
            str(source),
            "--yes",
            "--jsonl",
            "--expected-sha256",
            hashlib.sha256(payload).hexdigest(),
            "--steam-dir",
            str(steam),
            "--data-dir",
            str(data_dir),
        ]
    )

    assert result == 0
    target = steam / "appcache" / "stats" / "UserGameStatsSchema_123.bin"
    assert target.read_bytes() == payload
    state = json.loads((data_dir / "state.json").read_text(encoding="utf-8"))
    transaction = state["apps"]["123"]["transactions"][-1]
    assert transaction["variant_id"].startswith("local-")
    assert transaction["source_kind"] == "local-import"
    assert transaction["game_name"] == "Local Game"
    events = jsonl_events(capsys.readouterr().out)
    assert events[-1]["payload"]["succeeded"] == 1

    assert main(["status", "123", "--offline", "--json", "--data-dir", str(data_dir)]) == 0
    status = json.loads(capsys.readouterr().out)[0]
    assert status["game_name"] == "Local Game"
    assert status["installed_source"] == "local-import"
    assert status["installed_sha256"] == hashlib.sha256(payload).hexdigest()


def test_local_import_refuses_content_changed_after_preview(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    steam = make_steam(tmp_path)
    source = tmp_path / "UserGameStatsSchema_123.bin"
    source.write_bytes(schema_bytes())

    result = main(
        [
            "local-import",
            str(source),
            "--yes",
            "--jsonl",
            "--expected-sha256",
            "0" * 64,
            "--steam-dir",
            str(steam),
            "--data-dir",
            str(tmp_path / "data"),
        ]
    )

    assert result == 5
    events = jsonl_events(capsys.readouterr().out)
    assert "自预览后已变化" in events[-1]["payload"]["message"]
    assert not (steam / "appcache").exists()
