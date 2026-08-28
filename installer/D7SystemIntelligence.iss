#define MyAppName "D7 System Intelligence"
#define MyAppVersion "0.1.0"
#define MyAppExeName "D7SystemIntelligence.exe"
[Setup]
AppId={{0A5D7AB4-6AF8-4A51-AB32-7D7D7D7D0701}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
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
