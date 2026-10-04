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
; Always the Meloday Music folder, even over an install made under the old name.
UsePreviousAppDir=no
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\artifacts\final
OutputBaseFilename=MelodayMusic-Setup-{#AppVersion}-x64
SetupIconFile=..\src\HumeMusic.App\Assets\MelodayMusic.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Ask the app to close, and if it takes too long (it saves the queue and tells Plex first), close it.
CloseApplications=force
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[InstallDelete]
; The app's old name: its install folder and shortcuts go, replaced by Meloday Music's.
Type: filesandordirs; Name: "{localappdata}\Programs\Hume Music"
Type: files; Name: "{autoprograms}\Hume Music.lnk"
Type: files; Name: "{autodesktop}\Hume Music.lnk"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
