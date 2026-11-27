; PickMatch installer for Windows (Inno Setup 6).
; Built by GitHub Actions:  iscc /DMyAppVersion=3.0.0 /DMyArch=x64 /DSourceDir=..\..\publish\PickMatch PickMatch.iss
; Installs for the current user (no admin needed); "Install for all users" is offered too.

#define MyAppName "PickMatch"
#define MyAppExe "PickMatch.exe"
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#ifndef MyArch
  #define MyArch "x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\publish\PickMatch"
#endif

[Setup]
AppId={{8D3F2A61-5C4E-4B7A-9E21-6A1F0C3B7D54}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=PickMatch
AppPublisherURL=https://github.com/MedTaha4Ever/PickMatch
AppSupportURL=https://github.com/MedTaha4Ever/PickMatch/issues
AppUpdatesURL=https://github.com/MedTaha4Ever/PickMatch/releases
AppComments=Find the original RAW/JPG of the photos your client picked
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
#if MyArch == "arm64"
ArchitecturesAllowed=arm64
#else
ArchitecturesAllowed=x64compatible
#endif
ArchitecturesInstallIn64BitMode={#MyArch == "arm64" ? "arm64" : "x64compatible"}
MinVersion=10.0.17763
LicenseFile=..\..\LICENSE
SetupIconFile=..\..\src\PickMatch\Assets\PickMatch.ico
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
OutputDir=..\..\dist
OutputBaseFilename=PickMatch-setup-windows-{#MyArch}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
VersionInfoVersion={#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
