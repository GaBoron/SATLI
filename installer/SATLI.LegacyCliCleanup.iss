// Releases that used a separate command helper left the cli directory behind.
// The new in-process command core no longer needs it, so upgrades recycle that
// exact legacy directory before installing the new payload.

[Code]
const
  ShellRecycleBinNamespace = 10;
  ShellMoveSilent = 4;
  ShellMoveNoConfirmation = 16;
  ShellMoveNoErrorUi = 1024;

procedure RecycleLegacyCliDirectory;
var
  LegacyCliDirectory: String;
  ShellApplication: Variant;
  RecycleBinFolder: Variant;
  WaitAttempt: Integer;
begin
  LegacyCliDirectory := ExpandConstant('{app}\cli');
  if not DirExists(LegacyCliDirectory) then
    Exit;

  Log('Legacy command helper directory detected; moving it to the Recycle Bin.');
  try
    ShellApplication := CreateOleObject('Shell.Application');
    RecycleBinFolder := ShellApplication.NameSpace(ShellRecycleBinNamespace);
    if VarIsClear(RecycleBinFolder) then
      RaiseException('Windows Recycle Bin is unavailable.');

    RecycleBinFolder.MoveHere(
      LegacyCliDirectory,
      ShellMoveSilent or ShellMoveNoConfirmation or ShellMoveNoErrorUi);
    for WaitAttempt := 1 to 50 do
    begin
      if not DirExists(LegacyCliDirectory) then
        Break;
      Sleep(100);
    end;

    if DirExists(LegacyCliDirectory) then
      RaiseException('The legacy command helper directory is still present.');
    Log('Legacy command helper directory moved to the Recycle Bin.');
  except
    Log('Legacy command helper cleanup failed: ' + GetExceptionMessage);
    RaiseException(
      '无法将旧版命令行组件移入 Windows 回收站。请关闭占用 SATLI 安装目录的程序后重试。');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    RecycleLegacyCliDirectory;
end;
