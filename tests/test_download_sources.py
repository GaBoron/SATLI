from __future__ import annotations

import pytest

from satl.download_sources import DownloadSourceOrder
from satl.network import NetworkConfigurationError


def test_defaults_prioritize_github_raw_for_index_only(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.delenv("SATL_INDEX_SOURCES", raising=False)
    monkeypatch.delenv("SATL_FILE_SOURCES", raising=False)
    order = DownloadSourceOrder.from_environment()

    assert order.index_source_ids == (
        "github",
        "jsdelivr",
        "jsdelivr-fastly",
        "staticdelivr",
    )
    assert order.catalog_urls[0].startswith("https://raw.githubusercontent.com/")
    assert order.file_source_ids == ("jsdelivr", "jsdelivr-fastly", "github")


def test_environment_configures_index_and_file_orders_independently(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv(
        "SATL_INDEX_SOURCES",
        "github;jsdelivr;staticdelivr;jsdelivr-fastly",
    )
    monkeypatch.setenv("SATL_FILE_SOURCES", "github,jsdelivr-fastly,jsdelivr")

    order = DownloadSourceOrder.from_environment()

    assert order.index_source_ids == (
        "github",
        "jsdelivr",
        "staticdelivr",
        "jsdelivr-fastly",
    )
    assert order.file_source_ids == ("github", "jsdelivr-fastly", "jsdelivr")
    assert order.catalog_urls[0].startswith("https://raw.githubusercontent.com/")
    assert order.file_roots[1].startswith("https://fastly.jsdelivr.net/")


def test_environment_removes_duplicate_sources(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("SATL_INDEX_SOURCES", "jsdelivr;jsdelivr;github")

    order = DownloadSourceOrder.from_environment()

    assert order.index_source_ids == ("jsdelivr", "github")


def test_environment_rejects_unknown_source(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("SATL_FILE_SOURCES", "jsdelivr;unknown")

    with pytest.raises(NetworkConfigurationError, match="未知来源"):
        DownloadSourceOrder.from_environment()


def test_environment_rejects_index_only_source_for_files(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("SATL_FILE_SOURCES", "jsdelivr;staticdelivr")

    with pytest.raises(NetworkConfigurationError, match="不支持来源"):
        DownloadSourceOrder.from_environment()
