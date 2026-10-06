#define AppName "PaperLike"
#define AppVersion "0.1.0"
#define AppPublisher "PaperLike contributors"
#define AppExeName "PaperLike.exe"

[Setup]
AppId={{6F90B012-4FCA-42D9-A0A9-36882BD38D03}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\PaperLike
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
OutputDir=..\..\artifacts\windows
OutputBaseFilename=PaperLike-Setup-x64
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Files]
Source: "..\..\artifacts\windows\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\PaperLike"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\PaperLike"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch PaperLike"; Flags: nowait postinstall skipifsilent
