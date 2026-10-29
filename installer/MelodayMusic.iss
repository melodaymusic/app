; Meloday Music installer (Inno Setup 6).
; Per-user install: no administrator prompt. Build with installer\build.ps1.

#define AppName "Meloday Music"
#define AppExe "MelodayMusic.exe"
; The version is passed in by build.ps1, from Directory.Build.props.
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\x64"
#endif

[Setup]
AppId={{6E2B1C7A-5B3F-4D8E-9A1C-4F7D2E9B8C31}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Meloday Music
DefaultDirName={localappdata}\Programs\Meloday Music
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\artifacts\final
OutputBaseFilename=MelodayMusic-Setup-{#AppVersion}-x64
SetupIconFile=..\src\MelodayMusic.App\Assets\MelodayMusic.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Ask Meloday to quit normally before replacing its files; never force termination.
AppMutex=Local\MelodayMusic-{username}
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode, Attempt: Integer;
begin
  Result := '';
  if FileExists(ExpandConstant('{app}\{#AppExe}')) then
  begin
    Exec(ExpandConstant('{app}\{#AppExe}'), '--quit', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
    for Attempt := 1 to 40 do
    begin
      if not CheckForMutexes(ExpandConstant('Local\MelodayMusic-{username}')) then Break;
      Sleep(250);
    end;
    if CheckForMutexes(ExpandConstant('Local\MelodayMusic-{username}')) then
      Result := 'Please choose Quit in Meloday Music, then try the installation again. Your playback and saved state will be preserved.';
  end;
end;
