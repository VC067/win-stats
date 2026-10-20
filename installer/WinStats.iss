#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\release"
#endif

[Setup]
AppId={{A8F6D4E1-6B75-4A87-9D3C-2E5F8C1B7A40}
AppName=Win Stats
AppVersion={#AppVersion}
AppPublisher=Win Stats
DefaultDirName={localappdata}\Programs\WinStats
DefaultGroupName=Win Stats
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.22000
CloseApplications=yes
RestartApplications=no
Uninstallable=yes
UninstallDisplayIcon={app}\WinStats.exe
SetupIconFile=..\src\StatsBar\Assets\WinStats.ico
OutputDir={#OutputDir}
OutputBaseFilename=WinStats-Setup-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Win Stats"; Filename: "{app}\WinStats.exe"
Name: "{autodesktop}\Win Stats"; Filename: "{app}\WinStats.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\WinStats.exe"; Description: "Launch Win Stats"; Flags: postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupCommand: string;
  InstalledCommand: string;
begin
  if CurUninstallStep = usUninstall then
  begin
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Win Stats', StartupCommand) then
    begin
      InstalledCommand := '"' + ExpandConstant('{app}\WinStats.exe') + '"';
      if CompareText(StartupCommand, InstalledCommand) = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Win Stats');
    end;
  end;
end;
