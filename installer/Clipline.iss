; Clipline setup. Built by scripts/installer.ps1, which publishes the app first:
;   ./scripts/installer.ps1 -Runtime win-x64
; Installs for the current user only: no admin, no UAC prompt.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\installer-win-" + Arch
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

[Setup]
AppId={{1FE3FB15-EC99-4BCD-B7B0-2C0CE19E9643}
AppName=Clipline
AppVersion={#AppVersion}
AppVerName=Clipline {#AppVersion}
AppPublisher=Jay Cheng
AppPublisherURL=https://github.com/xxxJay123/Clipline
AppSupportURL=https://github.com/xxxJay123/Clipline/issues
AppUpdatesURL=https://github.com/xxxJay123/Clipline/releases
AppCopyright=Copyright (c) 2026 Jay Cheng
VersionInfoVersion={#AppVersion}
VersionInfoDescription=Clipline Setup
DefaultDirName={autopf}\Clipline
DisableProgramGroupPage=yes
DisableDirPage=auto
DisableReadyPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed={#Arch}compatible
ArchitecturesInstallIn64BitMode={#Arch}compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=Clipline-Setup-{#AppVersion}-{#Arch}
SetupIconFile=..\src\Clipline\Assets\Clipline.ico
UninstallDisplayIcon={app}\Clipline.exe
UninstallDisplayName=Clipline
LicenseFile=..\LICENSE
WizardStyle=modern
WizardSizePercent=100
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
; Clipline has no window to close; setup ends it itself, see [Code].
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start Clipline with Windows"; GroupDescription: "When Windows starts:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\NOTICE.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Clipline"; Filename: "{app}\Clipline.exe"; Comment: "Your screenshots, on a line at the top of the screen"
Name: "{autodesktop}\Clipline"; Filename: "{app}\Clipline.exe"; Tasks: desktopicon

[Registry]
; The same entry the tray menu's Start with Windows writes, so the two agree.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Clipline"; \
  ValueData: """{app}\Clipline.exe"" --background"; Tasks: startup; Flags: uninsdeletevalue
; Removed on uninstall even when it was turned on from the tray afterwards.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Clipline"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Clipline.exe"; Description: "Open Clipline now"; Flags: nowait postinstall

[Code]
// Clipline lives in the tray with no window to close, so setup and the
// uninstaller end it themselves before touching its files.
procedure StopClipline();
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM Clipline.exe /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(400);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopClipline();
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then StopClipline();
  // The line and settings stay in %LOCALAPPDATA%\Clipline unless the user asks for them to go.
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    if MsgBox('Also remove your Clipline settings and the images it saved from the clipboard?' + #13#10 +
              ExpandConstant('{localappdata}\Clipline'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{localappdata}\Clipline'), True, True, True);
end;
