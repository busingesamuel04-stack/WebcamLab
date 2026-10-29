; WebcamLab Inno Setup Script
; Official Stable Release Installer for Windows x64

#define MyAppName "WebcamLab"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "Antigravity Imaging"
#define MyAppExeName "WebcamLab.exe"
#define MyAppSourcePath "C:\Users\sambu\source\WebcamLab_Staging\Phase35_Stable_v1.1.0_Staging\app"

[Setup]
AppId={{E517B1B6-84F7-47C2-9E15-E4E30FA4B6C3}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile={#MyAppSourcePath}\LICENSE
OutputDir=C:\Users\sambu\source\WebcamLab\publish
OutputBaseFilename=WebcamLab-Setup-v1.1.0-x64
SetupIconFile={#MyAppSourcePath}\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#MyAppSourcePath}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\app.ico"
Name: "{group}\Documentation (README)"; Filename: "{app}\README.md"
Name: "{group}\Third-Party Licenses"; Filename: "{app}\THIRD_PARTY_LICENSES.md"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\app.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Only delete files installed in {app}. Explicitly NEVER touch user data in %USERPROFILE%\Videos or %LOCALAPPDATA%
Type: filesandordirs; Name: "{app}"
