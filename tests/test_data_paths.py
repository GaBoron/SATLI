from pathlib import Path

import pytest

from satli.data_paths import migrate_default_data_dir


def test_default_data_directory_moves_legacy_content(tmp_path: Path) -> None:
    legacy = tmp_path / "SteamAchievementTranslationInstaller"
    legacy.mkdir()
    (legacy / "gui-settings.json").write_text("{}", encoding="utf-8")
    updates = legacy / "updates"
    updates.mkdir()
    (updates / "SATLInstaller-Setup-v0.12.0.exe").write_text("stale", encoding="utf-8")

    current = migrate_default_data_dir(tmp_path)

    assert current == tmp_path / "SATLI"
    assert (current / "gui-settings.json").read_text(encoding="utf-8") == "{}"
    assert list((current / "updates").iterdir()) == []
    assert not legacy.exists()


def test_default_data_directory_rejects_conflicting_current_directory(tmp_path: Path) -> None:
    current = tmp_path / "SATLI"
    current.mkdir()
    (current / "current.txt").write_text("current", encoding="utf-8")
    legacy = tmp_path / "SteamAchievementTranslationInstaller"
    legacy.mkdir()
    (legacy / "legacy.txt").write_text("legacy", encoding="utf-8")

    with pytest.raises(FileExistsError, match="目标目录已包含文件"):
        migrate_default_data_dir(tmp_path)

    assert (current / "current.txt").is_file()
    assert (legacy / "legacy.txt").is_file()
