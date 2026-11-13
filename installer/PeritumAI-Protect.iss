; Inno Setup 6.4+ or 7 script for PeritumAI Protect. Build it with build\publish-release.ps1, which passes
; /DAppVersion=<x.y.z> and /DPublishDir=<folder with the published app>.
; Per-user install: no administrator rights, files go to %LOCALAPPDATA%\Programs\PeritumAI Protect.

#ifndef AppVersion
  #error Pass /DAppVersion=x.y.z
#endif
#ifndef PublishDir
  #error Pass /DPublishDir=<published app folder>
#endif

#define AppName "PeritumAI Protect"
#define AppExe "Peritum.Protect.exe"
#define AppPublisher "Danilo Stojković (PeritumAI)"
#define AppUrl "https://peritumai.com/protect"

[Setup]
; Never change AppId: Windows uses it to find the installed copy for updates and uninstall.
AppId={{F2BA2417-B4AA-412D-A0F9-6BFA98B276A4}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://peritumai.com
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}
AppCopyright=Copyright 2026 Danilo Stojković
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} setup
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
LicenseFile=..\LICENSE
SetupIconFile=..\PeritumProtect.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=force
RestartApplications=no
OutputDir=..\artifacts
OutputBaseFilename=PeritumAI-Protect-{#AppVersion}-setup-x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; The AppUserModelID must match Peritum.Protect.App.Native.AppUserModel.Id, so the taskbar groups the window with this shortcut.
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; AppUserModelID: "Peritum.Protect"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; AppUserModelID: "Peritum.Protect"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Removes the Startup shortcut and the Explorer menu entries. Settings and activity in %LOCALAPPDATA%\Peritum\Protect stay.
Filename: "{app}\{#AppExe}"; Parameters: "--uninstall-cleanup"; Flags: runhidden waituntilterminated; RunOnceId: "Cleanup"
