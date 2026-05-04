; ============================================================
; LambdaSQL — Inno Setup Script
; Installs: Server, Web UI, CLI
; Creates Windows Service for Server and Web
; Adds CLI to PATH
; Registers uninstaller in "Apps & Features"
; ============================================================

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef DistDir
  #define DistDir "..\dist"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist\installer"
#endif

#define MyAppName      "LambdaSQL"
#define MyAppPublisher "LambdaSQL Project"
#define MyAppURL       "https://github.com/your-org/lambdasql"
#define MyAppExeServer "lambdasql-server.exe"
#define MyAppExeWeb    "lambdasql-web.exe"
#define MyAppExeCli    "lambdasql.exe"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\LambdaSQL
DefaultGroupName=LambdaSQL
AllowNoIcons=yes
LicenseFile=..\..\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=LambdaSQL-Setup-{#MyAppVersion}
SetupIconFile=assets\icon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName=LambdaSQL {#MyAppVersion}
UninstallDisplayIcon={app}\server\{#MyAppExeServer}
CloseApplications=yes
RestartApplications=no
ChangesEnvironment=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "installservice_server"; Description: "Install LambdaSQL Server as a Windows Service"; GroupDescription: "Windows Services:"; Flags: checkedonce
Name: "installservice_web";    Description: "Install LambdaSQL Web UI as a Windows Service";  GroupDescription: "Windows Services:"; Flags: checkedonce
Name: "addtopath";             Description: "Add lambdasql CLI to PATH";                       GroupDescription: "Command Line:";    Flags: checkedonce
Name: "desktopicon";           Description: "Create desktop shortcut for Web UI";              GroupDescription: "Shortcuts:";

[Dirs]
Name: "{app}\data";   Permissions: everyone-full
Name: "{app}\server"
Name: "{app}\web"
Name: "{app}\cli"
Name: "{app}\logs";   Permissions: everyone-full

[Files]
; Server
Source: "{#DistDir}\win-x64\server\*"; DestDir: "{app}\server"; Flags: ignoreversion recursesubdirs

; Web UI
Source: "{#DistDir}\win-x64\web\*";    DestDir: "{app}\web";    Flags: ignoreversion recursesubdirs

; CLI
Source: "{#DistDir}\win-x64\cli\*";    DestDir: "{app}\cli";    Flags: ignoreversion recursesubdirs

; Config files
Source: "assets\server.json";          DestDir: "{app}";        Flags: onlyifdoesntexist
Source: "assets\web.json";             DestDir: "{app}";        Flags: onlyifdoesntexist

[Icons]
Name: "{group}\LambdaSQL Web UI";      Filename: "{app}\web\{#MyAppExeWeb}";    Parameters: "--urls=http://localhost:5000 --data={app}\data"
Name: "{group}\LambdaSQL CLI";         Filename: "{app}\cli\{#MyAppExeCli}";    Parameters: "--data {app}\data"
Name: "{group}\Uninstall LambdaSQL";   Filename: "{uninstallexe}"
Name: "{autodesktop}\LambdaSQL Web UI"; Filename: "{app}\web\{#MyAppExeWeb}";   Parameters: "--urls=http://localhost:5000 --data={app}\data"; Tasks: desktopicon

[Registry]
; Add CLI to PATH
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"; \
  ValueType: expandsz; ValueName: "Path"; \
  ValueData: "{olddata};{app}\cli"; \
  Check: NeedsAddPath('{app}\cli'); \
  Tasks: addtopath; \
  Flags: preservestringtype

[Run]
; Install Windows Services
Filename: "sc.exe"; Parameters: "create LambdaSQLServer binPath= ""{app}\server\{#MyAppExeServer} --data {app}\data"" start= auto DisplayName= ""LambdaSQL Server"""; \
  StatusMsg: "Installing LambdaSQL Server service..."; \
  Tasks: installservice_server; Flags: runhidden

Filename: "sc.exe"; Parameters: "start LambdaSQLServer"; \
  StatusMsg: "Starting LambdaSQL Server..."; \
  Tasks: installservice_server; Flags: runhidden

Filename: "sc.exe"; Parameters: "create LambdaSQLWeb binPath= ""{app}\web\{#MyAppExeWeb} --urls=http://localhost:5000 --data={app}\data"" start= auto DisplayName= ""LambdaSQL Web UI"""; \
  StatusMsg: "Installing LambdaSQL Web UI service..."; \
  Tasks: installservice_web; Flags: runhidden

Filename: "sc.exe"; Parameters: "start LambdaSQLWeb"; \
  StatusMsg: "Starting LambdaSQL Web UI..."; \
  Tasks: installservice_web; Flags: runhidden

; Open browser after install
Filename: "{#MyAppURL}"; Description: "Open LambdaSQL Web UI in browser"; \
  Flags: postinstall shellexec skipifsilent unchecked

[UninstallRun]
; Stop and remove services on uninstall
Filename: "sc.exe"; Parameters: "stop LambdaSQLServer";  Flags: runhidden; RunOnceId: "StopServer"
Filename: "sc.exe"; Parameters: "delete LambdaSQLServer"; Flags: runhidden; RunOnceId: "DelServer"
Filename: "sc.exe"; Parameters: "stop LambdaSQLWeb";     Flags: runhidden; RunOnceId: "StopWeb"
Filename: "sc.exe"; Parameters: "delete LambdaSQLWeb";   Flags: runhidden; RunOnceId: "DelWeb"

[Code]
// ── Check if path already contains the value ──────────────────────────────
function NeedsAddPath(Param: string): boolean;
var
  OrigPath: string;
begin
  if not RegQueryStringValue(
    HKEY_LOCAL_MACHINE,
    'SYSTEM\CurrentControlSet\Control\Session Manager\Environment',
    'Path', OrigPath)
  then begin
    Result := True;
    exit;
  end;
  Result := Pos(';' + Param + ';', ';' + OrigPath + ';') = 0;
end;

// ── Remove path entry on uninstall ────────────────────────────────────────
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Path, NewPath, Entry: string;
  P: Integer;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    Entry := ExpandConstant('{app}\cli');
    if RegQueryStringValue(
      HKEY_LOCAL_MACHINE,
      'SYSTEM\CurrentControlSet\Control\Session Manager\Environment',
      'Path', Path)
    then begin
      NewPath := StringReplace(Path, ';' + Entry, '', [rfReplaceAll, rfIgnoreCase]);
      NewPath := StringReplace(NewPath, Entry + ';', '', [rfReplaceAll, rfIgnoreCase]);
      NewPath := StringReplace(NewPath, Entry,       '', [rfReplaceAll, rfIgnoreCase]);
      RegWriteStringValue(
        HKEY_LOCAL_MACHINE,
        'SYSTEM\CurrentControlSet\Control\Session Manager\Environment',
        'Path', NewPath);
    end;
  end;
end;

// ── Ask user whether to keep data on uninstall ────────────────────────────
procedure CurUninstallStepChanged2(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    if MsgBox(
      'Do you want to delete all LambdaSQL data (databases, logs)?'#13#10 +
      'Click Yes to remove everything, No to keep your data.',
      mbConfirmation, MB_YESNO) = IDYES
    then begin
      DelTree(ExpandConstant('{app}\data'), True, True, True);
      DelTree(ExpandConstant('{app}\logs'), True, True, True);
    end;
  end;
end;

// Combine both uninstall handlers
procedure RealCurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  CurUninstallStepChanged(CurUninstallStep);
  CurUninstallStepChanged2(CurUninstallStep);
end;
