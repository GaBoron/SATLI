; SATLI's standalone package is framework-dependent. This module keeps runtime
; detection, verified downloads, and silent prerequisite installation separate
; from the main application installer definition.

[Code]
const
  DotNetRuntimeUrl = 'https://download.microsoft.com/download/a2b8b791-a2da-4835-8b5a-3078153deb88/ff7dbd4b-29c5-4a7f-b1a0-b5efcf3f7771/windowsdesktop-runtime-10.0.11-win-x64.exe';
  DotNetRuntimeFileName = 'windowsdesktop-runtime-10.0.11-win-x64.exe';
  DotNetRuntimeSha256 = '61d2e1447b185d6f99c0d5799896240b48246f5440648bc031ebdb159a3bf3d1';
  WindowsAppRuntimeUrl = 'https://aka.ms/windowsappsdk/2.2/2.2.0/windowsappruntimeinstall-x64.exe';
  WindowsAppRuntimeFileName = 'WindowsAppRuntimeInstall-2.2.0-x64.exe';
  WindowsAppRuntimeSha256 = 'e14abfeedd61ccf1e1b9618a9d4e8e5cad6b6a0becbacf159a50718d047eb927';
  WindowsAppRuntimeRegistryRoot = 'Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages';
  WindowsAppRuntimePackagePrefix = 'Microsoft.WindowsAppRuntime.2_';
  WindowsAppRuntimeX64Suffix = '_x64__8wekyb3d8bbwe';

var
  PrerequisiteDownloadPage: TDownloadWizardPage;
  DotNetRuntimeNeeded: Boolean;
  WindowsAppRuntimeNeeded: Boolean;
  PrerequisitesDownloaded: Boolean;

function DirectoryContainsDotNet10(const RuntimeRoot: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if not DirExists(RuntimeRoot) then
    Exit;

  if FindFirst(AddBackslash(RuntimeRoot) + '10.*', FindRec) then
  begin
    try
      repeat
        if ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and
           (Pos('10.', FindRec.Name) = 1) then
        begin
          Result := True;
          Exit;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function IsDotNetDesktopRuntimeInstalled: Boolean;
begin
  Result :=
    DirectoryContainsDotNet10(
      ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App')) or
    DirectoryContainsDotNet10(
      ExpandConstant('{localappdata}\Microsoft\dotnet\shared\Microsoft.WindowsDesktop.App'));
end;

function IsCompatibleWindowsAppRuntimePackage(const PackageName: String): Boolean;
var
  ArchitecturePosition: Integer;
  FirstDotPosition: Integer;
  SecondDotPosition: Integer;
  VersionText: String;
  MajorText: String;
  MinorText: String;
  MajorVersion: Integer;
  MinorVersion: Integer;
begin
  Result := False;
  if Pos(WindowsAppRuntimePackagePrefix, PackageName) <> 1 then
    Exit;

  ArchitecturePosition := Pos(WindowsAppRuntimeX64Suffix, PackageName);
  if ArchitecturePosition = 0 then
    Exit;

  VersionText := Copy(
    PackageName,
    Length(WindowsAppRuntimePackagePrefix) + 1,
    ArchitecturePosition - Length(WindowsAppRuntimePackagePrefix) - 1);
  FirstDotPosition := Pos('.', VersionText);
  if FirstDotPosition = 0 then
    Exit;

  MajorText := Copy(VersionText, 1, FirstDotPosition - 1);
  Delete(VersionText, 1, FirstDotPosition);
  SecondDotPosition := Pos('.', VersionText);
  if SecondDotPosition = 0 then
    Exit;

  MinorText := Copy(VersionText, 1, SecondDotPosition - 1);
  MajorVersion := StrToIntDef(MajorText, -1);
  MinorVersion := StrToIntDef(MinorText, -1);
  Result := (MajorVersion > 2) or
    ((MajorVersion = 2) and (MinorVersion >= 2));
end;

function IsWindowsAppRuntimeInstalled: Boolean;
var
  PackageNames: TArrayOfString;
  Index: Integer;
begin
  Result := False;
  if not RegGetSubkeyNames(HKCU, WindowsAppRuntimeRegistryRoot, PackageNames) then
    Exit;

  for Index := 0 to GetArrayLength(PackageNames) - 1 do
  begin
    if IsCompatibleWindowsAppRuntimePackage(PackageNames[Index]) then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

procedure RefreshPrerequisiteState;
begin
  DotNetRuntimeNeeded := not IsDotNetDesktopRuntimeInstalled;
  WindowsAppRuntimeNeeded := not IsWindowsAppRuntimeInstalled;
  Log(Format('Prerequisite check: dotnetNeeded=%d, windowsAppRuntimeNeeded=%d', [Ord(DotNetRuntimeNeeded), Ord(WindowsAppRuntimeNeeded)]));
end;

function DownloadMissingPrerequisites: Boolean;
var
  ErrorMessage: String;
begin
  Result := False;
  RefreshPrerequisiteState;
  if not DotNetRuntimeNeeded and not WindowsAppRuntimeNeeded then
  begin
    PrerequisitesDownloaded := True;
    Result := True;
    Exit;
  end;

  PrerequisiteDownloadPage.Clear;
  if DotNetRuntimeNeeded then
    PrerequisiteDownloadPage.Add(
      DotNetRuntimeUrl,
      DotNetRuntimeFileName,
      DotNetRuntimeSha256);
  if WindowsAppRuntimeNeeded then
    PrerequisiteDownloadPage.Add(
      WindowsAppRuntimeUrl,
      WindowsAppRuntimeFileName,
      WindowsAppRuntimeSha256);

  if not WizardSilent then
    PrerequisiteDownloadPage.Show;
  try
    try
      PrerequisiteDownloadPage.Download;
      PrerequisitesDownloaded := True;
      Result := True;
    except
      ErrorMessage := Format('%s：%s', [PrerequisiteDownloadPage.LastBaseNameOrUrl, GetExceptionMessage]);
      Log('Prerequisite download failed: ' + ErrorMessage);
      if not WizardSilent then
        SuppressibleMsgBox(
          '无法下载 SATLI 所需的 Microsoft 运行库。'#13#10 +
          ErrorMessage + #13#10#13#10 +
          '请检查网络连接后重试。',
          mbCriticalError,
          MB_OK,
          IDOK);
    end;
  finally
    if not WizardSilent then
      PrerequisiteDownloadPage.Hide;
  end;
end;

function InstallPrerequisite(
  const DisplayName: String;
  const FileName: String;
  const Parameters: String;
  var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  Log('Installing prerequisite: ' + DisplayName);
  if not Exec(
    ExpandConstant('{tmp}\') + FileName,
    Parameters,
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) then
  begin
    Result := '无法启动 ' + DisplayName + ' 安装程序。';
    Exit;
  end;

  if ResultCode = 3010 then
    NeedsRestart := True
  else if ResultCode <> 0 then
  begin
    Result := Format('%s 安装失败，错误代码：%d。', [DisplayName, ResultCode]);
    Exit;
  end;
  Log(Format('Prerequisite installed: %s, exitCode=%d', [DisplayName, ResultCode]));
end;

procedure InitializeWizard;
begin
  PrerequisiteDownloadPage := CreateDownloadPage(
    '正在准备 SATLI 运行环境',
    '仅在缺少 Microsoft 运行库时联网下载。',
    nil);
  PrerequisiteDownloadPage.ShowBaseNameInsteadOfUrl := True;
  PrerequisitesDownloaded := False;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  if CurPageID = wpReady then
    Result := DownloadMissingPrerequisites
  else
    Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not PrerequisitesDownloaded and not DownloadMissingPrerequisites then
  begin
    Result := 'Microsoft 运行库下载未完成。请检查网络连接后重试。';
    Exit;
  end;

  if DotNetRuntimeNeeded then
  begin
    Result := InstallPrerequisite(
      '.NET 10 Desktop Runtime',
      DotNetRuntimeFileName,
      '/install /quiet /norestart',
      NeedsRestart);
    if Result <> '' then
      Exit;
  end;

  if WindowsAppRuntimeNeeded then
  begin
    Result := InstallPrerequisite(
      'Windows App Runtime 2.2',
      WindowsAppRuntimeFileName,
      '--quiet',
      NeedsRestart);
    if Result <> '' then
      Exit;
  end;

  RefreshPrerequisiteState;
  if DotNetRuntimeNeeded or WindowsAppRuntimeNeeded then
    Result := 'Microsoft 运行库安装后仍未通过检测。请重新启动 Windows 后再次运行安装程序。';
end;
