; DorkNet launcher — Windows installer.
; Produces a per-user installer that drops dorknet.exe under
; %LocalAppData%\Programs\DorkNet, adds Start menu + desktop shortcuts,
; and registers an uninstaller. No admin rights required.
;
; Build: pwsh -File installer\build-installer.ps1

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

#define AppName "DorkNet"
#define AppPublisher "DorkSquadRR"
#define AppURL "https://github.com/DorkSquadRR/DorkNet"
#define AppExeName "dorknet.exe"
#define PublishDir "..\launcher\bin\Release\net9.0-windows\win-x64\publish"

[Setup]
AppId={{8C2C7E1A-6B4F-4F8D-9C0B-DorkNet000001}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}/releases
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=out
OutputBaseFilename=dorknet-setup-{#AppVersion}
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
MinVersion=10.0.17763
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=force
RestartApplications=no
; Skip the welcome page — single-file installer, the wizard already
; tells the user what's happening.
DisableWelcomePage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "autorunupdates"; Description: "Check for updates on launch"; GroupDescription: "Updates:"

[Files]
Source: "{#PublishDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
; Bundle the tunnelto helper if it's been built next to the launcher.
; The launcher auto-falls-back to PATH lookup when not bundled, so this
; is purely a convenience for users who don't already have it installed.
Source: "{#PublishDir}\tunnelto.exe"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Mark the install location + version so the launcher's updater can
; find where to swap the new exe into.
Root: HKCU; Subkey: "Software\DorkNet"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\DorkNet"; ValueType: string; ValueName: "Version"; ValueData: "{#AppVersion}"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\DorkNet"; ValueType: dword; ValueName: "AutoUpdate"; ValueData: "1"; Tasks: autorunupdates; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\DorkNet"; ValueType: dword; ValueName: "AutoUpdate"; ValueData: "0"; Tasks: not autorunupdates; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Leave %AppData%\DorkNet alone — that's user state (Photon AppIds,
; saved server name, etc.) and the user's worlds live in dorknet.db.
; Just clean the program files dir.
Type: filesandordirs; Name: "{app}"
