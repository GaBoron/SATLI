from __future__ import annotations

import os
from pathlib import Path


def migrate_default_data_dir(local_app_data: Path) -> Path:
    current = local_app_data / "SATLI"
    legacy = local_app_data / "SteamAchievementTranslationInstaller"
    if not legacy.is_dir():
        return current
    if current.exists():
        if any(current.iterdir()):
            raise FileExistsError(f"无法迁移旧数据目录：目标目录已包含文件：{current}")
        current.rmdir()
    legacy.rename(current)
    updates = current / "updates"
    if updates.is_dir():
        for pattern in ("SATLInstaller-Setup-v*.exe", "SATLInstaller-Setup-v*.exe.part"):
            for path in updates.glob(pattern):
                path.unlink()
    return current


def local_app_data_root() -> Path:
    base = os.environ.get("LOCALAPPDATA")
    return (
        Path(base)
        if base
        else Path.home() / "AppData" / "Local"
    )


def default_data_dir() -> Path:
    return local_app_data_root() / "SATLI"
