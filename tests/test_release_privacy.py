from __future__ import annotations

import importlib.util
import zipfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
MODULE_PATH = ROOT / "scripts" / "release_privacy.py"
SPEC = importlib.util.spec_from_file_location("release_privacy", MODULE_PATH)
assert SPEC and SPEC.loader
release_privacy = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(release_privacy)


def test_audit_accepts_neutral_release_content(tmp_path: Path) -> None:
    artifact = tmp_path / "artifact.bin"
    artifact.write_bytes(b"SATLI release payload")

    assert release_privacy.audit_paths([artifact], ROOT) == []


def test_audit_rejects_local_paths_and_private_emails(tmp_path: Path) -> None:
    artifact = tmp_path / "artifact.bin"
    local_path = r"C:\Users\Example\source"
    private_email = "private@example.test"
    artifact.write_bytes(f"{local_path}\n{private_email}".encode("utf-16-le"))

    findings = release_privacy.audit_paths(
        [artifact],
        ROOT,
        forbidden_values=[local_path],
        forbidden_emails=[private_email],
    )

    assert any("local path" in finding for finding in findings)
    assert any("private email" in finding for finding in findings)


def test_audit_scans_files_inside_msix_archives(tmp_path: Path) -> None:
    package = tmp_path / "package.msix"
    local_path = r"C:\Users\Example\source"
    with zipfile.ZipFile(package, "w") as archive:
        archive.writestr("payload/config.txt", local_path)

    findings = release_privacy.audit_paths(
        [package],
        ROOT,
        forbidden_values=[local_path],
    )

    assert any("payload/config.txt" in finding for finding in findings)
