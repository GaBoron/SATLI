from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def test_gui_build_prepares_a_complete_local_cli_runtime() -> None:
    gui_project = (ROOT / "src" / "Satl.Gui" / "Satl.Gui.csproj").read_text(
        encoding="utf-8"
    )
    runtime_script = (
        ROOT / "scripts" / "prepare-local-runtime.ps1"
    ).read_text(encoding="utf-8")

    assert 'Name="PrepareSatlLocalRuntime"' in gui_project
    assert 'AfterTargets="Build"' in gui_project
    assert "prepare-local-runtime.ps1" in gui_project
    assert 'Join-Path $OutputDirectory "_runtime"' in runtime_script
    assert 'Join-Path $RuntimeRoot "python.exe"' in runtime_script
    assert 'Join-Path $RuntimeRoot "satl.pyz"' in runtime_script
    assert "$EmbeddedPythonSha256" in runtime_script
    assert "Refusing to modify path outside project" in runtime_script
