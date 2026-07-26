from __future__ import annotations

import hashlib
import re
import zipfile
from dataclasses import dataclass
from pathlib import Path

from satl.bkv import achievement_preview
from satl.errors import PreflightError, UsageError


SCHEMA_NAME_RE = re.compile(r"^UserGameStatsSchema_([1-9][0-9]{0,19})\.(bin|zip)$", re.IGNORECASE)
MAX_SCHEMA_BYTES = 64 * 1024 * 1024
MAX_ARCHIVE_BYTES = 64 * 1024 * 1024


@dataclass(frozen=True, slots=True)
class LocalImportArtifact:
    source: Path
    app_id: str
    schema_name: str
    payload: bytes
    sha256: str
    preview: dict[str, object]


def read_local_import(path: Path) -> LocalImportArtifact:
    source = Path(path).expanduser().resolve()
    match = SCHEMA_NAME_RE.fullmatch(source.name)
    if match is None:
        raise UsageError(
            "本地导入文件必须命名为 UserGameStatsSchema_<app_id>.bin 或 "
            "UserGameStatsSchema_<app_id>.zip"
        )
    if not source.is_file():
        raise PreflightError(f"未找到本地导入文件：{source}")

    app_id = match.group(1)
    schema_name = f"UserGameStatsSchema_{app_id}.bin"
    try:
        source_size = source.stat().st_size
        if source_size > MAX_ARCHIVE_BYTES:
            raise PreflightError(f"本地导入文件超过 {MAX_ARCHIVE_BYTES // (1024 * 1024)} MiB 限制")
        payload = (
            _read_schema_zip(source, schema_name)
            if match.group(2).casefold() == "zip"
            else _read_schema_bin(source)
        )
    except OSError as exc:
        raise PreflightError(f"无法读取本地导入文件：{source}：{exc}") from exc

    preview = achievement_preview(payload)
    if int(preview["achievement_count"]) <= 0:
        raise PreflightError("本地导入文件中没有可识别的 Steam 成就")
    return LocalImportArtifact(
        source=source,
        app_id=app_id,
        schema_name=schema_name,
        payload=payload,
        sha256=hashlib.sha256(payload).hexdigest(),
        preview=preview,
    )


def _read_schema_bin(source: Path) -> bytes:
    size = source.stat().st_size
    if size <= 0:
        raise PreflightError("本地导入 BIN 为空文件")
    if size > MAX_SCHEMA_BYTES:
        raise PreflightError(f"本地导入 BIN 超过 {MAX_SCHEMA_BYTES // (1024 * 1024)} MiB 限制")
    return source.read_bytes()


def _read_schema_zip(source: Path, expected_name: str) -> bytes:
    try:
        with zipfile.ZipFile(source, "r") as archive:
            members = archive.infolist()
            if len(members) != 1 or members[0].filename != expected_name:
                raise PreflightError(
                    f"本地导入 ZIP 必须只包含根目录下的 {expected_name}"
                )
            member = members[0]
            if member.is_dir() or member.flag_bits & 0x1:
                raise PreflightError("本地导入 ZIP 的 schema 不能是目录或加密文件")
            if member.file_size <= 0:
                raise PreflightError("本地导入 ZIP 中的 BIN 为空文件")
            if member.file_size > MAX_SCHEMA_BYTES:
                raise PreflightError(
                    f"本地导入 ZIP 中的 BIN 超过 {MAX_SCHEMA_BYTES // (1024 * 1024)} MiB 限制"
                )
            payload = archive.read(member)
            if archive.testzip() is not None:
                raise PreflightError("本地导入 ZIP 未通过 CRC 校验")
            return payload
    except (zipfile.BadZipFile, zipfile.LargeZipFile, NotImplementedError) as exc:
        raise PreflightError(f"本地导入 ZIP 无效：{exc}") from exc
