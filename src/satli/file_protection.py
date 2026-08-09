from __future__ import annotations

import stat
from pathlib import Path

from satli.errors import PreflightError, TransactionError


def is_read_only(path: Path) -> bool:
    """Return whether Windows currently exposes *path* as read-only."""
    target = Path(path)
    try:
        return not bool(target.stat().st_mode & stat.S_IWRITE)
    except FileNotFoundError:
        return False
    except OSError as exc:
        raise PreflightError(f"无法读取文件保护状态：{target}：{exc}") from exc


def set_read_only(path: Path, enabled: bool) -> None:
    """Toggle the Windows read-only attribute and verify the resulting state."""
    target = Path(path)
    if not target.is_file():
        raise PreflightError(f"找不到要保护的 Steam 成就文件：{target}")
    try:
        mode = target.stat().st_mode
        target.chmod(mode & ~stat.S_IWRITE if enabled else mode | stat.S_IWRITE)
    except OSError as exc:
        action = "设置" if enabled else "清除"
        raise TransactionError(f"无法{action}只读属性：{target}：{exc}") from exc
    if is_read_only(target) != enabled:
        state = "只读" if enabled else "可写"
        raise TransactionError(f"Windows 未将文件切换为预期的{state}状态：{target}")


def delete_read_only_file(path: Path) -> None:
    """Delete a file after clearing read-only, if needed."""
    target = Path(path)
    if not target.exists():
        return
    if is_read_only(target):
        set_read_only(target, False)
    target.unlink()
