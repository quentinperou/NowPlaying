; NowPlaying installer (Inno Setup 6), in English and French (picked from the Windows display language).
; Built by scripts\build.ps1, which prepares dist\app first:
;   ISCC.exe /DAppVersion=1.0.0 installer\NowPlaying.iss
; The app also runs it to install an update (src\NowPlaying\Services\Updater.cs): silent, closing the running app
; (its hidden window answers the Restart Manager's WM_CLOSE), /RELAUNCH=1.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

; Windows file versions are numbers only: 1.1.0 for a 1.1.0-beta.1 pre-release.
#if Pos("-", AppVersion) > 0
  #define AppFileVersion Copy(AppVersion, 1, Pos("-", AppVersion) - 1)
#else
  #define AppFileVersion AppVersion
#endif

#define AppName "NowPlaying"
#define AppExe "NowPlaying.exe"
; The widget of the Windows Widgets board: its provider (started by Windows) and the package that lists it.
#define WidgetsExe "NowPlaying.Widgets.exe"
#define WidgetsPackage "NowPlaying.Widgets"

[Setup]
AppId={{870C6294-F4BF-485B-A89F-F67C2C88C264}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=QuentinPerou
AppPublisherURL=https://github.com/quentinperou
AppSupportURL=https://github.com/quentinperou/NowPlaying
AppUpdatesURL=https://github.com/quentinperou/NowPlaying/releases
VersionInfoVersion={#AppFileVersion}
DefaultDirName={autopf}\NowPlaying
DisableProgramGroupPage=yes
; Admin rights: installs for every user of the PC, lets the .NET runtime be installed, and registers the unsigned
; package of the widget (Add-AppxPackage -AllowUnsigned).
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\dist
OutputBaseFilename=NowPlaying-Setup-{#AppVersion}
SetupIconFile=..\src\NowPlaying\Assets\NowPlaying.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
; Only ask for the language when Windows uses one the installer does not have.
ShowLanguageDialog=auto

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[CustomMessages]
english.RuntimeMissing=NowPlaying needs the Microsoft ".NET 10 Desktop Runtime", which is not installed on this PC.%nInstall it now?
french.RuntimeMissing=NowPlaying a besoin du « .NET 10 Desktop Runtime » (Microsoft), absent de ce PC.%nL'installer maintenant ?
english.RuntimeInstalling=Installing the .NET 10 Desktop Runtime...
french.RuntimeInstalling=Installation du .NET 10 Desktop Runtime…
english.RuntimeFailed=The automatic installation failed. The download page will open:%nchoose ".NET Desktop Runtime 10" (x64).
french.RuntimeFailed=L'installation automatique a échoué. La page de téléchargement va s'ouvrir :%nchoisissez « .NET Desktop Runtime 10 » (x64).
english.DeleteUserData=Also delete your NowPlaying settings (the style of the widget and of the overlays)?%n%nKeep them if you are going to install NowPlaying again.
french.DeleteUserData=Supprimer aussi vos réglages NowPlaying (le style du widget et des overlays) ?%n%nGardez-les si vous allez réinstaller NowPlaying.
english.WidgetNotRegistered=The NowPlaying widget could not be added to the Windows Widgets board.%nThe rest of NowPlaying works as usual.
french.WidgetNotRegistered=Le widget NowPlaying n'a pas pu être ajouté au tableau Widgets de Windows.%nLe reste de NowPlaying fonctionne normalement.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\dist\NowPlaying.Widgets.msix"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
; "Start with Windows" is written by the app itself (Settings › General, on by default once installed): removed with it.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "NowPlaying"; Flags: uninsdeletevalue dontcreatekey
Root: HKCU; Subkey: "Software\NowPlaying"; ValueType: none; Flags: uninsdeletekey dontcreatekey

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[UninstallDelete]
; Temporary files of the app: the last update downloaded.
Type: filesandordirs; Name: "{%TEMP}\NowPlaying-Update"

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser
; Update installed from the app (silent, /RELAUNCH=1): start the app again, as the user, not elevated, without
; opening its settings.
Filename: "{app}\{#AppExe}"; Parameters: "--background"; Flags: nowait runasoriginaluser skipifnotsilent; Check: RelaunchRequested

[Code]
{ Set by the app when it installs an update (Updater.InstallerArguments). }
function RelaunchRequested: Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|0}') = '1';
end;

{ The app is framework-dependent: it needs the .NET 10 Desktop Runtime. }
function IsDesktopRuntimeInstalled: Boolean;
var
  FindRec: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), FindRec);
  if Result then
    FindClose(FindRec);
end;

procedure EnsureDesktopRuntime;
var
  ResultCode: Integer;
begin
  if IsDesktopRuntimeInstalled then
    Exit;
  if MsgBox(CustomMessage('RuntimeMissing'), mbConfirmation, MB_YESNO) <> IDYES then
    Exit;
  WizardForm.StatusLabel.Caption := CustomMessage('RuntimeInstalling');
  if not Exec('winget.exe', 'install --id Microsoft.DotNet.DesktopRuntime.10 -e --silent --accept-source-agreements --accept-package-agreements',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or not IsDesktopRuntimeInstalled then
  begin
    MsgBox(CustomMessage('RuntimeFailed'), mbInformation, MB_OK);
    ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/10.0', '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
  end;
end;

function IsWindows11: Boolean;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  Result := (Version.Major > 10) or ((Version.Major = 10) and (Version.Build >= 22000));
end;

function RunPowerShell(const Command: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "' + Command + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function SetEnvironmentVariable(Name, Value: String): BOOL;
  external 'SetEnvironmentVariableW@kernel32.dll stdcall';

{ The provider of the widget runs while a NowPlaying widget is on the board (Windows starts it): ended so that its
  files can be replaced or removed. Windows starts it again when the board needs it. }
procedure StopWidgetProvider;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/f /im {#WidgetsExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

{ Windows 11 Widgets board: sparse package pointing at the install folder. Registered again at each install, but
  never removed by an update: Windows would take the widget off the user's board. When the registration in place
  does not match (another folder, another package), it is replaced. }
procedure RegisterWidget;
begin
  { The paths reach PowerShell through environment variables (inherited by the child process), not inside the
    command text: a quote in the install folder would end a quoted string and break the command. }
  SetEnvironmentVariable('NOWPLAYING_MSIX', ExpandConstant('{app}\NowPlaying.Widgets.msix'));
  SetEnvironmentVariable('NOWPLAYING_LOCATION', ExpandConstant('{app}'));
  if RunPowerShell('Add-AppxPackage -Path $env:NOWPLAYING_MSIX -ExternalLocation $env:NOWPLAYING_LOCATION -AllowUnsigned') then
    Log('Widget package registered')
  else if RunPowerShell('Get-AppxPackage -Name ''{#WidgetsPackage}'' | Remove-AppxPackage; ' +
    'Add-AppxPackage -Path $env:NOWPLAYING_MSIX -ExternalLocation $env:NOWPLAYING_LOCATION -AllowUnsigned') then
    Log('Widget package registered again')
  else
    SuppressibleMsgBox(CustomMessage('WidgetNotRegistered'), mbInformation, MB_OK, IDOK);
end;

{ The uninstaller drops the package for every account, since an administrator who is not the user may have accepted
  the UAC prompt of an earlier install. }
procedure UnregisterWidget;
begin
  StopWidgetProvider;
  if RunPowerShell('Get-AppxPackage -AllUsers -Name ''{#WidgetsPackage}'' | Remove-AppxPackage -AllUsers') then
    Log('Widget package removed for all accounts')
  else if RunPowerShell('Get-AppxPackage -Name ''{#WidgetsPackage}'' | Remove-AppxPackage') then
    Log('Widget package removed for the current account')
  else
    Log('Could not remove the widget package');
end;

{ Called before Setup looks for the applications using the files it replaces (CloseApplications): with the provider
  ended here, an update does not ask to close it. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopWidgetProvider;
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    EnsureDesktopRuntime;
    if IsWindows11 then
      RegisterWidget;
  end;
end;

{ NowPlaying runs in the notification area: close it before its files are removed. taskkill without /f sends
  WM_CLOSE to the windows of NowPlaying.exe only (its hidden window quits the app, and the widget process ends with
  it); a window title would also match a File Explorer window open on a folder named NowPlaying. /f for what is left. }
procedure CloseRunningApp;
var
  ResultCode: Integer;
begin
  if Exec('taskkill.exe', '/im {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
    Sleep(1500);
  Exec('taskkill.exe', '/f /im {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function InitializeUninstall: Boolean;
begin
  CloseRunningApp;
  Result := True;
end;

{ Settings (%LOCALAPPDATA%\NowPlaying, of the account that uninstalls): kept unless the user says otherwise, "No"
  being the default, so that a reinstall finds them. A silent uninstall keeps them. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  { Before the files go: the package points at them. The start with Windows (written by the app, not by Setup) goes
    too: it would start an exe that is gone. }
  if CurUninstallStep = usUninstall then
  begin
    UnregisterWidget;
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'NowPlaying');
  end;
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent and
     (MsgBox(CustomMessage('DeleteUserData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
    DelTree(ExpandConstant('{localappdata}\NowPlaying'), True, True, True);
end;
