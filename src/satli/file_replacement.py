from __future__ import annotations

import os
import stat
from pathlib import Path


class FileReplacementError(OSError):
    """Describe the exact filesystem stage that prevented an atomic replacement."""


def replace_staged_file(stage: Path, target: Path) -> None:
    """Replace *target* with a verified staged file, handling Windows read-only files."""
    stage = Path(stage)
    target = Path(target)
    target_mode: int | None = None
    target_was_read_only = False
    try:
        target_mode = target.stat().st_mode
        target_was_read_only = not bool(target_mode & stat.S_IWRITE)
    except FileNotFoundError:
        pass
    except OSError as exc:
        raise FileReplacementError(
            f"读取目标文件属性失败（阶段=inspect-target）：{target}：{_system_error(exc)}"
        ) from exc

    if target_was_read_only and target_mode is not None:
        try:
            target.chmod(target_mode | stat.S_IWRITE)
        except OSError as exc:
            raise FileReplacementError(
                f"无法清除目标文件的只读属性（阶段=clear-read-only）：{target}："
                f"{_system_error(exc)}"
            ) from exc

    stage_mode: int | None = None
    if target_was_read_only:
        try:
            stage_mode = stage.stat().st_mode
            stage.chmod(stage_mode & ~stat.S_IWRITE)
        except OSError as exc:
            if target_mode is not None and target.exists():
                target.chmod(target_mode)
            raise FileReplacementError(
                f"无法让暂存文件继承只读属性（阶段=prepare-read-only）：{stage}："
                f"{_system_error(exc)}"
            ) from exc

    try:
        os.replace(stage, target)
    except OSError as exc:
        restored_read_only = False
        if stage_mode is not None and stage.exists():
            try:
                stage.chmod(stage_mode)
            except OSError:
                pass
        if target_was_read_only and target_mode is not None and target.exists():
            try:
                target.chmod(target_mode)
                restored_read_only = True
            except OSError:
                pass
        raise FileReplacementError(
            _replacement_failure_message(
                stage,
                target,
                exc,
                target_was_read_only=target_was_read_only,
                restored_read_only=restored_read_only,
            )
        ) from exc



def _replacement_failure_message(
    stage: Path,
    target: Path,
    error: OSError,
    *,
    target_was_read_only: bool,
    restored_read_only: bool,
) -> str:
    directory_writable = os.access(target.parent, os.W_OK)
    facts = (
        f"临时文件已创建并完成写入；目标原先只读={'是（已清除）' if target_was_read_only else '否'}；"
        f"目标目录可写={'是' if directory_writable else '否'}"
    )
    if restored_read_only:
        facts += "；替换失败后已恢复原只读属性"
    winerror = getattr(error, "winerror", None)
    if winerror == 32:
        diagnosis = "目标文件正被其他进程占用，请完全退出 Steam 后重试。"
    elif winerror == 5 or isinstance(error, PermissionError):
        diagnosis = (
            "Windows 仍拒绝替换目标；请检查目标文件或目录 ACL，"
            "以及 Windows 安全中心或第三方安全软件的拦截记录。"
        )
    else:
        diagnosis = "操作系统未允许完成目标文件替换。"
    return (
        f"替换目标文件失败（阶段=replace）：{stage} -> {target}：{_system_error(error)}；"
        f"{facts}。{diagnosis}"
    )


def _system_error(error: OSError) -> str:
    winerror = getattr(error, "winerror", None)
    prefix = f"WinError {winerror}" if winerror is not None else type(error).__name__
    return f"{prefix}: {error}"
