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
#ifndef WizardImagePath
  #error WizardImagePath must be supplied by Build-ReplayFoundryInstaller.ps1
#endif
#ifndef AdvancedPayloadMode
  #define AdvancedPayloadMode "Embedded"
#endif
#ifndef AdvancedCatalogPath
  #define AdvancedCatalogPath ""
#endif
#ifndef OfferAdvancedAi
  #define OfferAdvancedAi "0"
#endif
#ifndef YouTubeCredentialTargetName
  #error YouTubeCredentialTargetName must be supplied by Build-ReplayFoundryInstaller.ps1
#endif

#define MyAppName "Replay Foundry"
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
DisableWelcomePage=no
PrivilegesRequired=lowest
OutputDir={#InstallerOutputDir}
OutputBaseFilename=ReplayFoundry-{#MyAppVersion}-{#InstallerProfile}-win-x64-setup
SetupIconFile={#RepoRoot}\src\ReplayFoundry.Desktop\Assets\Icons\Application\ReplayFoundry.ico
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
WizardImageFile={#WizardImagePath}
WizardImageBackColor=#071014
WizardSmallImageFile={#WizardSmallImagePath}
WizardSmallImageBackColor=#071014
CloseApplications=yes
RestartApplications=no
ChangesAssociations=no
ChangesEnvironment=no
UsePreviousAppDir=yes
UsePreviousTasks=no
VersionInfoVersion={#MyAppFileVersion}
VersionInfoProductName={#MyAppName}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Replay Foundry local-first gaming clip editor
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

[Messages]
WelcomeLabel1=Install [name]
WelcomeLabel2=Turn gameplay recordings into clips ready to share. Replay Foundry works locally, so your source videos stay on this PC.%n%nSetup will guide you through the few choices that follow.
WizardLicense=Review the license
LicenseLabel=Review the license terms for Replay Foundry before continuing.
LicenseLabel3=Review the Replay Foundry license terms. You must accept them to continue.
WizardSelectTasks=Choose what to add
SelectTasksDesc=Shortcuts and local AI
SelectTasksLabel2=Choose any optional additions, then select Next.
WizardReady=Ready to install
ReadyLabel1=[name] is ready to install on this PC.
ReadyLabel2a=Review your choices, then select Install.
ReadyLabel2b=Select Install to continue.
WizardInstalling=Installing [name]
InstallingLabel=Keep this window open while Replay Foundry and its local tools are prepared.
FinishedHeadingLabel=[name] is ready
FinishedLabelNoIcons=Setup finished installing [name].
FinishedLabel=Setup finished installing [name]. Select Finish to open it.

[Tasks]
Name: "desktopicon"; Description: "Add a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
#if OfferAdvancedAi == "1"
Name: "advancedai"; Description: "Add Advanced AI (about 12.7 GB download)"; GroupDescription: "Optional local tools (visual AI needs a compatible 16 GB NVIDIA GPU):"; Flags: unchecked
#endif

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
#if OfferAdvancedAi == "1"
Source: "{#AdvancedCatalogPath}"; DestDir: "{tmp}\ReplayFoundryPacks"; DestName: "advanced-runtime-catalog.json"; Flags: deleteafterinstall
#endif

[Icons]
Name: "{autoprograms}\Replay Foundry"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Replay Foundry"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Replay Foundry"; Flags: nowait postinstall skipifsilent

[InstallDelete]
Type: files; Name: "{autoprograms}\ReplayFoundry.lnk"
Type: files; Name: "{autodesktop}\ReplayFoundry.lnk"
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
    WizardForm.WizardBitmapImage.Visible := False;
    WizardForm.WizardBitmapImage2.Visible := False;
    WizardForm.WizardSmallBitmapImage.Visible := False;
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
  Succeeded: Boolean;
begin
  WizardForm.StatusLabel.Caption := LabelText;
  WizardForm.FilenameLabel.Caption := '';
  ExitCode := -1;
  Succeeded := Exec(
    ExpandConstant('{app}\Tools\RuntimeInstaller\ReplayFoundry.RuntimeInstaller.exe'),
    Arguments,
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ExitCode);
  if not Succeeded or (ExitCode <> 0) then
  begin
    Log(LabelText + ' failed. Runtime installer exit code: ' + IntToStr(ExitCode));
    RaiseException(
      'Replay Foundry could not finish preparing its local tools. ' +
      'Nothing incomplete was kept. Run Setup again, or visit replayfoundry.com/support.');
  end;
end;

procedure RetainInstaller(const Destination: String);
begin
  { Repair may be launched from the retained installer itself. }
  if CompareText(ExpandConstant('{srcexe}'), Destination) = 0 then exit;
  if not CopyFile(ExpandConstant('{srcexe}'), Destination, False) then
    RaiseException('Unable to retain the current Replay Foundry installer for repair.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  StoreRoot: String;
begin
  if CurStep <> ssPostInstall then exit;
  WizardForm.PageNameLabel.Caption := 'Finishing setup';
  WizardForm.PageDescriptionLabel.Caption :=
    'Preparing Replay Foundry''s local tools. This can take several minutes.';
  WizardForm.ProgressGauge.Style := npbstMarquee;
  try
    StoreRoot := ExpandConstant('{localappdata}\ReplayFoundry\R');
    RequireRuntimeInstallerSuccess(
      'repair --source "' + ExpandConstant('{tmp}\ReplayFoundryPacks\replayfoundry-media-tools.zip') + '" --store-root "' + StoreRoot + '"',
      'Preparing video tools');
#if InstallerProfile == "Advanced"
  #if AdvancedPayloadMode == "Embedded"
    RequireRuntimeInstallerSuccess('repair --source "' + ExpandConstant('{tmp}\ReplayFoundryPacks\replayfoundry-silero-vad.zip') + '" --store-root "' + StoreRoot + '"', 'Preparing speech timing');
    RequireRuntimeInstallerSuccess('repair --source "' + ExpandConstant('{tmp}\ReplayFoundryPacks\replayfoundry-whisper-cpp.zip') + '" --store-root "' + StoreRoot + '"', 'Preparing captions');
    RequireRuntimeInstallerSuccess('repair --source "' + ExpandConstant('{tmp}\ReplayFoundryPacks\replayfoundry-whisper-small-multilingual.zip') + '" --store-root "' + StoreRoot + '"', 'Preparing language support');
    RequireRuntimeInstallerSuccess('repair --source "' + ExpandConstant('{tmp}\ReplayFoundryPacks\replayfoundry-qwen3-vl-runtime.zip') + '" --store-root "' + StoreRoot + '"', 'Preparing visual analysis');
    RequireRuntimeInstallerSuccess('repair --source "' + ExpandConstant('{tmp}\ReplayFoundryPacks\replayfoundry-qwen3-vl-4b-instruct.zip') + '" --store-root "' + StoreRoot + '"', 'Preparing local AI');
  #else
    RequireRuntimeInstallerSuccess(
      'install-catalog --catalog "' + ExpandConstant('{tmp}\ReplayFoundryPacks\advanced-runtime-catalog.json') + '" --store-root "' + StoreRoot + '"',
      'Downloading and preparing Advanced AI');
  #endif
#endif
#if OfferAdvancedAi == "1"
    if WizardIsTaskSelected('advancedai') then
    begin
      RequireRuntimeInstallerSuccess(
        'install-catalog --catalog "' + ExpandConstant('{tmp}\ReplayFoundryPacks\advanced-runtime-catalog.json') + '" --store-root "' + StoreRoot + '"',
        'Downloading and preparing Advanced AI');
    end;
#endif
    RequireRuntimeInstallerSuccess(
      'prune-inactive --store-root "' + StoreRoot + '"',
      'Finishing setup');
    if not ForceDirectories(ExpandConstant('{localappdata}\ReplayFoundry\Installers')) then
    begin
      RaiseException('Unable to create the retained installer directory.');
    end;
    RetainInstaller(ExpandConstant('{localappdata}\ReplayFoundry\Installers\ReplayFoundry-{#InstallerProfile}-Setup.exe'));
    RetainInstaller(ExpandConstant('{localappdata}\ReplayFoundry\Installers\ReplayFoundry-Setup.exe'));
  finally
    WizardForm.ProgressGauge.Style := npbstNormal;
  end;
end;
