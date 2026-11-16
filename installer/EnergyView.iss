#ifndef AppVersion
  #define AppVersion "0.1.5"
#endif
#ifndef PublishDir
  #define PublishDir "..\dist\app"
#endif

[Setup]
AppId={{5557C34B-DEE4-4C3E-91F0-3934F394538C}
AppName=Energy View
AppVersion={#AppVersion}
AppPublisher=Energy View
AppPublisherURL=https://github.com/srviniaviz/energy-view
AppUpdatesURL=https://github.com/srviniaviz/energy-view/releases/latest
DefaultDirName={autopf}\Energy View
DefaultGroupName=Energy View
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
UninstallDisplayIcon={app}\EnergyView.exe
SetupIconFile=..\Assets\EnergyView.ico
OutputDir=..\dist\release
OutputBaseFilename=EnergyView-Setup-v{#AppVersion}-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\EnergyView.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Energy View"; Filename: "{app}\EnergyView.exe"

[Run]
Filename: "{app}\EnergyView.exe"; Description: "{cm:LaunchProgram,Energy View}"; Flags: nowait postinstall skipifsilent runascurrentuser; Check: not IsUpdate
Filename: "{app}\EnergyView.exe"; Flags: nowait; Check: IsUpdate

[UninstallDelete]
Type: filesandordirs; Name: "{app}\updates"

[Code]
function IsUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;

procedure CurUninstallStepChanged(Step: TUninstallStep);
var
  Service, Root, Tasks, Task: Variant;
  I: Integer;
  Name: String;
begin
  if Step <> usUninstall then
    Exit;
  try
    Service := CreateOleObject('Schedule.Service');
    Service.Connect();
    Root := Service.GetFolder('\');
    Tasks := Root.GetTasks(0);
    for I := Tasks.Count downto 1 do
    begin
      Task := Tasks.Item[I];
      Name := Task.Name;
      if Pos('EnergyView-S-1-', Name) = 1 then
        Root.DeleteTask(Name, 0);
    end;
  except
    Log('Não foi possível remover as tarefas de início automático do Energy View.');
  end;
end;
