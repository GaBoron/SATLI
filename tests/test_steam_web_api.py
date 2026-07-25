from __future__ import annotations

import json
import urllib.error

import pytest

from satl.models import OwnedGame
from satl.steam_web_api import (
    SteamWebApiClient,
    SteamWebApiError,
    merge_owned_games,
)


API_KEY = "0123456789abcdef0123456789abcdef"
STEAM_ID = "76561197960265728"


class FakeResponse:
    def __init__(self, payload: object) -> None:
        self._payload = json.dumps(payload).encode("utf-8")

    def __enter__(self) -> FakeResponse:
        return self

    def __exit__(self, *args: object) -> None:
        return None

    def read(self, limit: int) -> bytes:
        return self._payload[:limit]


def test_owned_games_request_uses_account_and_returns_names() -> None:
    captured_url = ""

    def opener(request, timeout: float):
        nonlocal captured_url
        captured_url = request.full_url
        assert timeout == 15
        return FakeResponse(
            {
                "response": {
                    "game_count": 2,
                    "games": [
                        {"appid": 456, "name": "Never Installed"},
                        {"appid": 123, "name": "Local Game"},
                    ],
                }
            }
        )

    games = SteamWebApiClient(opener=opener).get_owned_games(API_KEY, STEAM_ID)

    assert games == (
        OwnedGame("123", "Local Game"),
        OwnedGame("456", "Never Installed"),
    )
    assert f"key={API_KEY}" in captured_url
    assert f"steamid={STEAM_ID}" in captured_url
    assert "include_appinfo=true" in captured_url
    assert "include_played_free_games=true" in captured_url


def test_private_or_empty_library_is_a_valid_result() -> None:
    games = SteamWebApiClient(
        opener=lambda request, timeout: FakeResponse({"response": {}})
    ).get_owned_games(API_KEY, STEAM_ID)

    assert games == ()


def test_http_error_never_exposes_api_key() -> None:
    def reject(request, timeout: float):
        raise urllib.error.HTTPError(request.full_url, 403, "Forbidden", {}, None)

    with pytest.raises(SteamWebApiError) as caught:
        SteamWebApiClient(opener=reject).get_owned_games(API_KEY, STEAM_ID)

    assert "API Key" in str(caught.value)
    assert API_KEY not in str(caught.value)


def test_merge_owned_games_adds_source_account_and_name() -> None:
    records = {}

    merge_owned_games(
        records,
        (OwnedGame("456", "Never Installed"),),
        STEAM_ID,
    )

    assert records["456"].game_name == "Never Installed"
    assert records["456"].discovery == {"steam-web-api"}
    assert records["456"].accounts == {STEAM_ID}
