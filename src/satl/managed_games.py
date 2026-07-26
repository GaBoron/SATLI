from __future__ import annotations

from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path
from typing import Any, Literal

from satl.errors import IntegrityError, PreflightError, TransactionError
from satl.schema_edit import EditHistoryStore, restore_schema, sha256_path
from satl.steam import discover_installed_games
from satl.transaction import TransactionManager


ManagedSource = Literal["installation", "local-edit"]


@dataclass(frozen=True, slots=True)
class ManagedRecord:
    app_id: str
    installed_state: str
    installed_variant_id: str | None
    installed_source: str | None
    installed_at: str | None
    installed_sha256: str | None
    game_name: str | None


@dataclass(frozen=True, slots=True)
class _TransactionCandidate:
    source: ManagedSource
    transaction: dict[str, Any]
    timestamp: datetime


class ManagedGameRegistry:
    """Present installation and local-edit histories as one managed-game stack."""

    def __init__(self, data_dir: Path) -> None:
        self.data_dir = Path(data_dir).expanduser().resolve()
        self.installations = TransactionManager(self.data_dir)
        self.edits = EditHistoryStore(self.data_dir)
        self._local_names_by_steam_dir: dict[Path, dict[str, str]] = {}

    def managed_app_ids(self) -> tuple[str, ...]:
        app_ids = set(self.installations.store.managed_app_ids())
        app_ids.update(self.edits.managed_app_ids())
        return tuple(sorted(app_ids, key=int))

    def has_active_transaction(self, app_id: str) -> bool:
        return self._active_candidate(app_id) is not None

    def record(self, app_id: str) -> ManagedRecord:
        candidate = self._active_candidate(app_id)
        active = candidate is not None
        candidate = candidate or self._latest_historical_candidate(app_id)
        if candidate is None:
            return ManagedRecord(app_id, "unmanaged", None, None, None, None, None)
        if candidate.source == "local-edit":
            transaction = candidate.transaction
            edited_hash = _optional_string(transaction.get("edited_sha256"))
            state = self._local_edit_status(transaction) if active else "restored"
            variant_id = (
                f"local-edit-{edited_hash[:12]}" if edited_hash else "local-edit"
            )
            return ManagedRecord(
                app_id=app_id,
                installed_state=state,
                installed_variant_id=variant_id,
                installed_source="local-edit",
                installed_at=_optional_string(transaction.get("edited_at")),
                installed_sha256=edited_hash,
                game_name=_optional_string(transaction.get("game_name"))
                or self._known_game_name(app_id, transaction),
            )

        transaction = candidate.transaction
        variant_id = _optional_string(transaction.get("variant_id"))
        source_kind = transaction.get("source_kind")
        if source_kind not in {"catalog", "local-import"}:
            source_kind = "local-import" if (variant_id or "").startswith("local-") else "catalog"
        return ManagedRecord(
            app_id=app_id,
            installed_state=self.installations.status(app_id) if active else "restored",
            installed_variant_id=variant_id if active else None,
            installed_source=source_kind,
            installed_at=_optional_string(transaction.get("installed_at")),
            installed_sha256=_optional_string(transaction.get("installed_sha256")),
            game_name=_optional_string(transaction.get("game_name")),
        )

    def restore_preview_source(self, app_id: str, expected_target: Path) -> Path | None:
        candidate = self._required_active_candidate(app_id)
        if candidate.source == "installation":
            return self.installations.restore_preview_source(app_id, expected_target)
        transaction = candidate.transaction
        target = Path(str(transaction.get("target") or "")).resolve()
        if target != Path(expected_target).resolve():
            raise TransactionError(f"编辑历史中的目标路径与当前 Steam 目录不一致：{target}")
        snapshot = self._edit_snapshot(app_id, transaction)
        return snapshot

    def restore(self, app_id: str, expected_target: Path, *, force: bool) -> dict[str, Any]:
        candidate = self._required_active_candidate(app_id)
        if candidate.source == "local-edit":
            return restore_schema(expected_target, app_id, self.data_dir, force=force)
        return self.installations.restore(app_id, expected_target, force=force)

    def _active_candidate(self, app_id: str) -> _TransactionCandidate | None:
        installation = self.installations.store.active_transaction(app_id)
        edit = self.edits.active(app_id)
        return self._choose_candidate(installation, edit)

    def _latest_historical_candidate(self, app_id: str) -> _TransactionCandidate | None:
        installations = self.installations.store.transactions(app_id)
        edits = self.edits.transactions(app_id)
        return self._choose_candidate(
            installations[-1] if installations else None,
            edits[-1] if edits else None,
        )

    def _choose_candidate(
        self,
        installation: dict[str, Any] | None,
        edit: dict[str, Any] | None,
    ) -> _TransactionCandidate | None:
        if installation is None:
            return self._candidate("local-edit", edit) if edit is not None else None
        if edit is None:
            return self._candidate("installation", installation)
        installation_candidate = self._candidate("installation", installation)
        edit_candidate = self._candidate("local-edit", edit)
        if installation_candidate.timestamp != edit_candidate.timestamp:
            return max(installation_candidate, edit_candidate, key=lambda item: item.timestamp)

        installed_previous = _optional_string(installation.get("previous_sha256"))
        edited_hash = _optional_string(edit.get("edited_sha256"))
        if installed_previous and installed_previous == edited_hash:
            return installation_candidate
        edit_original = _optional_string(edit.get("original_sha256"))
        installed_hash = _optional_string(installation.get("installed_sha256"))
        if edit_original and edit_original == installed_hash:
            return edit_candidate
        return edit_candidate

    @staticmethod
    def _candidate(source: ManagedSource, transaction: dict[str, Any]) -> _TransactionCandidate:
        field = "installed_at" if source == "installation" else "edited_at"
        return _TransactionCandidate(source, transaction, _parse_timestamp(transaction.get(field)))

    def _required_active_candidate(self, app_id: str) -> _TransactionCandidate:
        candidate = self._active_candidate(app_id)
        if candidate is None:
            raise TransactionError(f"{app_id} 没有可恢复的管理记录")
        return candidate

    @staticmethod
    def _local_edit_status(transaction: dict[str, Any]) -> str:
        target = Path(str(transaction.get("target") or ""))
        if not target.is_file():
            return "missing"
        try:
            actual = sha256_path(target)
        except OSError:
            return "unreadable"
        return "installed" if actual == transaction.get("edited_sha256") else "modified"

    def _edit_snapshot(self, app_id: str, transaction: dict[str, Any]) -> Path:
        snapshot_value = transaction.get("snapshot")
        if not isinstance(snapshot_value, str) or not snapshot_value:
            raise TransactionError(f"{app_id} 的编辑前备份路径缺失")
        snapshot = (self.data_dir / snapshot_value).resolve()
        try:
            snapshot.relative_to(self.data_dir)
        except ValueError as exc:
            raise TransactionError(f"{app_id} 的编辑前备份路径越界") from exc
        if not snapshot.is_file():
            raise TransactionError(f"找不到编辑前备份：{snapshot}")
        original_hash = _optional_string(transaction.get("original_sha256"))
        if not original_hash or sha256_path(snapshot) != original_hash:
            raise IntegrityError(f"编辑前备份 SHA-256 不匹配：{snapshot}")
        return snapshot

    def _known_game_name(
        self,
        app_id: str,
        edit_transaction: dict[str, Any],
    ) -> str | None:
        for transaction in reversed(self.installations.store.transactions(app_id)):
            game_name = _optional_string(transaction.get("game_name"))
            if game_name:
                return game_name
        target = Path(str(edit_transaction.get("target") or ""))
        if (
            target.parent.name.casefold() != "stats"
            or target.parent.parent.name.casefold() != "appcache"
        ):
            return None
        steam_dir = target.parent.parent.parent.resolve()
        if steam_dir not in self._local_names_by_steam_dir:
            try:
                self._local_names_by_steam_dir[steam_dir] = discover_installed_games(steam_dir)
            except (OSError, PreflightError):
                self._local_names_by_steam_dir[steam_dir] = {}
        return _optional_string(self._local_names_by_steam_dir[steam_dir].get(app_id))


def _optional_string(value: object) -> str | None:
    return value.strip() if isinstance(value, str) and value.strip() else None


def _parse_timestamp(value: object) -> datetime:
    if not isinstance(value, str):
        return datetime.min.replace(tzinfo=UTC)
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
        return parsed if parsed.tzinfo else parsed.replace(tzinfo=UTC)
    except ValueError:
        return datetime.min.replace(tzinfo=UTC)
