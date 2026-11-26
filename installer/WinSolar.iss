; Win Solar 3 installer (Inno Setup 6). Built by build/release.ps1.
; Per-user install: no administrator rights, no UAC prompt.

#ifndef Version
  #define Version "3.0.0"
#endif

[Setup]
AppId={{6F1B4C2E-8A3D-4E7B-9C51-2D7E4A9B3F10}
AppName=Win Solar
AppVersion={#Version}
AppVerName=Win Solar {#Version}
AppPublisher=Celal Çelikkaya
AppPublisherURL=https://github.com/celalcelikkaya/win-solar
DefaultDirName={localappdata}\Programs\Win Solar
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=WinSolarSetup-{#Version}
SetupIconFile=..\build\icon.ico
UninstallDisplayIcon={app}\WinSolar.exe
UninstallDisplayName=Win Solar
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#Version}
VersionInfoCompany=Celal Çelikkaya
VersionInfoProductName=Win Solar
VersionInfoDescription=Win Solar Setup

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; Left by the Win Solar 2 portable build: points into a deleted %TEMP% folder and shows a blank icon in Start.
Type: files; Name: "{userprograms}\WinSolar.lnk"

[Icons]
Name: "{userprograms}\Win Solar"; Filename: "{app}\WinSolar.exe"

[Registry]
; Created by the app itself ("Start with Windows"); removed when Win Solar is uninstalled.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Win Solar"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\WinSolar.exe"; Description: "Start Win Solar"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM WinSolar.exe /F"; Flags: runhidden; RunOnceId: "StopWinSolar"

[Code]
const
  UninstallRoot = 'Software\Microsoft\Windows\CurrentVersion\Uninstall';
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

function DesktopRuntime8Installed: Boolean;
var
  FindRec: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), FindRec);
  if Result then FindClose(FindRec);
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not DesktopRuntime8Installed then
  begin
    if MsgBox('Win Solar needs the .NET 8 Desktop Runtime (free, from Microsoft).' + #13#10 + #13#10 +
              'Open the download page now? Install "Windows Desktop Runtime 8 (x64)", then run this setup again.',
              mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/8.0', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    Result := False;
  end;
end;

{ Win Solar 2 was an Electron app (NSIS uninstaller): find it, per-user or per-machine. }
function FindV2(RootKey: Integer; var KeyPath, Uninstaller, Args: String): Boolean;
var
  Keys: TArrayOfString;
  I, Q: Integer;
  Path, Name, Ver, Command: String;
begin
  Result := False;
  if not RegGetSubkeyNames(RootKey, UninstallRoot, Keys) then Exit;
  for I := 0 to GetArrayLength(Keys) - 1 do
  begin
    Path := UninstallRoot + '\' + Keys[I];
    if RegQueryStringValue(RootKey, Path, 'DisplayName', Name) and (Pos('Win Solar', Name) = 1) and
       RegQueryStringValue(RootKey, Path, 'DisplayVersion', Ver) and (Pos('2.', Ver) = 1) and
       RegQueryStringValue(RootKey, Path, 'UninstallString', Command) then
    begin
      { "C:\...\Uninstall Win Solar.exe" /currentuser  ->  exe path + arguments }
      Command := Trim(Command);
      if Copy(Command, 1, 1) = '"' then
      begin
        Delete(Command, 1, 1);
        Q := Pos('"', Command);
        if Q = 0 then Exit;
        Uninstaller := Copy(Command, 1, Q - 1);
        Args := Trim(Copy(Command, Q + 1, Length(Command)));
      end
      else
      begin
        Uninstaller := Command;
        Args := '';
      end;
      KeyPath := Path;
      Result := FileExists(Uninstaller);
      Exit;
    end;
  end;
end;

procedure RemoveV2;
var
  RootKey, ResultCode, Waited: Integer;
  KeyPath, Uninstaller, Args, Dir: String;
begin
  RootKey := HKEY_CURRENT_USER;
  if not FindV2(RootKey, KeyPath, Uninstaller, Args) then
  begin
    RootKey := HKEY_LOCAL_MACHINE;
    if not FindV2(RootKey, KeyPath, Uninstaller, Args) then Exit;
  end;

  if SuppressibleMsgBox('Win Solar 2 is installed. Remove it now?' + #13#10 + #13#10 +
            'Win Solar 3 replaces it. Running both would make them fight over the theme.',
            mbConfirmation, MB_YESNO, IDYES) <> IDYES then Exit;

  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM "Win Solar.exe" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  RegDeleteValue(HKEY_CURRENT_USER, RunKey, 'electron.app.Win Solar');
  RegDeleteValue(HKEY_CURRENT_USER, RunKey, 'com.winsolar.app');
  RegDeleteValue(HKEY_CURRENT_USER, RunKey, 'Win Solar');

  { A per-machine Win Solar 2 needs administrator rights to remove; this setup never asks for them. }
  if RootKey = HKEY_LOCAL_MACHINE then
  begin
    SuppressibleMsgBox('Win Solar 2 is installed for all users, which needs administrator rights to remove.' + #13#10 +
      'Please remove it in Settings > Apps after this setup finishes.', mbInformation, MB_OK, IDOK);
    Exit;
  end;

  { An NSIS uninstaller copies itself to %TEMP%, starts that copy and returns at once. The copy then deletes
    "Programs\Win Solar" and the "Win Solar" Start shortcut - the same folder and shortcut Win Solar 3 uses - while
    or after we install, which broke the 3.0.0 install. "_?=<folder>" makes it run in place and finish first. }
  Dir := ExtractFileDir(Uninstaller);
  if not Exec(Uninstaller, Trim(Args + ' /S') + ' _?=' + Dir, Dir, SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    SuppressibleMsgBox('Win Solar 2 could not be removed automatically. Please remove it in Settings > Apps.',
      mbInformation, MB_OK, IDOK);
    Exit;
  end;

  { Belt and braces: wait (up to 60 s) until its uninstall entry is gone. }
  Waited := 0;
  while RegKeyExists(RootKey, KeyPath) and (Waited < 60000) do
  begin
    Sleep(250);
    Waited := Waited + 250;
  end;

  { Run in place, the uninstaller cannot delete itself. }
  DeleteFile(Uninstaller);
end;

{ The app has only hidden windows, so Restart Manager may not close it: stop it before files are replaced. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM WinSolar.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then RemoveV2;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and
     (MsgBox('Also delete your Win Solar settings?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
    DelTree(ExpandConstant('{userappdata}\Win Solar'), True, True, True);
end;
