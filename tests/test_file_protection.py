from __future__ import annotations

import stat
from pathlib import Path

from satli.file_protection import delete_read_only_file, is_read_only, set_read_only


def test_toggles_and_verifies_read_only_attribute(tmp_path: Path) -> None:
    target = tmp_path / "UserGameStatsSchema_123.bin"
    target.write_bytes(b"schema")

    set_read_only(target, True)
    assert is_read_only(target)
    assert not target.stat().st_mode & stat.S_IWRITE

    set_read_only(target, False)
    assert not is_read_only(target)
    assert target.stat().st_mode & stat.S_IWRITE


def test_delete_read_only_file_clears_attribute_first(tmp_path: Path) -> None:
    target = tmp_path / "UserGameStatsSchema_123.bin"
    target.write_bytes(b"schema")
    set_read_only(target, True)

    delete_read_only_file(target)

    assert not target.exists()
