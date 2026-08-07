from __future__ import annotations

import os
from dataclasses import dataclass

from satli.network import NetworkConfigurationError

REPOSITORY = "GaBoron/steam-achievement-translation-library"


@dataclass(frozen=True)
class DownloadSource:
    source_id: str
    root: str

    @property
    def catalog_url(self) -> str:
        return f"{self.root}/index.json"


SOURCES = {
    "jsdelivr": DownloadSource(
        "jsdelivr",
        f"https://cdn.jsdelivr.net/gh/{REPOSITORY}@main",
    ),
    "github": DownloadSource(
        "github",
        f"https://raw.githubusercontent.com/{REPOSITORY}/main",
    ),
    "jsdelivr-fastly": DownloadSource(
        "jsdelivr-fastly",
        f"https://fastly.jsdelivr.net/gh/{REPOSITORY}@main",
    ),
    "staticdelivr": DownloadSource(
        "staticdelivr",
        f"https://cdn.staticdelivr.com/gh/{REPOSITORY}/main",
    ),
}

DEFAULT_INDEX_SOURCE_IDS = (
    "github",
    "jsdelivr",
    "jsdelivr-fastly",
    "staticdelivr",
)
DEFAULT_FILE_SOURCE_IDS = ("jsdelivr", "jsdelivr-fastly", "github")
DEFAULT_CATALOG_URLS = tuple(SOURCES[item].catalog_url for item in DEFAULT_INDEX_SOURCE_IDS)
DEFAULT_FILE_ROOTS = tuple(SOURCES[item].root for item in DEFAULT_FILE_SOURCE_IDS)


@dataclass(frozen=True)
class DownloadSourceOrder:
    index_source_ids: tuple[str, ...] = DEFAULT_INDEX_SOURCE_IDS
    file_source_ids: tuple[str, ...] = DEFAULT_FILE_SOURCE_IDS

    @classmethod
    def from_environment(cls) -> DownloadSourceOrder:
        return cls(
            index_source_ids=_parse_order(
                os.environ.get("SATLI_INDEX_SOURCES"),
                DEFAULT_INDEX_SOURCE_IDS,
                "索引下载源",
            ),
            file_source_ids=_parse_order(
                os.environ.get("SATLI_FILE_SOURCES"),
                DEFAULT_FILE_SOURCE_IDS,
                "文件下载源",
            ),
        )

    @property
    def catalog_urls(self) -> tuple[str, ...]:
        return tuple(SOURCES[item].catalog_url for item in self.index_source_ids)

    @property
    def file_roots(self) -> tuple[str, ...]:
        return tuple(SOURCES[item].root for item in self.file_source_ids)


def _parse_order(
    raw: str | None,
    default: tuple[str, ...],
    description: str,
) -> tuple[str, ...]:
    if raw is None or not raw.strip():
        return default

    result: list[str] = []
    for value in raw.replace(",", ";").split(";"):
        source_id = value.strip().lower()
        if not source_id:
            continue
        if source_id not in SOURCES:
            raise NetworkConfigurationError(f"{description}包含未知来源：{source_id}")
        if source_id not in default:
            raise NetworkConfigurationError(f"{description}不支持来源：{source_id}")
        if source_id not in result:
            result.append(source_id)
    if not result:
        raise NetworkConfigurationError(f"{description}不能为空。")
    return tuple(result)
