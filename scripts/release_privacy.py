from __future__ import annotations

import argparse
import hashlib
import os
import re
import subprocess
import sys
import zipfile
from collections.abc import Iterable, Iterator, Sequence
from pathlib import Path


PRIVATE_EMAIL_HASHES = frozenset(
    {
        "b22111fbb4773796db7ad17fdf1a50db71cb0456bc28cf04bb067e6bb492a102",
    }
)
EMAIL_PATTERN = re.compile(r"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", re.IGNORECASE)


def _email_hash(value: str) -> str:
    return hashlib.sha256(value.casefold().encode("utf-8")).hexdigest()


def _git_email(project_root: Path) -> str | None:
    result = subprocess.run(
        ["git", "config", "user.email"],
        cwd=project_root,
        capture_output=True,
        check=False,
        text=True,
    )
    value = result.stdout.strip()
    if result.returncode != 0 or not value or value.endswith("@users.noreply.github.com"):
        return None
    return value


def _sensitive_values(project_root: Path, extra_values: Iterable[str]) -> tuple[str, ...]:
    values = {
        str(project_root.resolve()),
        str(Path.home().resolve()),
        os.environ.get("USERPROFILE", ""),
        os.environ.get("GITHUB_WORKSPACE", ""),
        *extra_values,
    }
    return tuple(sorted((value for value in values if len(value) >= 4), key=len, reverse=True))


def _encoded_variants(value: str) -> tuple[bytes, ...]:
    normalized = {value, value.replace("\\", "/"), value.replace("/", "\\")}
    return tuple(
        encoded
        for item in normalized
        for encoded in (item.encode("utf-8"), item.encode("utf-16-le"))
        if encoded
    )


def _decoded_views(data: bytes) -> Iterator[str]:
    yield data.decode("utf-8", errors="ignore")
    if len(data) >= 2:
        yield data.decode("utf-16-le", errors="ignore")


def _content_findings(
    label: str,
    data: bytes,
    forbidden_values: Sequence[str],
    forbidden_email_hashes: frozenset[str],
) -> list[str]:
    findings: list[str] = []
    if any(pattern in data for value in forbidden_values for pattern in _encoded_variants(value)):
        findings.append(f"{label}: contains a local path or explicitly forbidden value")

    for view in _decoded_views(data):
        if any(_email_hash(match.group(0)) in forbidden_email_hashes for match in EMAIL_PATTERN.finditer(view)):
            findings.append(f"{label}: contains a private email address")
            break
    return findings


def _file_contents(path: Path) -> Iterator[tuple[str, bytes]]:
    yield str(path), path.read_bytes()
    if not zipfile.is_zipfile(path):
        return
    with zipfile.ZipFile(path) as archive:
        for entry in archive.infolist():
            if not entry.is_dir():
                yield f"{path}!/{entry.filename}", archive.read(entry)


def _input_files(paths: Iterable[Path]) -> Iterator[Path]:
    for path in paths:
        if path.is_dir():
            yield from (candidate for candidate in path.rglob("*") if candidate.is_file())
        elif path.is_file():
            yield path
        else:
            raise FileNotFoundError(path)


def audit_paths(
    paths: Iterable[Path],
    project_root: Path,
    *,
    forbidden_values: Iterable[str] = (),
    forbidden_emails: Iterable[str] = (),
) -> list[str]:
    values = _sensitive_values(project_root, forbidden_values)
    email_hashes = set(PRIVATE_EMAIL_HASHES)
    email_hashes.update(_email_hash(value) for value in forbidden_emails if value)
    configured_email = _git_email(project_root)
    if configured_email:
        email_hashes.add(_email_hash(configured_email))

    findings: list[str] = []
    for path in _input_files(paths):
        for label, data in _file_contents(path):
            findings.extend(_content_findings(label, data, values, frozenset(email_hashes)))
    return findings


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Reject private data in SATLI release artifacts.")
    parser.add_argument("--project-root", required=True, type=Path)
    parser.add_argument("--path", action="append", required=True, type=Path, dest="paths")
    parser.add_argument("--forbid", action="append", default=[], dest="forbidden_values")
    parser.add_argument("--forbid-email", action="append", default=[], dest="forbidden_emails")
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    findings = audit_paths(
        args.paths,
        args.project_root,
        forbidden_values=args.forbidden_values,
        forbidden_emails=args.forbidden_emails,
    )
    if findings:
        print("Release privacy audit failed:", file=sys.stderr)
        for finding in findings:
            print(f"- {finding}", file=sys.stderr)
        return 1
    print(f"Release privacy audit passed for {len(args.paths)} path(s).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
