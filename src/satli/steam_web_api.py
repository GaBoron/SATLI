from __future__ import annotations

import json
import urllib.error
import urllib.parse
import urllib.request
from typing import Any, Callable

from satli.models import DiscoveryRecord, OwnedGame
from satli.network import NetworkTransport, describe_network_error


GET_OWNED_GAMES_URL = (
    "https://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/"
)
MAX_RESPONSE_BYTES = 32 * 1024 * 1024


class SteamWebApiError(RuntimeError):
    """A user-facing Steam Web API error that never contains the API key."""


class SteamWebApiClient:
    def __init__(
        self,
        opener: Callable[..., Any] | None = None,
    ) -> None:
        self._opener = opener or NetworkTransport().open

    def get_owned_games(self, api_key: str, steam_id: str) -> tuple[OwnedGame, ...]:
        normalized_key = _validate_api_key(api_key)
        normalized_steam_id = _validate_steam_id(steam_id)
        query = urllib.parse.urlencode(
            {
                "key": normalized_key,
                "steamid": normalized_steam_id,
                "include_appinfo": "true",
                "include_played_free_games": "true",
                "format": "json",
            }
        )
        request = urllib.request.Request(
            f"{GET_OWNED_GAMES_URL}?{query}",
            headers={"User-Agent": "satli/steam-library"},
        )
        try:
            with self._opener(request, timeout=15) as response:
                payload = response.read(MAX_RESPONSE_BYTES + 1)
        except urllib.error.HTTPError as error:
            if error.code in {401, 403}:
                raise SteamWebApiError(
                    "Steam Web API 拒绝了凭据，请检查 API Key 和 SteamID64。"
                ) from None
            if error.code == 429:
                raise SteamWebApiError("Steam Web API 请求过于频繁，请稍后重试。") from None
            raise SteamWebApiError(
                f"Steam Web API 返回 HTTP {error.code}，请稍后重试。"
            ) from None
        except (OSError, urllib.error.URLError, TimeoutError) as error:
            raise SteamWebApiError(describe_network_error(error)) from None

        if len(payload) > MAX_RESPONSE_BYTES:
            raise SteamWebApiError("Steam Web API 返回的数据过大，已停止读取。")
        try:
            raw = json.loads(payload.decode("utf-8"))
        except (UnicodeError, json.JSONDecodeError):
            raise SteamWebApiError("Steam Web API 返回了无效数据。") from None
        return _parse_owned_games(raw)


def _parse_owned_games(raw: object) -> tuple[OwnedGame, ...]:
    if not isinstance(raw, dict):
        raise SteamWebApiError("Steam Web API 返回了无效数据。")
    response = raw.get("response")
    if not isinstance(response, dict):
        raise SteamWebApiError("Steam Web API 响应缺少游戏库信息。")
    games = response.get("games", [])
    if not isinstance(games, list):
        raise SteamWebApiError("Steam Web API 返回的游戏列表无效。")

    owned: dict[str, OwnedGame] = {}
    for item in games:
        if not isinstance(item, dict):
            continue
        raw_app_id = item.get("appid")
        app_id = str(raw_app_id) if isinstance(raw_app_id, int) else ""
        if not app_id.isdigit():
            continue
        name = item.get("name")
        owned[app_id] = OwnedGame(
            app_id=app_id,
            name=name.strip() if isinstance(name, str) else "",
        )
    return tuple(owned[app_id] for app_id in sorted(owned, key=int))


def _validate_api_key(value: str) -> str:
    normalized = value.strip()
    if len(normalized) != 32 or any(character not in "0123456789abcdefABCDEF" for character in normalized):
        raise SteamWebApiError("Steam Web API Key 应为 32 位十六进制字符。")
    return normalized


def _validate_steam_id(value: str) -> str:
    normalized = value.strip()
    if not normalized.isdigit() or len(normalized) != 17:
        raise SteamWebApiError("SteamID64 应为 17 位数字。")
    return normalized


def merge_owned_games(
    records: dict[str, DiscoveryRecord],
    games: tuple[OwnedGame, ...],
    steam_id: str,
) -> None:
    for game in games:
        record = records.setdefault(game.app_id, DiscoveryRecord(game.app_id, game.name))
        if not record.game_name and game.name:
            record.game_name = game.name
        record.discovery.add("steam-web-api")
        record.accounts.add(steam_id)
