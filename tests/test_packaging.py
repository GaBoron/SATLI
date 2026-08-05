from __future__ import annotations

import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def test_gui_uses_on_demand_elevation_while_installer_stays_administrative() -> None:
    manifest = (ROOT / "src" / "Satl.Gui" / "app.manifest").read_text(encoding="utf-8")
    installer = (ROOT / "installer" / "SATLInstaller.iss").read_text(encoding="utf-8")
    cli_service = (
        ROOT / "src" / "Satl.Gui" / "Services" / "SatlCliService.cs"
    ).read_text(encoding="utf-8")
    elevated_runner = (
        ROOT / "src" / "Satl.Gui" / "Services" / "ElevatedCliRunner.cs"
    ).read_text(encoding="utf-8")

    assert 'requestedExecutionLevel level="asInvoker"' in manifest
    assert 'requestedExecutionLevel level="requireAdministrator"' not in manifest
    assert "CliElevationPolicy.RequiresElevation" in cli_service
    assert 'Verb = "runas"' in elevated_runner
    assert "PipeSecurity" in elevated_runner
    assert "WellKnownSidType.BuiltinAdministratorsSid" in elevated_runner
    assert "PrivilegesRequired=admin" in installer
    assert "UsedUserAreasWarning=no" in installer
    assert "DefaultDirName={autopf}" in installer
    assert "runasoriginaluser" in installer
    assert "runascurrentuser" not in installer


def test_installed_app_name_does_not_include_the_version() -> None:
    installer = (ROOT / "installer" / "SATLInstaller.iss").read_text(encoding="utf-8")

    assert '#define MyAppName "Steam 成就翻译管理器"' in installer
    assert "UninstallDisplayName={#MyAppName}" in installer


def test_installer_removes_per_user_and_machine_registry_entries_on_uninstall() -> None:
    installer = (ROOT / "installer" / "SATLInstaller.iss").read_text(encoding="utf-8")
    uninstall_key = (
        r"Software\Microsoft\Windows\CurrentVersion\Uninstall"
        r"\{{8E4CF3D1-13E7-4FF7-A979-CE07F27F020A}_is1"
    )

    assert (
        f'Root: HKCU; Subkey: "{uninstall_key}"; '
        "Flags: deletekey uninsdeletekey dontcreatekey"
    ) in installer
    assert (
        f'Root: HKLM; Subkey: "{uninstall_key}"; '
        "Flags: uninsdeletekey dontcreatekey"
    ) in installer


def test_settings_page_owns_log_display_settings() -> None:
    settings_page = (ROOT / "src" / "Satl.Gui" / "Pages" / "SettingsPage.xaml").read_text(
        encoding="utf-8"
    )
    logs_page = (ROOT / "src" / "Satl.Gui" / "Pages" / "LogsPage.xaml").read_text(
        encoding="utf-8"
    )

    assert "LogWordWrapSwitch" in settings_page
    assert "OpenLogs_Click" in settings_page
    assert "WordWrapButton" not in logs_page
    assert 'Label="打开目录"' not in logs_page


def test_release_projects_keep_runtime_payloads_small() -> None:
    gui_project = (ROOT / "src" / "Satl.Gui" / "Satl.Gui.csproj").read_text(encoding="utf-8")

    assert 'Include="Microsoft.WindowsAppSDK.WinUI"' in gui_project
    assert 'Include="Microsoft.WindowsAppSDK"' not in gui_project
    assert "<PublishTrimmed>False</PublishTrimmed>" in gui_project
    assert "<TrimMode>" not in gui_project
    assert "<Optimize>True</Optimize>" in gui_project
    assert "<EnableMsixTooling>true</EnableMsixTooling>" in gui_project
    assert "<PublishSingleFile>True</PublishSingleFile>" in gui_project
    assert "<IncludeAllContentForSelfExtract>True</IncludeAllContentForSelfExtract>" in gui_project
    assert "<IncludeNativeLibrariesForSelfExtract>True</IncludeNativeLibrariesForSelfExtract>" in (
        gui_project
    )
    assert "<EnableCompressionInSingleFile>True</EnableCompressionInSingleFile>" in gui_project

def test_release_build_has_size_guard_and_cleans_staging_directories() -> None:
    build_script = (ROOT / "scripts" / "build.ps1").read_text(encoding="utf-8")

    assert "$MaximumPackageSizeBytes = 140MB" in build_script
    assert "$PackageSizeBytes -gt $MaximumPackageSizeBytes" in build_script
    assert '$PackageRuntimeRoot = Join-Path $PackageRoot "_runtime"' in build_script
    assert "Installer payload root must contain only SATLInstaller.exe" in build_script
    assert "Installer payload root contains scattered runtime files" in build_script
    assert "WinUI single-file publish produced unexpected loose files" in build_script
    assert "PortableArchive" not in build_script
    assert "Compress-Archive" not in build_script
    assert "CliLauncherProject" not in build_script
    assert "CliBuildRoot" not in build_script
    assert "Uncompressed release payload:" in build_script
    assert "Remove-Item -LiteralPath $Path -Recurse -Force" in build_script


def test_store_msix_is_a_full_trust_desktop_package() -> None:
    manifest = (
        ROOT / "store" / "Package.appxmanifest.template"
    ).read_text(encoding="utf-8")
    build_script = (ROOT / "scripts" / "build.ps1").read_text(encoding="utf-8")
    package_module = (
        ROOT / "scripts" / "StoreMsixPackage.psm1"
    ).read_text(encoding="utf-8")
    identity_example = json.loads(
        (ROOT / "store" / "identity.example.json").read_text(encoding="utf-8")
    )

    assert 'EntryPoint="Windows.FullTrustApplication"' in manifest
    assert '<rescap:Capability Name="runFullTrust" />' in manifest
    assert '<Capability Name="internetClient" />' in manifest
    assert 'ProcessorArchitecture="x64"' in manifest
    assert "{{PACKAGE_IDENTITY_NAME}}" in manifest
    assert "{{PACKAGE_PUBLISHER}}" in manifest
    assert "{{PACKAGE_DISPLAY_NAME}}" in manifest
    assert '<Resource Language="zh-CN" />' in manifest
    assert '<Resource Language="en-US" />' not in manifest
    assert manifest.count("<Resource Language=") == 1
    assert '[ValidateSet("All", "Installer", "StoreMsix")]' in build_script
    assert '[string] $Target = "All"' in build_script
    assert identity_example == {
        "packageIdentityName": "YOUR_PARTNER_CENTER_PACKAGE_IDENTITY_NAME",
        "packagePublisher": "CN=YOUR_PARTNER_CENTER_PUBLISHER_ID",
        "packageDisplayName": "YOUR_RESERVED_STORE_DISPLAY_NAME",
        "publisherDisplayName": "YOUR_PARTNER_CENTER_PUBLISHER_DISPLAY_NAME",
    }
    assert 'Get-Content -LiteralPath $StoreIdentityPath' in build_script
    assert "Store identity is local-only" in build_script
    assert "identity.example.json" in build_script
    assert "New-SatlStoreMsix" in build_script
    assert 'return "$($parsed.Major).$($parsed.Minor).$($parsed.Build).0"' in package_module
    assert "makeappx.exe" in package_module


def test_release_metadata_uses_project_identity_and_runs_privacy_audits() -> None:
    installer = (ROOT / "installer" / "SATLInstaller.iss").read_text(encoding="utf-8")
    project = (ROOT / "pyproject.toml").read_text(encoding="utf-8")
    license_text = (ROOT / "LICENSE").read_text(encoding="utf-8")
    gui_project = (ROOT / "src" / "Satl.Gui" / "Satl.Gui.csproj").read_text(
        encoding="utf-8"
    )
    build_script = (ROOT / "scripts" / "build.ps1").read_text(encoding="utf-8")

    assert '#define MyAppPublisher "GaBoron"' in installer
    assert 'authors = [{ name = "GaBoron" }]' in project
    assert "Copyright (c) 2026 GaBoron" in license_text
    assert "<Product>Steam 成就翻译管理器</Product>" in gui_project
    assert "<Deterministic>True</Deterministic>" in gui_project
    assert "<PathMap>$(MSBuildProjectDirectory)=/_/Satl.Gui</PathMap>" in gui_project
    assert '$ReleasePrivacyScript = Join-Path $PSScriptRoot "release_privacy.py"' in build_script
    assert build_script.count("--path $PackageRoot") == 1
    assert build_script.count("--path $ReleaseArtifact") == 1

    for surface in (
        build_script,
        (ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8"),
    ):
        assert "SHA256SUMS.txt" not in surface


def test_local_build_owns_both_release_channels_while_github_runs_tests_only() -> None:
    build_script = (ROOT / "scripts" / "build.ps1").read_text(encoding="utf-8")
    ci = (ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")

    assert '$Target -in @("All", "Installer")' in build_script
    assert '$Target -in @("All", "StoreMsix")' in build_script
    assert "$ReleaseArtifacts.Add($SetupExecutable)" in build_script
    assert "$ReleaseArtifacts.Add($BuiltStorePackage)" in build_script
    assert "Built $($ReleaseArtifacts.Count) $Target release asset(s)" in build_script
    assert "package:" not in ci
    assert "build.ps1" not in ci
    assert not (ROOT / ".github" / "workflows" / "store-msix.yml").exists()


def test_root_gitignore_covers_generated_sensitive_and_release_files() -> None:
    ignored = (ROOT / ".gitignore").read_text(encoding="utf-8")

    for pattern in (
        "**/[Bb]in/",
        "**/[Oo]bj/",
        "**/Properties/PublishProfiles/",
        "*.msix",
        "*.pdb",
        "*.pfx",
        "*.snk",
        "credentials.*",
        "secrets.json",
        "store/identity.json",
        "*.dmp",
        "Thumbs.db",
    ):
        assert pattern in ignored


def test_store_install_uses_store_managed_updates() -> None:
    distribution_service = (
        ROOT / "src" / "Satl.Gui" / "Services" / "ApplicationDistributionService.cs"
    ).read_text(encoding="utf-8")
    main_view_model = (
        ROOT / "src" / "Satl.Gui" / "ViewModels" / "MainViewModel.cs"
    ).read_text(encoding="utf-8")
    settings_page = (
        ROOT / "src" / "Satl.Gui" / "Pages" / "SettingsPage.xaml"
    ).read_text(encoding="utf-8")

    assert "GetCurrentPackageFullName" in distribution_service
    assert "UsesStoreManagedUpdates" in distribution_service
    assert "Settings.CheckForUpdatesOnStartup && !UsesStoreManagedUpdates" in main_view_model
    assert "此版本由 Microsoft Store 管理软件更新" in main_view_model
    assert 'x:Name="StoreUpdateNotice"' in settings_page


def test_user_documentation_separates_installation_channels() -> None:
    readme = (ROOT / "README.md").read_text(encoding="utf-8")
    usage = (ROOT / "docs" / "USAGE.md").read_text(encoding="utf-8")
    privacy = (ROOT / "docs" / "PRIVACY.md").read_text(encoding="utf-8")

    for document in (readme, usage):
        assert "Microsoft Store 版" in document
        assert "独立安装版" in document
        assert "即将推出" in document
    assert "上架后将由 Microsoft Store 检查、下载和安装" in usage
    assert "GitHub Releases" in usage
    assert "即将推出的 Microsoft Store 版不使用 GitHub 安装程序更新" in privacy
    assert "docs/DEVELOPMENT.md" not in readme
    assert "docs/MICROSOFT_STORE.md" not in readme
    assert not (ROOT / "docs" / "DEVELOPMENT.md").exists()
    assert not (ROOT / "docs" / "MICROSOFT_STORE.md").exists()


def test_release_bundles_pinned_pure_python_git_dependency() -> None:
    project = (ROOT / "pyproject.toml").read_text(encoding="utf-8")
    build_script = (ROOT / "scripts" / "build.ps1").read_text(encoding="utf-8")
    notices = (ROOT / "THIRD_PARTY_NOTICES.md").read_text(encoding="utf-8")

    assert '"dulwich==1.2.12"' in project
    assert '"dulwich==1.2.12"' in build_script
    assert '"urllib3==2.7.0"' in build_script
    assert '.Extension -in @(".pyd", ".dll")' in build_script
    assert 'foreach ($GeneratedEntryPointDirectory in @("bin", "Scripts"))' in build_script
    assert "$GeneratedEntryPointPath" in build_script
    assert "does not contain Dulwich" in build_script
    assert '$env:PATH = ""' in build_script
    assert "schema revisions verify" in build_script
    assert "failed without system PATH" in build_script
    assert "Dulwich" in notices
    assert "Git for Windows" in notices


def test_gui_resolves_the_internal_python_runtime() -> None:
    process_runner = (
        ROOT / "src" / "Satl.Gui" / "Services" / "CliProcessRunner.cs"
    ).read_text(encoding="utf-8")

    assert "var processPath = Environment.ProcessPath;" in process_runner
    assert "var applicationDirectory = ResolveApplicationDirectory();" in process_runner
    assert 'Path.Combine(applicationDirectory, "_runtime")' in process_runner
    assert 'Path.Combine(runtimeDirectory, "python.exe")' in process_runner
    assert 'Path.Combine(runtimeDirectory, "satl.pyz")' in process_runner


def test_main_view_model_delegates_translation_workflows() -> None:
    main_view_model = (
        ROOT / "src" / "Satl.Gui" / "ViewModels" / "MainViewModel.cs"
    ).read_text(encoding="utf-8")
    translation_view_model = (
        ROOT / "src" / "Satl.Gui" / "ViewModels" / "TranslationManagementViewModel.cs"
    ).read_text(encoding="utf-8")
    translation_operations = (
        ROOT
        / "src"
        / "Satl.Gui"
        / "ViewModels"
        / "TranslationManagementViewModel.Operations.cs"
    ).read_text(encoding="utf-8")

    assert "TranslationManagementViewModel Translations" in main_view_model
    assert "SatlCliService" not in main_view_model
    assert "PreviewCurrentAsync" in translation_operations
    assert len(main_view_model.splitlines()) < 400
    assert len(translation_view_model.splitlines()) < 200
    assert len(translation_operations.splitlines()) < 400


def test_achievement_editor_page_is_split_by_ui_responsibility() -> None:
    page_root = ROOT / "src" / "Satl.Gui" / "Pages"
    files = {
        "lifecycle": page_root / "AchievementEditorPage.xaml.cs",
        "editing": page_root / "AchievementEditorPage.Editing.cs",
        "persistence": page_root / "AchievementEditorPage.Persistence.cs",
        "navigation": page_root / "AchievementEditorPage.Navigation.cs",
    }

    for path in files.values():
        assert path.is_file()
        assert len(path.read_text(encoding="utf-8").splitlines()) < 250

    assert "OnNavigatedTo" in files["lifecycle"].read_text(encoding="utf-8")
    assert "SelectTargetLanguageAsync" in files["editing"].read_text(encoding="utf-8")
    assert "SaveChangesAsync" in files["persistence"].read_text(encoding="utf-8")
    assert "Frame_Navigating" in files["navigation"].read_text(encoding="utf-8")


def test_release_surfaces_installable_artifacts_only() -> None:
    surfaces = [
        ROOT / "README.md",
        ROOT / "scripts" / "build.ps1",
        ROOT / ".github" / "workflows" / "ci.yml",
        ROOT / "src" / "Satl.Gui" / "Services" / "UpdateService.cs",
    ]

    for surface in surfaces:
        content = surface.read_text(encoding="utf-8")
        assert "SATLInstaller-Portable" not in content
        assert "PortableDownload" not in content


def test_installer_removes_known_legacy_runtime_before_upgrade() -> None:
    installer = (ROOT / "installer" / "SATLInstaller.iss").read_text(encoding="utf-8")

    assert "[InstallDelete]" in installer
    for legacy_pattern in (
        r'{app}\*.dll',
        r'{app}\*.json',
        r'{app}\*.pri',
        r'{app}\*.winmd',
        r'{app}\*.xbf',
        r'{app}\satl.exe',
        r'{app}\createdump.exe',
        r'{app}\RestartAgent.exe',
        r'{app}\_satl_runtime',
        r'{app}\Assets',
        r'{app}\Microsoft.UI.Xaml',
        r'{app}\Pages',
    ):
        assert legacy_pattern in installer

    assert r'Type: filesandordirs; Name: "{app}\*"' not in installer
    assert r'Type: files; Name: "{app}\*.exe"' not in installer
