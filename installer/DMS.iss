#define AppName "DMS"
#define AppPublisher "DMS"
#define AppExeName "DMS.exe"

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#ifndef PublishDirectory
  #define PublishDirectory "..\publish\DMS-" + AppVersion
#endif

[Setup]
AppId={{B8C6E9E2-1B4E-4F2E-9D91-7E1A4B1D2A10}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=..\Assets\dms-logo.ico
OutputDir=..\publish
OutputBaseFilename=DMS-Setup-{#AppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no

[Files]
Source: "{#PublishDirectory}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDirectory}\.env"; DestDir: "{app}"; Flags: ignoreversion onlyifdoesntexist

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: postinstall nowait skipifsilent