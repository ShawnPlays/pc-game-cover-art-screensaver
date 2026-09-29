; Installer for PC Game Cover Art Screensaver, built with Inno Setup 6 (https://jrsoftware.org/isinfo.php).
; scripts/build-release.ps1 compiles it after building the screensaver and add-on:
;   ISCC.exe /DAppVersion=1.4.0 /DDistDir=..\dist installer\PCGameCoverArt.iss
;
; What it does:
;   - puts PCGameCoverArt.scr in C:\Windows\System32, where Windows' Screen Saver Settings lists it
;     (an installer's files aren't marked as downloaded, so there's nothing to unblock)
;   - closes a running copy first, so upgrading never fails with "file in use"
;   - optionally makes it the active screensaver
;   - if Playnite is installed, offers to install or update the Playnite add-on
;   - adds an entry to Windows' Installed apps, so it can be uninstalled normally
; Running a newer installer over an older one upgrades it and keeps the user's settings.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef DistDir
  #define DistDir "..\dist"
#endif

#define AppName "PC Game Cover Art Screensaver"
#define RepoUrl "https://github.com/ShawnPlays/pc-game-cover-art-screensaver"

[Setup]
; Never change AppId: Windows uses it to recognise an upgrade of the same app.
AppId={{8B4B3E5A-6C1F-4F2B-9C63-2E7B8B1D4A11}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=ShawnPlays
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\PC Game Cover Art
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
; System32 needs administrator rights; 64-bit mode makes {sys} the real System32, not SysWOW64.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#DistDir}
OutputBaseFilename=PCGameCoverArtSetup_{#AppVersion}
SetupIconFile=..\src\CoverArtSaver\icon.ico
UninstallDisplayIcon={sys}\PCGameCoverArt.scr
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=no
; The screensaver setting is per user; the installer runs elevated as the signed-in user in the usual case.
UsedUserAreasWarning=no

[Tasks]
Name: activate; Description: "Use it as my screensaver"

[Files]
Source: "{#DistDir}\PCGameCoverArt.scr"; DestDir: "{sys}"; Flags: ignoreversion
; Kept next to the uninstaller so the add-on can be installed from the finish page (and again later).
Source: "{#DistDir}\PCGameCoverArtExporter_{#AppVersion}.pext"; DestDir: "{app}"; DestName: "PCGameCoverArtExporter.pext"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
; Windows' own setting for the active screensaver. HKCU is the signed-in user's (the installer is elevated as them).
Root: HKCU; Subkey: "Control Panel\Desktop"; ValueType: string; ValueName: "SCRNSAVE.EXE"; ValueData: "{sys}\PCGameCoverArt.scr"; Tasks: activate
Root: HKCU; Subkey: "Control Panel\Desktop"; ValueType: string; ValueName: "ScreenSaveActive"; ValueData: "1"; Tasks: activate

[Run]
; Opening the .pext hands it to Playnite (Playnite.DesktopApp.exe --installext), which asks before installing.
; Run as the signed-in user, not as administrator, so Playnite starts normally.
Filename: "{app}\PCGameCoverArtExporter.pext"; Description: "Install or update the Playnite add-on (Playnite users only)"; \
  Flags: shellexec postinstall runasoriginaluser skipifsilent; Check: PlayniteInstalled
Filename: "{sys}\PCGameCoverArt.scr"; Parameters: "/c"; Description: "Open the screensaver settings"; \
  Flags: postinstall runasoriginaluser nowait skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM PCGameCoverArt.scr /T"; Flags: runhidden; RunOnceId: "StopScreensaver"

[Messages]
FinishedLabel=The screensaver is installed.%n%nWindows' Screen Saver Settings (Start → "Change screen saver") lists it as PCGameCoverArt. Your settings from any earlier version are kept.

[Code]
// Playnite registers .pext files with its "Playnite.ext" handler; portable copies of Playnite don't.
function PlayniteInstalled: Boolean;
begin
  Result := RegKeyExists(HKEY_CLASSES_ROOT, 'Playnite.ext\shell\open\command');
end;

// Windows can't replace the .scr while it runs: the screensaver itself, its settings window, or the little
// preview in Screen Saver Settings. Stop them before copying files.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM PCGameCoverArt.scr /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

// Uninstalling: if it's the active screensaver, stop Windows pointing at a file that's about to go.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Current: String;
begin
  if (CurUninstallStep = usPostUninstall)
    and RegQueryStringValue(HKEY_CURRENT_USER, 'Control Panel\Desktop', 'SCRNSAVE.EXE', Current)
    and (Pos('PCGAMECOVERART.SCR', Uppercase(Current)) > 0) then
  begin
    RegDeleteValue(HKEY_CURRENT_USER, 'Control Panel\Desktop', 'SCRNSAVE.EXE');
  end;
end;
