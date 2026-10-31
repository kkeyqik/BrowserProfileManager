; Inno Setup 6 script for Browser Profile Manager.
; Built by build.ps1:  ISCC.exe /DAppVersion=1.0.0 installer\BrowserProfileManager.iss
; Installs for the current user by default (no admin); the wizard offers "all users" too.

#define AppName "Browser Profile Manager"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppExe "BrowserProfileManager.exe"
#define SourceDir "..\artifacts\publish"

[Setup]
AppId={{8F3C2B1E-5A4D-4E7B-9C21-6D0F4A9B7E15}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts
OutputBaseFilename=BrowserProfileManager-Setup-{#AppVersion}
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "{#SourceDir}\NOTICE"; DestDir: "{app}"; DestName: "NOTICE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
// On uninstall, offer to remove the app's own data (settings, change log, backups made before changes).
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
  begin
    DataDir := ExpandConstant('{localappdata}\BrowserProfileManager');
    if DirExists(DataDir) then
      if MsgBox('Also delete the app''s settings, change log and backups?' + #13#10#13#10 + DataDir,
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
