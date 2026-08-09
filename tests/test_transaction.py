from __future__ import annotations

import hashlib
import os
import stat
from pathlib import Path

import pytest

from satli import file_replacement
from satli.errors import TransactionError
from satli.models import SchemaVariant
from satli.state import StateStore
from satli.transaction import TransactionManager


def variant_for(app_id: str, payload: bytes, variant_id: str = "default") -> SchemaVariant:
    primary = variant_id == "default"
    folder = "" if primary else f"{variant_id}/"
    return SchemaVariant(
        variant_id=variant_id,
        primary=primary,
        schema_file=f"files/{app_id}/{folder}UserGameStatsSchema_{app_id}.bin",
        sha256=hashlib.sha256(payload).hexdigest(),
        file_size_bytes=len(payload),
    )


def source_file(root: Path, name: str, payload: bytes) -> Path:
    path = root / name
    path.write_bytes(payload)
    return path


def test_install_and_restore_existing_original(tmp_path: Path) -> None:
    data = tmp_path / "data"
    target = tmp_path / "Steam" / "appcache" / "stats" / "UserGameStatsSchema_123.bin"
    target.parent.mkdir(parents=True)
    target.write_bytes(b"original")
    translated = b"translated"
    source = source_file(tmp_path, "translated.bin", translated)
    manager = TransactionManager(data)

    transaction = manager.install("123", target, source, variant_for("123", translated))
    assert target.read_bytes() == translated
    assert transaction["previous_exists"] is True
    assert (data / transaction["snapshot"]).read_bytes() == b"original"
    assert manager.status("123") == "installed"
    assert manager.installed_variant_id("123") == "default"
    assert manager.installation_details("123")["installed_source"] == "catalog"
    assert manager.restore_preview_source("123", target).read_bytes() == b"original"

    manager.restore("123", target)
    assert target.read_bytes() == b"original"
    assert manager.status("123") == "restored"
    assert manager.installed_variant_id("123") is None


def test_install_clears_read_only_target_before_windows_replace(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    target = tmp_path / "Steam" / "appcache" / "stats" / "UserGameStatsSchema_123.bin"
    target.parent.mkdir(parents=True)
    target.write_bytes(b"original")
    target.chmod(stat.S_IREAD)
    source = source_file(tmp_path, "translated.bin", b"translated")
    real_replace = os.replace

    def windows_replace(stage: Path, destination: Path) -> None:
        if Path(destination) == target and not target.stat().st_mode & stat.S_IWRITE:
            raise PermissionError(13, "Access is denied", str(target))
        real_replace(stage, destination)

    monkeypatch.setattr(file_replacement.os, "replace", windows_replace)

    TransactionManager(tmp_path / "data").install(
        "123", target, source, variant_for("123", b"translated")
    )

    assert target.read_bytes() == b"translated"
    assert not target.stat().st_mode & stat.S_IWRITE


def test_install_reports_replace_stage_and_permission_diagnosis(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    target = tmp_path / "Steam" / "appcache" / "stats" / "UserGameStatsSchema_123.bin"
    target.parent.mkdir(parents=True)
    target.write_bytes(b"original")
    source = source_file(tmp_path, "translated.bin", b"translated")
    real_replace = os.replace

    def denied_replace(stage: Path, destination: Path) -> None:
        if Path(destination) == target:
            raise PermissionError(13, "Access is denied", str(target))
        real_replace(stage, destination)

    monkeypatch.setattr(file_replacement.os, "replace", denied_replace)

    with pytest.raises(TransactionError, match=r"阶段=replace.*ACL.*安全软件"):
        TransactionManager(tmp_path / "data").install(
            "123", target, source, variant_for("123", b"translated")
        )

    assert target.read_bytes() == b"original"


def test_restore_deletes_file_when_no_original_existed(tmp_path: Path) -> None:
    target = tmp_path / "Steam" / "appcache" / "stats" / "UserGameStatsSchema_123.bin"
    payload = b"translated"
    source = source_file(tmp_path, "translated.bin", payload)
    manager = TransactionManager(tmp_path / "data")
    manager.install("123", target, source, variant_for("123", payload))
    assert target.exists()
    assert manager.restore_preview_source("123", target) is None
    manager.restore("123", target)
    assert not target.exists()


def test_repeated_installs_preserve_history_and_restore_lifo(tmp_path: Path) -> None:
    target = tmp_path / "Steam" / "appcache" / "stats" / "UserGameStatsSchema_123.bin"
    target.parent.mkdir(parents=True)
    target.write_bytes(b"original")
    manager = TransactionManager(tmp_path / "data")
    first = source_file(tmp_path, "first.bin", b"first")
    second = source_file(tmp_path, "second.bin", b"second")
    manager.install("123", target, first, variant_for("123", b"first"))
    manager.install("123", target, second, variant_for("123", b"second", "beta"))
    assert len(manager.store.transactions("123")) == 2
    assert manager.installed_variant_id("123") == "beta"

    manager.restore("123", target)
    assert target.read_bytes() == b"first"
    assert manager.status("123") == "installed"
    assert manager.installed_variant_id("123") == "default"
    manager.restore("123", target)
    assert target.read_bytes() == b"original"
    assert manager.status("123") == "restored"


def test_modified_target_refused_then_force_archives_current(tmp_path: Path) -> None:
    target = tmp_path / "Steam" / "appcache" / "stats" / "UserGameStatsSchema_123.bin"
    target.parent.mkdir(parents=True)
    target.write_bytes(b"original")
    manager = TransactionManager(tmp_path / "data")
    source = source_file(tmp_path, "translated.bin", b"translated")
    manager.install("123", target, source, variant_for("123", b"translated"))
    target.write_bytes(b"steam-regenerated")

    with pytest.raises(TransactionError, match="--force"):
        manager.restore("123", target)
    result = manager.restore("123", target, force=True)
    assert target.read_bytes() == b"original"
    assert result["forced_archive"] is not None
    assert (manager.data_dir / result["forced_archive"]).read_bytes() == b"steam-regenerated"


def test_state_failure_rolls_target_back(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    target = tmp_path / "Steam" / "appcache" / "stats" / "UserGameStatsSchema_123.bin"
    target.parent.mkdir(parents=True)
    target.write_bytes(b"original")
    store = StateStore(tmp_path / "data")
    manager = TransactionManager(tmp_path / "data", store)
    source = source_file(tmp_path, "translated.bin", b"translated")

    def fail(*_args, **_kwargs):
        raise TransactionError("simulated state failure")

    monkeypatch.setattr(store, "add_transaction", fail)
    with pytest.raises(TransactionError, match="已回滚"):
        manager.install("123", target, source, variant_for("123", b"translated"))
    assert target.read_bytes() == b"original"
    assert not store.path.exists()


def test_dry_run_never_writes_target_or_state(tmp_path: Path) -> None:
    target = tmp_path / "missing" / "UserGameStatsSchema_123.bin"
    source = source_file(tmp_path, "translated.bin", b"translated")
    manager = TransactionManager(tmp_path / "data")
    result = manager.install(
        "123",
        target,
        source,
        variant_for("123", b"translated"),
        dry_run=True,
    )
    assert result["action"] == "would-install"
    assert not target.parent.exists()
    assert not manager.store.path.exists()


def test_installation_details_preserve_local_source_and_infer_legacy_records(tmp_path: Path) -> None:
    target = tmp_path / "Steam" / "appcache" / "stats" / "UserGameStatsSchema_123.bin"
    payload = b"translated"
    source = source_file(tmp_path, "translated.bin", payload)
    manager = TransactionManager(tmp_path / "data")
    transaction = manager.install(
        "123",
        target,
        source,
        variant_for("123", payload, "local-abcdef123456"),
        source_kind="local-import",
        game_name="Local Game",
    )

    details = manager.installation_details("123")
    assert details["installed_source"] == "local-import"
    assert details["game_name"] == "Local Game"
    assert details["installed_at"] == transaction["installed_at"]
    assert details["installed_sha256"] == transaction["installed_sha256"]

    state = manager.store.load()
    del state["apps"]["123"]["transactions"][-1]["source_kind"]
    manager.store.save(state)
    assert manager.installation_details("123")["installed_source"] == "local-import"
