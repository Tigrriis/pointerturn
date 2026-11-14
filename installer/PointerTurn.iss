; The PointerTurn installer (Inno Setup 6).
;
; Don't compile this by hand: installer\Build-Installer.ps1 builds the add-ins, lays them out
; in a staging folder and then runs the Inno Setup compiler on this script with
;
;   /DAppVersion=1.2.3  /DStageDir=<staging folder>  /DOutputDir=<where setup.exe goes>
;
; What it installs, and where (all users | just me):
;
;   {app}                          C:\Program Files\PointerTurn | %LOCALAPPDATA%\Programs\PointerTurn
;     BricsCAD\net48\              the add-in for BricsCAD V24 and V25 (.NET Framework 4.8)
;     BricsCAD\net8\               the add-in for BricsCAD V26 and later (.NET 8)
;   <app data>\Autodesk\ApplicationPlugins\PointerTurn.bundle
;                                  %ProgramData% | %APPDATA%. AutoCAD 2021 and later load any
;                                  bundle found there by themselves: nothing to register.
;
; BricsCAD has no such folder that can be relied on, so it is told about the add-in in the
; registry, once for every version and language of BricsCAD found on the PC:
;
;   HKLM | HKCU \Software\Bricsys\BricsCAD\V25x64\en_US\Applications\PointerTurn
;     LOADER     full path of PointerTurn.BricsCAD.dll
;     LOADCTRLS  2 (load at startup)
;     MANAGED    1 (a .NET assembly)
;
; A BricsCAD installed later isn't registered until this installer is run again.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef StageDir
  #define StageDir "..\artifacts\stage"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

; Whether this installer holds the .NET 8 build for BricsCAD (a build made with -AllowMissing may not).
#if DirExists(StageDir + "\BricsCAD\net8")
  #define ShipsBricsCADNet8 "yes"
#else
  #define ShipsBricsCADNet8 "no"
#endif

#define AppName "PointerTurn"
#define AppPublisher "Arete Engineering"
#define BundleDir "{autoappdata}\Autodesk\ApplicationPlugins\PointerTurn.bundle"
#define DotNetFile "windowsdesktop-runtime-8-win-x64.exe"
; Microsoft's permanent link to the newest .NET 8 Desktop Runtime, and the page a person can get it from.
#ifndef DotNetUrl
  #define DotNetUrl "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"
#endif
#define DotNetPage "https://dotnet.microsoft.com/download/dotnet/8.0"

[Setup]
; Identifies PointerTurn to Windows across versions, so a newer installer upgrades in place. Never change it.
AppId={{B3FE8729-DF31-47D4-A220-9225E28AE835}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} setup
UninstallDisplayName={#AppName} {#AppVersion}

DefaultDirName={autopf}\{#AppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableWelcomePage=no

; Asks whether to install for everyone on the PC (needs an administrator) or just this user.
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog commandline

; BricsCAD and AutoCAD are 64-bit only: use the 64-bit Program Files and the 64-bit registry.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; A CAD program that has the add-in loaded holds its files open. Setup lists such programs and
; offers to close them; it never restarts them.
CloseApplications=yes
RestartApplications=no

OutputDir={#OutputDir}
OutputBaseFilename=PointerTurn-{#AppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#StageDir}\BricsCAD\*"; DestDir: "{app}\BricsCAD"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#StageDir}\AutoCAD\PointerTurn.bundle\*"; DestDir: "{#BundleDir}"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

[InstallDelete]
; Files of an earlier version that this one no longer ships.
Type: filesandordirs; Name: "{app}\BricsCAD"
Type: filesandordirs; Name: "{#BundleDir}"

[Run]
Filename: "{tmp}\{#DotNetFile}"; Parameters: "/install /quiet /norestart"; StatusMsg: "Installing the Microsoft .NET 8 Desktop Runtime (BricsCAD needs it to run add-ins)..."; Check: DotNetDownloaded; Flags: waituntilterminated

[Code]
const
  BricsRoot = 'Software\Bricsys\BricsCAD';
  AutoCADRoot = 'SOFTWARE\Autodesk\AutoCAD';
  AppKeyName = 'PointerTurn';
  { The oldest BricsCAD the add-in supports, and the first that runs add-ins on .NET 8. }
  OldestBricsCAD = 24;
  FirstNet8BricsCAD = 26;
  { The oldest AutoCAD release the bundle supports: R24.0 is AutoCAD 2021. }
  OldestAutoCAD = 24;

var
  DownloadPage: TDownloadWizardPage;
  DotNetFetched: Boolean;
  { BricsCAD the add-in was registered with, for the last page: 'V25 (en_US), V26 (en_US)'. }
  RegisteredWith: String;

{ 'V25x64' gives 25. 0 when the name isn't a BricsCAD version key. }
function BricsCADVersionOf(const KeyName: String): Integer;
var
  S, Rest: String;
  I: Integer;
begin
  Result := 0;
  S := Uppercase(KeyName);
  if (Length(S) < 2) or (S[1] <> 'V') then
    Exit;

  I := 2;
  while (I <= Length(S)) and (S[I] >= '0') and (S[I] <= '9') do
    I := I + 1;

  Rest := Copy(S, I, Length(S));
  if (I > 2) and ((Rest = 'X64') or (Rest = '')) then
    Result := StrToIntDef(Copy(S, 2, I - 2), 0);
end;

{ BricsCAD keeps one key per installed language under each version: en_US, de_DE... }
function IsLanguageKey(const KeyName: String): Boolean;
begin
  Result := (Length(KeyName) = 5) and (KeyName[3] = '_');
end;

{ The build of the add-in a BricsCAD version loads. }
function LoaderFor(const Version: Integer): String;
begin
  if Version >= FirstNet8BricsCAD then
    Result := ExpandConstant('{app}\BricsCAD\net8\PointerTurn.BricsCAD.dll')
  else
    Result := ExpandConstant('{app}\BricsCAD\net48\PointerTurn.BricsCAD.dll');
end;

{ Adds 'V25x64\en_US' for every supported version and language of BricsCAD under a registry hive. }
procedure CollectBricsCAD(const RootKey: Integer; const Found: TStringList);
var
  Versions, Languages: TArrayOfString;
  I, J: Integer;
  Entry: String;
begin
  if not RegGetSubkeyNames(RootKey, BricsRoot, Versions) then
    Exit;

  for I := 0 to GetArrayLength(Versions) - 1 do
    if BricsCADVersionOf(Versions[I]) >= OldestBricsCAD then
      if RegGetSubkeyNames(RootKey, BricsRoot + '\' + Versions[I], Languages) then
        for J := 0 to GetArrayLength(Languages) - 1 do
          if IsLanguageKey(Languages[J]) then
          begin
            Entry := Versions[I] + '\' + Languages[J];
            if Found.IndexOf(Entry) < 0 then
              Found.Add(Entry);
          end;
end;

{ Every BricsCAD on the PC, whoever it was installed for, as 'V25x64\en_US'. }
procedure FindBricsCAD(const Found: TStringList);
begin
  CollectBricsCAD(HKLM, Found);
  CollectBricsCAD(HKCU, Found);
  Found.Sort;
end;

{ The newest BricsCAD version found, 0 for none. }
function NewestBricsCAD: Integer;
var
  Found: TStringList;
  I, Version: Integer;
begin
  Result := 0;
  Found := TStringList.Create;
  try
    FindBricsCAD(Found);
    for I := 0 to Found.Count - 1 do
    begin
      Version := BricsCADVersionOf(Copy(Found[I], 1, Pos('\', Found[I]) - 1));
      if Version > Result then
        Result := Version;
    end;
  finally
    Found.Free;
  end;
end;

{ The newest AutoCAD release found, as its major number (24 for R24.x, AutoCAD 2021 to 2024). 0 for none. }
function NewestAutoCAD: Integer;
var
  Releases: TArrayOfString;
  I, P, Major: Integer;
  S: String;
begin
  Result := 0;
  if not RegGetSubkeyNames(HKLM, AutoCADRoot, Releases) then
    Exit;

  for I := 0 to GetArrayLength(Releases) - 1 do
  begin
    S := Uppercase(Releases[I]);
    P := Pos('.', S);
    if (Length(S) > 1) and (S[1] = 'R') and (P > 2) then
    begin
      Major := StrToIntDef(Copy(S, 2, P - 2), 0);
      if Major > Result then
        Result := Major;
    end;
  end;
end;

{ Is Microsoft's .NET 8 Desktop Runtime (x64) installed? BricsCAD V26 and later run add-ins on it,
  and don't bring it themselves. AutoCAD 2025 and later install their own. }
function HasDotNet8: Boolean;
var
  Find: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), Find) then
  try
    repeat
      if (Find.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        Result := True;
    until Result or not FindNext(Find);
  finally
    FindClose(Find);
  end;
end;

function NeedsDotNet8: Boolean;
begin
  Result := ('{#ShipsBricsCADNet8}' = 'yes') and (NewestBricsCAD >= FirstNet8BricsCAD) and not HasDotNet8;
end;

{ [Run] installs the runtime only when it was fetched. }
function DotNetDownloaded: Boolean;
begin
  Result := DotNetFetched;
end;

procedure RegisterWithBricsCAD;
var
  Found: TStringList;
  I, Version: Integer;
  VersionKey, Language, Key, Loader: String;
begin
  RegisteredWith := '';
  Found := TStringList.Create;
  try
    FindBricsCAD(Found);
    for I := 0 to Found.Count - 1 do
    begin
      VersionKey := Copy(Found[I], 1, Pos('\', Found[I]) - 1);
      Language := Copy(Found[I], Pos('\', Found[I]) + 1, Length(Found[I]));
      Version := BricsCADVersionOf(VersionKey);
      Loader := LoaderFor(Version);
      if not FileExists(Loader) then
      begin
        Log(Format('BricsCAD V%d (%s): not registered, this installer has no add-in for it (%s).', [Version, Language, Loader]));
        Continue;
      end;

      Key := BricsRoot + '\' + Found[I] + '\Applications\' + AppKeyName;
      if RegWriteStringValue(HKA, Key, 'LOADER', Loader)
        and RegWriteDWordValue(HKA, Key, 'LOADCTRLS', 2)
        and RegWriteDWordValue(HKA, Key, 'MANAGED', 1)
        and RegWriteStringValue(HKA, Key, 'DESCRIPTION', 'PointerTurn (vehicle turning paths)') then
      begin
        Log(Format('BricsCAD V%d (%s): registered %s.', [Version, Language, Loader]));
        if RegisteredWith <> '' then
          RegisteredWith := RegisteredWith + ', ';
        RegisteredWith := RegisteredWith + Format('V%d (%s)', [Version, Language]);
      end
      else
        Log(Format('BricsCAD V%d (%s): could not write the registry key %s.', [Version, Language, Key]));
    end;
  finally
    Found.Free;
  end;
end;

{ Takes PointerTurn out of every BricsCAD it was registered with, and tidies up keys that
  registering made and nothing else uses. }
procedure UnregisterFromBricsCAD;
var
  Versions, Languages: TArrayOfString;
  I, J: Integer;
  LanguageKey: String;
begin
  if not RegGetSubkeyNames(HKA, BricsRoot, Versions) then
    Exit;

  for I := 0 to GetArrayLength(Versions) - 1 do
    if RegGetSubkeyNames(HKA, BricsRoot + '\' + Versions[I], Languages) then
    begin
      for J := 0 to GetArrayLength(Languages) - 1 do
      begin
        LanguageKey := BricsRoot + '\' + Versions[I] + '\' + Languages[J];
        if RegKeyExists(HKA, LanguageKey + '\Applications\' + AppKeyName) then
        begin
          RegDeleteKeyIncludingSubkeys(HKA, LanguageKey + '\Applications\' + AppKeyName);
          RegDeleteKeyIfEmpty(HKA, LanguageKey + '\Applications');
          RegDeleteKeyIfEmpty(HKA, LanguageKey);
        end;
      end;

      RegDeleteKeyIfEmpty(HKA, BricsRoot + '\' + Versions[I]);
    end;

  RegDeleteKeyIfEmpty(HKA, BricsRoot);
  RegDeleteKeyIfEmpty(HKA, 'Software\Bricsys');
end;

procedure InitializeWizard;
begin
  DotNetFetched := False;
  DownloadPage := CreateDownloadPage('Getting the Microsoft .NET 8 Desktop Runtime', 'BricsCAD V26 and later need it to run add-ins.', nil);
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := 'PointerTurn {#AppVersion} will be installed for:' + NewLine;
  if NewestBricsCAD > 0 then
    Result := Result + Space + 'BricsCAD: every version from V' + IntToStr(OldestBricsCAD) + ' found on this computer' + NewLine
  else
    Result := Result + Space + 'BricsCAD: not found (run this installer again after installing it)' + NewLine;

  Result := Result + Space + 'AutoCAD: 2021 and later' + NewLine;

  { The memo doesn't wrap, so the lines are kept short. }
  if NeedsDotNet8 then
  begin
    Result := Result + NewLine + 'BricsCAD V26 and later need the Microsoft .NET 8 Desktop Runtime' + NewLine + 'to run add-ins, and it is not installed.';
    if IsAdminInstallMode then
      Result := Result + ' It will be downloaded' + NewLine + 'and installed.' + NewLine
    else
      Result := Result + ' Installing it needs' + NewLine + 'an administrator.' + NewLine;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and NeedsDotNet8 and IsAdminInstallMode then
  begin
    DownloadPage.Clear;
    DownloadPage.Add('{#DotNetUrl}', '{#DotNetFile}', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
        DotNetFetched := True;
      except
        { No internet, or a proxy in the way: PointerTurn still installs, and the last page says what to do. }
        Log('The .NET 8 Desktop Runtime could not be downloaded: ' + GetExceptionMessage);
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    RegisterWithBricsCAD;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    UnregisterFromBricsCAD;
end;

{ The last page says what was done for each program, and what is left for the user to do. }
procedure CurPageChanged(CurPageID: Integer);
var
  Text, Gap: String;
  AutoCAD: Integer;
begin
  if CurPageID <> wpFinished then
    Exit;

  Gap := #13#10 + #13#10;
  Text := 'PointerTurn {#AppVersion} has been installed.' + Gap;

  if RegisteredWith <> '' then
    Text := Text + 'BricsCAD ' + RegisteredWith + ': PointerTurn loads the next time BricsCAD starts.'
  else if (NewestBricsCAD > 0) and not FileExists(LoaderFor(OldestBricsCAD)) and not FileExists(LoaderFor(FirstNet8BricsCAD)) then
    Text := Text + 'BricsCAD: this installer was built without the BricsCAD add-in, so nothing was installed for BricsCAD.'
  else if NewestBricsCAD > 0 then
    Text := Text + 'BricsCAD: PointerTurn could not be registered with the BricsCAD on this computer. Load it by hand with NETLOAD from ' + ExpandConstant('{app}\BricsCAD') + '.'
  else
    Text := Text + 'BricsCAD (V' + IntToStr(OldestBricsCAD) + ' or later) was not found. If you install it later, run this installer again.';

  if NeedsDotNet8 then
    Text := Text + ' BricsCAD V26 and later also need the Microsoft .NET 8 Desktop Runtime (x64), which is not installed: get it from {#DotNetPage} (choose ".NET Desktop Runtime", x64).';

  AutoCAD := NewestAutoCAD;
  Text := Text + Gap;
  if AutoCAD >= OldestAutoCAD then
    Text := Text + 'AutoCAD: PointerTurn loads the next time AutoCAD (2021 or later) starts.'
  else if AutoCAD > 0 then
    Text := Text + 'AutoCAD: PointerTurn needs AutoCAD 2021 or later, and loads by itself once that is installed.'
  else
    Text := Text + 'AutoCAD was not found. If you install AutoCAD 2021 or later, PointerTurn loads in it by itself.';

  Text := Text + Gap + 'Look for the PointerTurn tab on the ribbon, or type PTPATH.';
  WizardForm.FinishedLabel.Caption := Text;
  WizardForm.FinishedLabel.Height := WizardForm.FinishedLabel.Parent.ClientHeight - WizardForm.FinishedLabel.Top - ScaleY(12);
  Log(Text);
end;
