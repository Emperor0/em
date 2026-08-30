#define MyAppName "D7 System Intelligence"
#ifndef MyAppVersion
  #define MyAppVersion "0.4.0"
#endif
#define MyAppExeName "D7SystemIntelligence.exe"

[Setup]
AppId={{0A5D7AB4-6AF8-4A51-AB32-7D7D7D7D0701}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
DefaultDirName={autopf}\D7 System Intelligence
DefaultGroupName={#MyAppName}
OutputDir=output
OutputBaseFilename=D7-System-Intelligence-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch D7 System Intelligence"; Flags: nowait postinstall skipifsilent

[Code]
function IsSilentUpdate: Boolean;
begin
  Result := WizardSilent and (ExpandConstant('{param:D7UPDATE|0}') = '1');
end;

function RecoveryDir: String;
begin
  Result := ExpandConstant('{localappdata}\D7SystemIntelligence\UpdateRecovery');
end;

function PreviousExe: String;
begin
  Result := RecoveryDir + '\D7SystemIntelligence.previous.exe';
end;

procedure LogRecovery(const Line: String);
begin
  ForceDirectories(RecoveryDir);
  SaveStringToFile(RecoveryDir + '\installer-recovery.log',
    GetDateTimeString('yyyy-mm-dd hh:nn:ss', '-', ':') + ' ' + Line + #13#10, True);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  CurrentExe: String;
  ResultCode: Integer;
  Started: Boolean;
begin
  if not IsSilentUpdate then
    exit;

  CurrentExe := ExpandConstant('{app}\{#MyAppExeName}');

  if CurStep = ssInstall then
  begin
    ForceDirectories(RecoveryDir);
    if FileExists(CurrentExe) then
    begin
      if FileCopy(CurrentExe, PreviousExe, False) then
        LogRecovery('BACKUP_OK ' + CurrentExe)
      else
        LogRecovery('BACKUP_FAILED ' + CurrentExe);
    end;
  end;

  if CurStep = ssPostInstall then
  begin
    LogRecovery('HEALTHCHECK_START v{#MyAppVersion}');
    Started := Exec(CurrentExe, '--post-update-healthcheck', ExpandConstant('{app}'),
      SW_HIDE, ewWaitUntilTerminated, ResultCode);

    if Started and (ResultCode = 0) then
    begin
      LogRecovery('HEALTHCHECK_OK v{#MyAppVersion}');
      Exec(CurrentExe, '', ExpandConstant('{app}'), SW_SHOWNORMAL, ewNoWait, ResultCode);
    end
    else
    begin
      LogRecovery('HEALTHCHECK_FAILED code=' + IntToStr(ResultCode));
      if FileExists(PreviousExe) and FileCopy(PreviousExe, CurrentExe, False) then
      begin
        LogRecovery('ROLLBACK_OK previous executable restored');
        Exec(CurrentExe, '', ExpandConstant('{app}'), SW_SHOWNORMAL, ewNoWait, ResultCode);
      end
      else
      begin
        LogRecovery('ROLLBACK_FAILED previous executable unavailable or copy failed');
      end;
    end;
  end;
end;
