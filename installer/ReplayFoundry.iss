#ifndef MyAppVersion
  #error MyAppVersion must be supplied by Build-ReplayFoundryInstaller.ps1
#endif
#ifndef MyAppFileVersion
  #error MyAppFileVersion must be supplied by Build-ReplayFoundryInstaller.ps1
#endif
#ifndef PublishDir
  #error PublishDir must be supplied by Build-ReplayFoundryInstaller.ps1
#endif
#ifndef RepoRoot
  #error RepoRoot must be supplied by Build-ReplayFoundryInstaller.ps1
#endif
#ifndef InstallerOutputDir
  #error InstallerOutputDir must be supplied by Build-ReplayFoundryInstaller.ps1
#endif
#ifndef InstallerProfile
  #error InstallerProfile must be Base or Advanced
#endif
#ifndef RuntimePackBuildRoot
  #error RuntimePackBuildRoot must be supplied
#endif
#ifndef WizardBackImagePath
  #error WizardBackImagePath must be supplied by Build-ReplayFoundryInstaller.ps1
#endif
#ifndef WizardSmallImagePath
  #error WizardSmallImagePath must be supplied by Build-ReplayFoundryInstaller.ps1
#endif
#ifndef AdvancedPayloadMode
  #define AdvancedPayloadMode "Embedded"
#endif
#ifndef AdvancedCatalogPath
  #define AdvancedCatalogPath ""
#endif
#ifndef YouTubeCredentialTargetName
  #error YouTubeCredentialTargetName must be supplied by Build-ReplayFoundryInstaller.ps1
#endif

#define MyAppName "ReplayFoundry"
#define MyAppPublisher "Expired Soda Studios LLC"
#define MyAppExeName "ReplayFoundry.Desktop.exe"

[Setup]
AppId={{5E72F4F1-3E1C-4F38-AFCF-837C0BDAE37C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://replayfoundry.com/
AppSupportURL=https://replayfoundry.com/support
AppUpdatesURL=https://replayfoundry.com/download
DefaultDirName={localappdata}\Programs\Replay Foundry
DefaultGroupName=Replay Foundry
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#InstallerOutputDir}
OutputBaseFilename=ReplayFoundry-{#MyAppVersion}-{#InstallerProfile}-win-x64-setup
SetupIconFile={#RepoRoot}\ReplayFoundry.Desktop\Assets\Icons\Application\ReplayFoundry.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
LicenseFile={#RepoRoot}\LICENSE.txt
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2/ultra64
SolidCompression=yes
; The generated images use the canonical ReplayFoundry mark. Inno disables
; its custom dark style automatically for Windows high-contrast themes; the
; InitializeWizard fallback below also removes the decorative background.
WizardStyle=modern dark windows11 hidebevels includetitlebar
WizardSizePercent=120,120
WizardKeepAspectRatio=yes
WizardImageStretch=yes
WizardBackColor=#071014
WizardBackImageFile={#WizardBackImagePath}
WizardBackImageOpacity=255
WizardImageFile=
WizardSmallImageFile={#WizardSmallImagePath}
WizardSmallImageBackColor=#071014
CloseApplications=yes
RestartApplications=no
ChangesAssociations=no
ChangesEnvironment=no
UsePreviousAppDir=yes
VersionInfoVersion={#MyAppFileVersion}
VersionInfoProductName={#MyAppName}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=ReplayFoundry local-first gaming clip editor
AppCopyright=Copyright (C) 2026 Expired Soda Studios LLC
#ifdef ReplayFoundrySignToolName
SignTool={#ReplayFoundrySignToolName}
SignedUninstaller=yes
SignToolRetryCount=3
SignToolMinimumTimeBetween=1000
#else
SignedUninstaller=no
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RuntimePackBuildRoot}\archives\replayfoundry-media-tools.zip"; DestDir: "{tmp}\ReplayFoundryPacks"; Flags: deleteafterinstall
#if InstallerProfile == "Advanced"
  #if AdvancedPayloadMode == "Embedded"
Source: "{#RuntimePackBuildRoot}\archives\replayfoundry-silero-vad.zip"; DestDir: "{tmp}\ReplayFoundryPacks"; Flags: deleteafterinstall
Source: "{#RuntimePackBuildRoot}\archives\replayfoundry-whisper-cpp.zip"; DestDir: "{tmp}\ReplayFoundryPacks"; Flags: deleteafterinstall
Source: "{#RuntimePackBuildRoot}\archives\replayfoundry-whisper-small-multilingual.zip"; DestDir: "{tmp}\ReplayFoundryPacks"; Flags: deleteafterinstall
Source: "{#RuntimePackBuildRoot}\archives\replayfoundry-qwen3-vl-runtime.zip"; DestDir: "{tmp}\ReplayFoundryPacks"; Flags: deleteafterinstall
Source: "{#RuntimePackBuildRoot}\archives\replayfoundry-qwen3-vl-4b-instruct.zip"; DestDir: "{tmp}\ReplayFoundryPacks"; Flags: deleteafterinstall
  #else
Source: "{#AdvancedCatalogPath}"; DestDir: "{tmp}\ReplayFoundryPacks"; DestName: "advanced-runtime-catalog.json"; Flags: deleteafterinstall
  #endif
#endif

[Icons]
Name: "{autoprograms}\ReplayFoundry"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\ReplayFoundry"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Replay Foundry"; Flags: nowait postinstall skipifsilent

[InstallDelete]
Type: files; Name: "{autoprograms}\Replay Foundry.lnk"
Type: files; Name: "{autodesktop}\Replay Foundry.lnk"

[UninstallDelete]
; These are ReplayFoundry-owned roots only. Finished videos live under the
; user's Videos folder (or a custom export folder) and are intentionally kept.
Type: filesandordirs; Name: "{localappdata}\ReplayFoundry"
Type: filesandordirs; Name: "{userappdata}\ReplayFoundry"
Type: filesandordirs; Name: "{%TEMP|{localappdata}\Temp}\ReplayFoundry"
Type: filesandordirs; Name: "{%TEMP|{localappdata}\Temp}\ReplayFoundry-RuntimeDownloads"
Type: filesandordirs; Name: "{%TMP|{localappdata}\Temp}\ReplayFoundry"
Type: filesandordirs; Name: "{%TMP|{localappdata}\Temp}\ReplayFoundry-RuntimeDownloads"
Type: files; Name: "{localappdata}\CrashDumps\ReplayFoundry.Desktop.exe*.dmp"

[Code]
const
  CredentialTypeGeneric = 1;
  ErrorNotFound = 1168;

function CredDelete(
  TargetName: String;
  CredentialType: Cardinal;
  Flags: Cardinal): Boolean;
  external 'CredDeleteW@advapi32.dll stdcall';

procedure InitializeWizard;
begin
  if HighContrastActive then
  begin
    WizardSetBackImage([], True, True, 255);
    WizardForm.Color := clWindow;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ErrorCode: Integer;
begin
  if CurUninstallStep <> usUninstall then exit;

  if not CredDelete('{#YouTubeCredentialTargetName}', CredentialTypeGeneric, 0) then
  begin
    ErrorCode := DLLGetLastError;
    if ErrorCode <> ErrorNotFound then
    begin
      Log('ReplayFoundry could not remove its stored YouTube credential. Windows error: ' +
        IntToStr(ErrorCode));
    end;
  end;
end;

procedure RequireRuntimeInstallerSuccess(const Arguments, LabelText: String);
var
  ExitCode: Integer;
begin
  WizardForm.StatusLabel.Caption := LabelText;
  if not Exec(
    ExpandConstant('{app}\Tools\RuntimeInstaller\ReplayFoundry.RuntimeInstaller.exe'),
    Arguments,
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ExitCode) or (ExitCode <> 0) then
  begin
    RaiseException(LabelText + ' failed. No incomplete runtime pack was activated. Exit code: ' + IntToStr(ExitCode));
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  StoreRoot: String;
begin
  if CurStep <> ssPostInstall then exit;
  StoreRoot := ExpandConstant('{localappdata}\ReplayFoundry\R');
  RequireRuntimeInstallerSuccess(
    'install --source "' + ExpandConstant('{tmp}\ReplayFoundryPacks\replayfoundry-media-tools.zip') + '" --store-root "' + StoreRoot + '"',
    'Installing verified Base media tools');
#if InstallerProfile == "Advanced"
  #if AdvancedPayloadMode == "Embedded"
  RequireRuntimeInstallerSuccess('install --source "' + ExpandConstant('{tmp}\ReplayFoundryPacks\replayfoundry-silero-vad.zip') + '" --store-root "' + StoreRoot + '"', 'Installing local speech timing');
  RequireRuntimeInstallerSuccess('install --source "' + ExpandConstant('{tmp}\ReplayFoundryPacks\replayfoundry-whisper-cpp.zip') + '" --store-root "' + StoreRoot + '"', 'Installing local transcription runtime');
  RequireRuntimeInstallerSuccess('install --source "' + ExpandConstant('{tmp}\ReplayFoundryPacks\replayfoundry-whisper-small-multilingual.zip') + '" --store-root "' + StoreRoot + '"', 'Installing multilingual transcription model');
  RequireRuntimeInstallerSuccess('install --source "' + ExpandConstant('{tmp}\ReplayFoundryPacks\replayfoundry-qwen3-vl-runtime.zip') + '" --store-root "' + StoreRoot + '"', 'Installing Qwen local runtime');
  RequireRuntimeInstallerSuccess('install --source "' + ExpandConstant('{tmp}\ReplayFoundryPacks\replayfoundry-qwen3-vl-4b-instruct.zip') + '" --store-root "' + StoreRoot + '"', 'Installing Qwen3-VL model');
  #else
  RequireRuntimeInstallerSuccess(
    'install-catalog --catalog "' + ExpandConstant('{tmp}\ReplayFoundryPacks\advanced-runtime-catalog.json') + '" --store-root "' + StoreRoot + '"',
    'Downloading and installing verified Advanced AI packs');
  #endif
#endif
  RequireRuntimeInstallerSuccess(
    'prune-inactive --store-root "' + StoreRoot + '"',
    'Removing inactive runtime packs');
  if not ForceDirectories(ExpandConstant('{localappdata}\ReplayFoundry\Installers')) then
  begin
    RaiseException('Unable to create the retained installer directory.');
  end;
  if not CopyFile(ExpandConstant('{srcexe}'), ExpandConstant('{localappdata}\ReplayFoundry\Installers\ReplayFoundry-{#InstallerProfile}-Setup.exe'), False) then
  begin
    RaiseException('Unable to retain the current ReplayFoundry installer for repair.');
  end;
end;
