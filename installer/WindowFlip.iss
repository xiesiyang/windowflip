#define AppName "WindowFlip"
#define AppPublisher "WindowFlip"
#define AppExeName "WindowFlip.exe"

#ifndef AppVersion
  #error AppVersion must be provided by scripts\publish.ps1.
#endif

#ifndef SourceDir
  #error SourceDir must be provided by scripts\publish.ps1.
#endif

#ifndef OutputDir
  #error OutputDir must be provided by scripts\publish.ps1.
#endif

#ifndef SetupIconPath
  #error SetupIconPath must be provided by scripts\publish.ps1.
#endif

[Setup]
AppId={{3F93D258-9555-47E0-81B9-12760C702F6E}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
SetupIconFile={#SetupIconPath}
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
AppMutex=Local\WindowFlip.6D9B13A9-20E7-4FC5-82BF-B63E8ED1EC86

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#SourceDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(
      HKCU,
      'Software\Microsoft\Windows\CurrentVersion\Run',
      'WindowFlip');
end;
