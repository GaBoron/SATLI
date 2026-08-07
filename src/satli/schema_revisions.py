from __future__ import annotations

import hashlib
import json
import os
import time
import zipfile
from contextlib import contextmanager
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path
from typing import Any, Iterator

from dulwich.objects import Blob, Commit, Tree
from dulwich.repo import Repo

from satli.bkv import achievement_preview
from satli.errors import IntegrityError, TransactionError, UsageError


REPOSITORY_NAME = "schema-revisions.git"
METADATA_VERSION = "1.0.0"
MAIN_REF = b"refs/heads/main"
AUTHOR = b"SATLI Revision Store <local@satli.invalid>"
FILE_MODE = 0o100644
DIRECTORY_MODE = 0o040000


@dataclass(frozen=True, slots=True)
class SchemaRevision:
    commit_id: str
    app_id: str
    game_name: str
    target_language: str
    action: str
    created_at: str
    schema_sha256: str
    parent_schema_sha256: str
    achievement_count: int
    changed_names: int
    changed_descriptions: int
    variant_id: str
    schema: bytes

    def record(self, *, include_schema: bool = False) -> dict[str, Any]:
        result = {
            "commit": self.commit_id,
            "short_commit": self.commit_id[:12],
            "app_id": self.app_id,
            "game_name": self.game_name,
            "target_language": self.target_language,
            "action": self.action,
            "created_at": self.created_at,
            "schema_sha256": self.schema_sha256,
            "parent_schema_sha256": self.parent_schema_sha256,
            "achievement_count": self.achievement_count,
            "changed_names": self.changed_names,
            "changed_descriptions": self.changed_descriptions,
            "variant_id": self.variant_id,
        }
        if include_schema:
            result["preview"] = achievement_preview(self.schema)
        return result


class SchemaRevisionRepository:
    def __init__(self, data_dir: Path) -> None:
        self.data_dir = Path(data_dir).expanduser().resolve()
        self.path = self.data_dir / REPOSITORY_NAME
        self.lock_path = self.data_dir / "schema-revisions.lock"

    @property
    def exists(self) -> bool:
        return self.path.is_dir()

    def record(
        self,
        app_id: str,
        schema: bytes,
        *,
        action: str,
        game_name: str = "",
        target_language: str = "",
        achievement_count: int | None = None,
        changed_names: int = 0,
        changed_descriptions: int = 0,
        variant_id: str = "",
    ) -> SchemaRevision:
        _validate_app_id(app_id)
        preview = achievement_preview(schema)
        schema_sha256 = _sha256(schema)
        count = int(achievement_count if achievement_count is not None else preview["achievement_count"])
        with self._locked():
            repo = self._open(create=True)
            head = self._head(repo)
            previous = self._revision_at(repo, head, app_id) if head is not None else None
            if previous is not None and previous.schema_sha256 == schema_sha256:
                return previous

            created_at = _utc_now()
            metadata = {
                "version": METADATA_VERSION,
                "app_id": app_id,
                "game_name": game_name.strip(),
                "target_language": target_language.strip().lower(),
                "action": action.strip(),
                "created_at": created_at,
                "schema_sha256": schema_sha256,
                "parent_schema_sha256": previous.schema_sha256 if previous else "",
                "achievement_count": count,
                "changed_names": int(changed_names),
                "changed_descriptions": int(changed_descriptions),
                "variant_id": variant_id.strip(),
            }
            root_id = self._updated_root(repo, head, app_id, schema, metadata)
            commit = Commit()
            commit.tree = root_id
            commit.parents = [head] if head is not None else []
            commit.author = AUTHOR
            commit.committer = AUTHOR
            now = int(time.time())
            commit.author_time = now
            commit.commit_time = now
            commit.author_timezone = 0
            commit.commit_timezone = 0
            commit.encoding = b"UTF-8"
            commit.message = (
                f"revision: {metadata['action']} {app_id} "
                f"{metadata['target_language'] or '-'}\n"
            ).encode("utf-8")
            repo.object_store.add_object(commit)
            if not repo.refs.set_if_equals(MAIN_REF, head, commit.id):
                raise TransactionError("修订仓库在提交期间发生并发变化，请重试")
            repo.refs.set_symbolic_ref(b"HEAD", MAIN_REF)
            return self._revision_at(repo, commit.id, app_id, required=True)

    def list(self, app_id: str) -> list[SchemaRevision]:
        _validate_app_id(app_id)
        if not self.exists:
            return []
        repo = self._open(create=False)
        head = self._head(repo)
        if head is None:
            return []
        path = f"games/{app_id}".encode()
        revisions: list[SchemaRevision] = []
        try:
            walker = repo.get_walker(include=[head], paths=[path])
            for entry in walker:
                revision = self._revision_at(repo, entry.commit.id, app_id)
                if revision is not None:
                    revisions.append(revision)
        except Exception as exc:
            raise TransactionError(f"无法读取修订仓库历史：{exc}") from exc
        return revisions

    def get(self, app_id: str, revision: str) -> SchemaRevision:
        _validate_app_id(app_id)
        repo = self._open(create=False)
        commit_id = self._resolve_revision(repo, app_id, revision)
        return self._revision_at(repo, commit_id, app_id, required=True)

    def export(
        self,
        app_id: str,
        revision: str,
        output_path: Path,
        output_format: str,
    ) -> SchemaRevision:
        item = self.get(app_id, revision)
        output = Path(output_path).expanduser().resolve()
        if output_format == "bin":
            if output.suffix.lower() != ".bin":
                raise UsageError("BIN 导出路径必须使用 .bin 扩展名")
            _atomic_write(output, item.schema)
        elif output_format == "zip":
            if output.suffix.lower() != ".zip":
                raise UsageError("ZIP 导出路径必须使用 .zip 扩展名")
            _atomic_zip(output, f"UserGameStatsSchema_{app_id}.bin", item.schema)
        else:
            raise UsageError(f"不支持的导出格式：{output_format}")
        return item

    def verify(self, app_id: str | None = None) -> dict[str, Any]:
        if app_id is not None:
            revisions = self.list(app_id)
        else:
            revisions = self._all_revisions()
        for revision in revisions:
            actual = _sha256(revision.schema)
            if actual != revision.schema_sha256:
                raise IntegrityError(
                    f"修订 {revision.commit_id[:12]} 的 schema SHA-256 不匹配"
                )
            achievement_preview(revision.schema)
        return {"verified": len(revisions), "repository": str(self.path)}

    def _all_revisions(self) -> list[SchemaRevision]:
        if not self.exists:
            return []
        repo = self._open(create=False)
        head = self._head(repo)
        if head is None:
            return []
        root = repo[repo[head].tree]
        games_entry = _tree_entry(root, b"games")
        if games_entry is None:
            return []
        games = repo[games_entry[1]]
        app_ids = [name.decode("ascii") for name, _mode, _sha in games.iteritems()]
        return [revision for app_id in app_ids for revision in self.list(app_id)]

    def _open(self, *, create: bool) -> Repo:
        try:
            if not self.exists:
                if not create:
                    raise TransactionError("尚未创建修订仓库")
                self.data_dir.mkdir(parents=True, exist_ok=True)
                return Repo.init_bare(self.path, mkdir=True, default_branch=MAIN_REF)
            return Repo(self.path)
        except TransactionError:
            raise
        except Exception as exc:
            raise TransactionError(f"无法打开修订仓库 {self.path}：{exc}") from exc

    @staticmethod
    def _head(repo: Repo) -> bytes | None:
        try:
            return repo.refs[MAIN_REF]
        except KeyError:
            return None

    def _updated_root(
        self,
        repo: Repo,
        head: bytes | None,
        app_id: str,
        schema: bytes,
        metadata: dict[str, Any],
    ) -> bytes:
        old_root = repo[repo[head].tree] if head is not None else None
        old_games_entry = _tree_entry(old_root, b"games") if old_root is not None else None
        old_games = repo[old_games_entry[1]] if old_games_entry is not None else None

        schema_blob = Blob.from_string(schema)
        metadata_blob = Blob.from_string(
            (json.dumps(metadata, ensure_ascii=False, indent=2, sort_keys=True) + "\n").encode()
        )
        repo.object_store.add_object(schema_blob)
        repo.object_store.add_object(metadata_blob)

        app_tree = Tree()
        app_tree.add(b"schema.bin", FILE_MODE, schema_blob.id)
        app_tree.add(b"metadata.json", FILE_MODE, metadata_blob.id)
        repo.object_store.add_object(app_tree)

        games = _copy_tree(old_games)
        games.add(app_id.encode("ascii"), DIRECTORY_MODE, app_tree.id)
        repo.object_store.add_object(games)

        root = _copy_tree(old_root)
        root.add(b"games", DIRECTORY_MODE, games.id)
        repo.object_store.add_object(root)
        return root.id

    def _revision_at(
        self,
        repo: Repo,
        commit_id: bytes | None,
        app_id: str,
        *,
        required: bool = False,
    ) -> SchemaRevision | None:
        if commit_id is None:
            return None
        try:
            commit = repo[commit_id]
            root = repo[commit.tree]
            games_entry = _tree_entry(root, b"games")
            if games_entry is None:
                raise KeyError("games")
            games = repo[games_entry[1]]
            app_entry = _tree_entry(games, app_id.encode("ascii"))
            if app_entry is None:
                raise KeyError(app_id)
            app_tree = repo[app_entry[1]]
            schema_entry = _tree_entry(app_tree, b"schema.bin")
            metadata_entry = _tree_entry(app_tree, b"metadata.json")
            if schema_entry is None or metadata_entry is None:
                raise KeyError("schema revision files")
            schema = bytes(repo[schema_entry[1]].data)
            metadata = json.loads(bytes(repo[metadata_entry[1]].data).decode("utf-8"))
            if metadata.get("version") != METADATA_VERSION:
                raise IntegrityError(
                    f"不支持的修订元数据版本：{metadata.get('version')}"
                )
            return SchemaRevision(
                commit_id=commit.id.decode("ascii"),
                app_id=str(metadata["app_id"]),
                game_name=str(metadata.get("game_name") or ""),
                target_language=str(metadata.get("target_language") or ""),
                action=str(metadata.get("action") or "unknown"),
                created_at=str(metadata.get("created_at") or ""),
                schema_sha256=str(metadata["schema_sha256"]),
                parent_schema_sha256=str(metadata.get("parent_schema_sha256") or ""),
                achievement_count=int(metadata.get("achievement_count") or 0),
                changed_names=int(metadata.get("changed_names") or 0),
                changed_descriptions=int(metadata.get("changed_descriptions") or 0),
                variant_id=str(metadata.get("variant_id") or ""),
                schema=schema,
            )
        except KeyError:
            if required:
                raise TransactionError(
                    f"修订 {commit_id.decode('ascii')[:12]} 中找不到 App ID {app_id}"
                )
            return None
        except (IntegrityError, TransactionError):
            raise
        except Exception as exc:
            raise TransactionError(
                f"无法读取修订 {commit_id.decode('ascii')[:12]}：{exc}"
            ) from exc

    def _resolve_revision(self, repo: Repo, app_id: str, value: str) -> bytes:
        normalized = value.strip().lower()
        if len(normalized) < 7 or any(character not in "0123456789abcdef" for character in normalized):
            raise UsageError("修订 ID 必须是至少 7 位十六进制 Git commit ID")
        matches = [
            revision.commit_id
            for revision in self.list(app_id)
            if revision.commit_id.startswith(normalized)
        ]
        if len(matches) != 1:
            raise UsageError(
                "找不到修订 ID" if not matches else "修订 ID 前缀不唯一，请提供更多字符"
            )
        return matches[0].encode("ascii")

    @contextmanager
    def _locked(self, timeout: float = 10.0) -> Iterator[None]:
        self.data_dir.mkdir(parents=True, exist_ok=True)
        with self.lock_path.open("a+b") as handle:
            handle.seek(0)
            if handle.read(1) == b"":
                handle.write(b"0")
                handle.flush()
            deadline = time.monotonic() + timeout
            while True:
                try:
                    _lock_file(handle)
                    break
                except OSError:
                    if time.monotonic() >= deadline:
                        raise TransactionError("等待修订仓库写锁超时")
                    time.sleep(0.05)
            try:
                yield
            finally:
                _unlock_file(handle)


def _copy_tree(source: Tree | None) -> Tree:
    target = Tree()
    if source is not None:
        for name, mode, sha in source.iteritems():
            target.add(name, mode, sha)
    return target


def _tree_entry(tree: Tree | None, name: bytes) -> tuple[int, bytes] | None:
    if tree is None:
        return None
    try:
        return tree[name]
    except KeyError:
        return None


def _validate_app_id(app_id: str) -> None:
    if not app_id.isascii() or not app_id.isdigit() or not app_id or len(app_id) > 20:
        raise UsageError(f"无效 Steam app ID：{app_id}")


def _sha256(payload: bytes) -> str:
    return hashlib.sha256(payload).hexdigest()


def _utc_now() -> str:
    return datetime.now(UTC).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def _atomic_write(path: Path, payload: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(f".{path.name}.{os.urandom(8).hex()}.tmp")
    try:
        with temporary.open("xb") as handle:
            handle.write(payload)
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def _atomic_zip(path: Path, member: str, payload: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(f".{path.name}.{os.urandom(8).hex()}.tmp")
    try:
        with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            archive.writestr(member, payload)
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def _lock_file(handle: Any) -> None:
    handle.seek(0)
    if os.name == "nt":
        import msvcrt

        msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
    else:
        import fcntl

        fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)


def _unlock_file(handle: Any) -> None:
    handle.seek(0)
    if os.name == "nt":
        import msvcrt

        msvcrt.locking(handle.fileno(), msvcrt.LK_UNLCK, 1)
    else:
        import fcntl

        fcntl.flock(handle.fileno(), fcntl.LOCK_UN)
