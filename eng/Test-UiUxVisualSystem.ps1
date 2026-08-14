param()

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$failures = [System.Collections.Generic.List[string]]::new()

function Add-Failure { param([string]$Message) $failures.Add($Message) }
function Read-RepoText { param([string]$Path) Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot $Path) }
function Require-Path { param([string]$Path) if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $Path))) { Add-Failure "Required UI-03 path is missing: $Path" } }
function Require-Pattern { param([string]$Path, [string]$Pattern, [string]$Message) if ((Read-RepoText $Path) -notmatch $Pattern) { Add-Failure $Message } }

$requiredPaths = @(
    "ReplayFoundry.Desktop/Assets/Icons/Application/ReplayFoundry.ico",
    "ReplayFoundry.Desktop/Assets/Branding/ReplayFoundry-App-Icon-1024.png",
    "ReplayFoundry.Desktop/Assets/Branding/favicon.svg",
    "ReplayFoundry.Desktop/Assets/Branding/README.md",
    "eng/New-ReplayFoundryBrandAssets.ps1",
    "eng/New-ReplayFoundryInstallerBranding.ps1",
    "eng/Test-InstallerBranding.ps1",
    "ReplayFoundry.Desktop/Resources/Theme/Colors.xaml",
    "ReplayFoundry.Desktop/Resources/Theme/Brushes.xaml",
    "ReplayFoundry.Desktop/Resources/Theme/Iconography.xaml",
    "ReplayFoundry.Desktop/Resources/Theme/Motion.xaml",
    "ReplayFoundry.Desktop/Resources/Controls/ButtonStyles.xaml",
    "ReplayFoundry.Desktop/Resources/Controls/InputStyles.xaml",
    "ReplayFoundry.Desktop/Resources/Controls/SelectionStyles.xaml",
    "ReplayFoundry.Desktop/Resources/Controls/ScrollStyles.xaml",
    "ReplayFoundry.Desktop/Resources/Controls/MenuPopupStyles.xaml",
    "ReplayFoundry.Desktop/Resources/Controls/RangeProgressStyles.xaml",
    "ReplayFoundry.Desktop/Resources/Controls/ValidationStyles.xaml",
    "ReplayFoundry.Desktop/Resources/Controls/WindowChromeStyles.xaml",
    "ReplayFoundry.Desktop/Platform/Dialogs/MediaRightsConfirmationWindow.xaml",
    "ReplayFoundry.Desktop/Presentation/Controls/IconPath.cs",
    "ReplayFoundry.Desktop/Presentation/Controls/AudioSignalWaveform.cs")
foreach ($path in $requiredPaths) { Require-Path $path }

$applicationIconPath = Join-Path $repositoryRoot "ReplayFoundry.Desktop/Assets/Icons/Application/ReplayFoundry.ico"
if (Test-Path -LiteralPath $applicationIconPath) {
    $iconBytes = [System.IO.File]::ReadAllBytes($applicationIconPath)
    if ($iconBytes.Length -lt 6 -or
        [BitConverter]::ToUInt16($iconBytes, 0) -ne 0 -or
        [BitConverter]::ToUInt16($iconBytes, 2) -ne 1) {
        Add-Failure "ReplayFoundry.ico is not a valid Windows icon container."
    }
    else {
        $iconEntryCount = [BitConverter]::ToUInt16($iconBytes, 4)
        $iconDirectoryLength = 6 + (16 * $iconEntryCount)
        if ($iconEntryCount -eq 0 -or $iconDirectoryLength -gt $iconBytes.Length) {
            Add-Failure "ReplayFoundry.ico has an invalid or empty image directory."
        }
        else {
            $iconSizes = @()
            $pngSignature = [byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)
            for ($index = 0; $index -lt $iconEntryCount; $index++) {
                $entryOffset = 6 + (16 * $index)
                $width = if ($iconBytes[$entryOffset] -eq 0) { 256 } else { [int]$iconBytes[$entryOffset] }
                $height = if ($iconBytes[$entryOffset + 1] -eq 0) { 256 } else { [int]$iconBytes[$entryOffset + 1] }
                $bitsPerPixel = [BitConverter]::ToUInt16($iconBytes, $entryOffset + 6)
                $resourceSize = [BitConverter]::ToUInt32($iconBytes, $entryOffset + 8)
                $resourceOffset = [BitConverter]::ToUInt32($iconBytes, $entryOffset + 12)
                $resourceEnd = [uint64]$resourceOffset + [uint64]$resourceSize
                if ($width -ne $height -or $bitsPerPixel -ne 32 -or
                    $resourceSize -lt $pngSignature.Length -or
                    $resourceOffset -lt $iconDirectoryLength -or
                    $resourceEnd -gt $iconBytes.Length) {
                    Add-Failure "ReplayFoundry.ico entry $index has invalid dimensions, depth, or bounds."
                    continue
                }
                for ($signatureIndex = 0; $signatureIndex -lt $pngSignature.Length; $signatureIndex++) {
                    if ($iconBytes[$resourceOffset + $signatureIndex] -ne $pngSignature[$signatureIndex]) {
                        Add-Failure "ReplayFoundry.ico entry $index is not a valid embedded PNG frame."
                        break
                    }
                }
                $iconSizes += $width
            }
            foreach ($requiredSize in @(16, 20, 24, 32, 40, 48, 64, 128, 256)) {
                if ($iconSizes -notcontains $requiredSize) {
                    Add-Failure "ReplayFoundry.ico is missing the $($requiredSize)x$($requiredSize) frame required by Windows chrome, taskbar, or app switching."
                }
            }
            if (($iconSizes | Select-Object -Unique).Count -ne $iconSizes.Count) {
                Add-Failure "ReplayFoundry.ico contains duplicate frame sizes."
            }
        }
    }
}

$desktopProject = Read-RepoText "ReplayFoundry.Desktop/ReplayFoundry.Desktop.csproj"
$installerDefinition = Read-RepoText "installer/ReplayFoundry.iss"
if ($desktopProject -notmatch '<ApplicationIcon>Assets\\Icons\\Application\\ReplayFoundry\.ico</ApplicationIcon>') {
    Add-Failure "Desktop publishing must point ApplicationIcon at the canonical multi-size ReplayFoundry.ico."
}
if ($installerDefinition -notmatch 'SetupIconFile=\{#RepoRoot\}\\ReplayFoundry\.Desktop\\Assets\\Icons\\Application\\ReplayFoundry\.ico') {
    Add-Failure "The Windows setup executable must use the same canonical ReplayFoundry.ico as Desktop."
}
$installerBranding = Read-RepoText "eng/New-ReplayFoundryInstallerBranding.ps1"
foreach ($brandingPattern in @(
    'ReplayFoundry-App-Icon-1024\.png',
    '#071014', '#1F9DC4', '#59CAF0', '#FFC75E',
    '1988', '1440', 'installer-branding-manifest\.json')) {
    if ($installerBranding -notmatch $brandingPattern) {
        Add-Failure "Installer branding generator is missing canonical visual contract: $brandingPattern"
    }
}
foreach ($installerPattern in @(
    'WizardStyle=modern dark windows11 hidebevels includetitlebar',
    'WizardBackImageFile=\{#WizardBackImagePath\}',
    'WizardSmallImageFile=\{#WizardSmallImagePath\}',
    'HighContrastActive')) {
    if ($installerDefinition -notmatch $installerPattern) {
        Add-Failure "Installer definition is missing branded/high-contrast presentation: $installerPattern"
    }
}

$app = Read-RepoText "ReplayFoundry.Desktop/App.xaml"
foreach ($dictionary in @(
    "Colors.xaml", "Brushes.xaml", "Iconography.xaml", "Motion.xaml",
    "ButtonStyles.xaml", "InputStyles.xaml", "SelectionStyles.xaml",
    "ScrollStyles.xaml", "MenuPopupStyles.xaml", "RangeProgressStyles.xaml",
    "ValidationStyles.xaml", "WindowChromeStyles.xaml")) {
    if ($app -notmatch [regex]::Escape("Resources/Theme/$dictionary") -and
        $app -notmatch [regex]::Escape("Resources/Controls/$dictionary")) {
        Add-Failure "App.xaml does not merge required dictionary $dictionary."
    }
}
if ($app -match "DesignTime") { Add-Failure "Production App.xaml contains a design-time reference." }

$mainWindow = Read-RepoText "ReplayFoundry.Desktop/Shell/MainWindow.xaml"
$mainWindowCode = Read-RepoText "ReplayFoundry.Desktop/Shell/MainWindow.xaml.cs"
$chromeInteraction = Read-RepoText "ReplayFoundry.Desktop/Shell/Windowing/WindowChromeInteraction.cs"
Require-Pattern "ReplayFoundry.Desktop/Shell/MainWindow.xaml" 'WindowStyle="None"' "Custom window chrome must remove the default frame."
Require-Pattern "ReplayFoundry.Desktop/Shell/MainWindow.xaml" 'AllowsTransparency="False"' "Custom window chrome must preserve native composition."
Require-Pattern "ReplayFoundry.Desktop/Shell/MainWindow.xaml" 'WindowChrome\.WindowChrome' "MainWindow must apply the WindowChrome resource."
Require-Pattern "ReplayFoundry.Desktop/Shell/MainWindow.xaml" 'WindowChrome\.IsHitTestVisibleInChrome' "Title-bar hit testing must be explicit."
Require-Pattern "ReplayFoundry.Desktop/Shell/MainWindow.xaml" 'Content="\{Binding CurrentWorkspace\}"' "The shell must retain one workspace host."
foreach ($caption in @("CaptionMinimizeButton", "CaptionMaximizeButton", "CaptionCloseButton")) {
    if ($mainWindow -notmatch [regex]::Escape($caption) -or $mainWindow -notmatch 'AutomationProperties\.Name="[^"]*window') { Add-Failure "Caption button automation coverage is incomplete: $caption" }
}
foreach ($nativeCommand in @("MinimizeWindow", "MaximizeWindow", "RestoreWindow", "CloseWindow", "ShowSystemMenu")) {
    if ($chromeInteraction -notmatch [regex]::Escape("SystemCommands.$nativeCommand")) { Add-Failure "Native caption behavior is missing SystemCommands.$nativeCommand." }
}
if ($mainWindowCode -match 'CurrentWorkspace|CurrentDestination|ShellDestination|\.Content\s*=|\.Visibility\s*=') { Add-Failure "MainWindow code-behind crosses the shell MVVM boundary." }

$scroll = Read-RepoText "ReplayFoundry.Desktop/Resources/Controls/ScrollStyles.xaml"
$input = Read-RepoText "ReplayFoundry.Desktop/Resources/Controls/InputStyles.xaml"
$selection = Read-RepoText "ReplayFoundry.Desktop/Resources/Controls/SelectionStyles.xaml"
$chromeTheme = Read-RepoText "ReplayFoundry.Desktop/Resources/Controls/WindowChromeStyles.xaml"
$popup = Read-RepoText "ReplayFoundry.Desktop/Resources/Controls/MenuPopupStyles.xaml"
$range = Read-RepoText "ReplayFoundry.Desktop/Resources/Controls/RangeProgressStyles.xaml"
$buttons = Read-RepoText "ReplayFoundry.Desktop/Resources/Controls/ButtonStyles.xaml"
foreach ($part in @("PART_Track", "Thumb", "RepeatButton")) { if ($scroll -notmatch $part) { Add-Failure "Scroll theme is missing required template part $part." } }
foreach ($part in @("PART_Popup", "PART_ContentHost")) { if ($input -notmatch $part) { Add-Failure "Input theme is missing required ComboBox/TextBox part $part." } }
foreach ($control in @("CheckBox", "RadioButton", "ListBoxItem", "TabControl", "GridSplitter")) { if ($selection -notmatch [regex]::Escape($control)) { Add-Failure "Selection theme is missing $control." } }
foreach ($control in @("ContextMenu", "MenuItem", "ToolTip")) { if ($popup -notmatch $control) { Add-Failure "Popup theme is missing $control." } }
foreach ($control in @("Slider", "ProgressBar", "PART_Indicator", "RangeThumb")) { if ($range -notmatch $control) { Add-Failure "Range/progress theme is missing $control." } }
foreach ($kineticContract in @(
    @{ Text = $input; Pattern = 'Control\.InlineSelectorComboBox[\s\S]*Control\.InlineSearchTextBox'; Message = 'Shared inline selector and search styles are missing.' },
    @{ Text = $input; Pattern = 'DropDownCaret[\s\S]*Brush\.KineticGlow'; Message = 'Shared ComboBox lacks its stateful caret or bounded popup glow.' },
    @{ Text = $selection; Pattern = 'Control\.PreferenceChoice[\s\S]*Control\.CanvasRailListBoxItem'; Message = 'Shared preference choices or bounded navigation selection are missing.' },
    @{ Text = $buttons; Pattern = 'SystemParameters\.ClientAreaAnimation[\s\S]*Control\.GhostButton'; Message = 'Ghost actions or reduced-motion-aware button lift are missing.' },
    @{ Text = (Read-RepoText "ReplayFoundry.Desktop/Resources/Controls/CardStyles.xaml"); Pattern = 'Control\.CanvasPane[\s\S]*Control\.KineticMediaCard'; Message = 'Tonal panes or kinetic media cards are missing.' },
    @{ Text = $range; Pattern = 'ThumbSurface[\s\S]*IsDragging[\s\S]*IsKeyboardFocusWithin'; Message = 'Slider thumb feedback must remain visible for pointer drag and keyboard focus.' })) {
    if ($kineticContract.Text -notmatch $kineticContract.Pattern) { Add-Failure $kineticContract.Message }
}
if ($buttons -notmatch 'x:Name="KineticAura"[\s\S]*BlurEffect Radius="7"' -or
    $buttons -notmatch 'Storyboard\.TargetName="HoverScale"[\s\S]*To="1\.01"' -or
    $buttons -notmatch 'Storyboard\.TargetName="PressScale"[\s\S]*To="0\.9703"' -or
    $buttons -notmatch 'Storyboard\.TargetName="PressAuraGate"') {
    Add-Failure 'Shared buttons must retain their full-contour semantic aura, hover lift, and tactile press compression.'
}
if ($buttons -match 'x:Name="AccentLeak"') {
    Add-Failure 'Shared buttons must not restore the detached underline hover treatment.'
}
if ($input -match 'x:Name="(?:FocusRail|OpenRail)"') {
    Add-Failure 'Text inputs and ComboBoxes must use their complete bounded surface for interaction feedback, not partial underline rails.'
}
Require-Pattern "ReplayFoundry.Desktop/Features/Library/Sections/LibraryFilterBarView.xaml" 'Control\.InlineSearchTextBox[\s\S]*Control\.InlineSelectorComboBox' "Library filters must reuse the shared borderless editorial controls."
Require-Pattern "ReplayFoundry.Desktop/Features/Library/Sections/LibraryCategoryRailView.xaml" 'Control\.CanvasRailListBoxItem' "Library category selection must reuse the shared bounded selection surface."
Require-Pattern "ReplayFoundry.Desktop/Features/Generate/CompositionReview/CompositionRegionEditor.xaml" 'CompositionReview\.CropMark' "Layout Review selected regions must retain precise crop-mark corners."
Require-Pattern "ReplayFoundry.Desktop/Features/Publish/Sections/PublishCalendarView.xaml" 'Control\.CanvasPane[\s\S]*Brush\.KineticGlowSoft' "Publish scheduling must preserve its tonal pane and cyan selected-day cues."
$publishCalendar = Read-RepoText "ReplayFoundry.Desktop/Features/Publish/Sections/PublishCalendarView.xaml"
if ($publishCalendar -notmatch 'IsSelected[\s\S]*Property="BorderBrush"[\s\S]*Brush\.StatusInfo' -or
    $publishCalendar -match 'Height="1\.5"') {
    Add-Failure 'Publish calendar selection must use the complete day-cell border, not partial cyan corner marks.'
}
Require-Pattern "ReplayFoundry.Desktop/Features/Publish/Sections/PublishLibraryBrowserView.xaml" 'Control\.InlineSearchTextBox[\s\S]*Control\.InlineSelectorComboBox' "Publish Library filters must reuse shared editorial controls."
Require-Pattern "ReplayFoundry.Desktop/Features/Studio/Browser/StudioBrowserView.xaml" 'Control\.CanvasRailListBoxItem[\s\S]*Control\.KineticMediaCard' "Studio Browser must reuse shared bounded selection and kinetic media cards."
Require-Pattern "ReplayFoundry.Desktop/Features/Studio/Preview/StudioPreviewView.xaml" 'Control\.CanvasPane[\s\S]*Control\.CanvasGhostZone' "Studio preview and transport must reuse shared tonal and ghost surfaces."
Require-Pattern "ReplayFoundry.Desktop/Features/Studio/Inspector/StudioInspectorView.xaml" 'Control\.CanvasPane[\s\S]*Control\.CanvasInsetCard' "Studio Inspector must reuse shared tonal editor groups."
Require-Pattern "ReplayFoundry.Desktop/Features/Settings/SettingsView.xaml" 'Control\.CanvasPane[\s\S]*Control\.CanvasRailListBoxItem' "Settings navigation must reuse the shared canvas pane and bounded selection surface."
Require-Pattern "ReplayFoundry.Desktop/Resources/Controls/WindowChromeStyles.xaml" 'ResizeBorderThickness="6"' "WindowChrome must retain a native resize border."
Require-Pattern "ReplayFoundry.Desktop/Resources/Controls/WindowChromeStyles.xaml" 'Control\.CaptionButton' "Caption buttons must have a shared theme."
if ($chromeTheme -notmatch 'Text\.CaptionGlyph[\s\S]*?FontSize" Value="8"') { Add-Failure "Caption glyphs must stay compact without shrinking their button hit targets." }
if ($chromeTheme -notmatch 'Control\.CaptionButton[\s\S]*?Width" Value="46"[\s\S]*?Height" Value="40"') { Add-Failure "Caption buttons must retain their 46 by 40 DIP hit targets." }
foreach ($chromeSurface in @(
    "ReplayFoundry.Desktop/Shell/MainWindow.xaml",
    "ReplayFoundry.Desktop/Presentation/Controls/WindowTitleBar.xaml")) {
    $surfaceText = Read-RepoText $chromeSurface
    if ([regex]::Matches($surfaceText, 'Text\.CaptionGlyph').Count -lt 3) { Add-Failure "$chromeSurface must use the shared compact style for all three native caption glyphs." }
    if ($surfaceText -notmatch '<Image[\s\S]*?Width="28"[\s\S]*?Height="28"[\s\S]*?Source="\{Binding Icon, RelativeSource=\{RelativeSource AncestorType=') { Add-Failure "$chromeSurface must project the application icon at 28 DIP from its owning Window." }
}
Require-Pattern "ReplayFoundry.Desktop/Resources/Theme/Colors.xaml" '#071014[\s\S]*#58D6FF[\s\S]*#1599C8[\s\S]*#FFC85A' "The shared theme must preserve the website-aligned ink, cyan, blue, and yellow brand palette."
Require-Pattern "ReplayFoundry.Desktop/Resources/Theme/Brushes.xaml" 'x:Key="Brush\.WindowGrid"' "The scalable brand grid brush is missing."
Require-Pattern "ReplayFoundry.Desktop/Shell/Dock/FloatingDock.xaml" 'controls:IconPath' "The dock must use scalable semantic icons instead of raster artwork."
$floatingDock = Read-RepoText "ReplayFoundry.Desktop/Shell/Dock/FloatingDock.xaml"
$floatingDockStyles = Read-RepoText "ReplayFoundry.Desktop/Resources/Controls/FloatingDockStyles.xaml"
if ([regex]::Matches($floatingDock, '<DropShadowEffect').Count -ne 1 -or
    $floatingDock -notmatch 'ShadowDepth="0"') {
    Add-Failure "The floating dock must use one centered shadow instead of an offset duplicate rectangle."
}
if ($floatingDockStyles -notmatch '<Grid ClipToBounds="True">[\s\S]*?x:Name="ActiveSurface"[\s\S]*?Margin="5,2"') {
    Add-Failure "Dock selection must remain clipped and inset inside its navigation cell."
}
if ($selection -match 'x:Name="SelectionWash"') {
    Add-Failure "Checkboxes and radio buttons must not paint an oversized selection wash behind their labels."
}
if ($range -match 'LaserGuide|ThumbGlow') {
    Add-Failure "Slider thumbs must not paint stray guide lines or detached glow shapes."
}
if ($scroll -notmatch 'x:Name="PART_ScrollContentPresenter"[\s\S]*?x:Name="ScrollCorner"') {
    Add-Failure "Scroll viewers must theme both the content presenter and scrollbar corner instead of exposing the white default square."
}
Require-Pattern "ReplayFoundry.Desktop/Features/Generate/GenerationSetup/Steps/Audio/AudioStepView.xaml" 'controls:AudioSignalWaveform[\s\S]*Peaks="\{Binding WaveformPeaks\}"[\s\S]*Progress="\{Binding AuditionProgress\}"' "Audio setup must render its inspected peak envelope against real playback progress."
Require-Pattern "ReplayFoundry.Desktop/Presentation/Controls/AudioSignalWaveform.cs" 'OnRender[\s\S]*peaks\[index\][\s\S]*progressX[\s\S]*DrawLine' "The waveform must paint actual inspected peaks and a playback-bound playhead."
Require-Pattern "ReplayFoundry.Desktop/Platform/Media/WpfAudioStreamAuditionService.cs" 'DispatcherTimer[\s\S]*_player\.Position[\s\S]*PlaybackChanged' "Audio waveform progress must follow MediaPlayer position rather than decorative timing."
if ($range -notmatch 'Motion\.Ambient' -or
    $range -notmatch 'SystemParameters\.ClientAreaAnimation') {
    Add-Failure "Indeterminate signal motion must be visible when enabled and honor the Windows reduced-motion setting."
}
if ($mainWindow -match 'MainWindowBackground|Assets/Icons/Dock') { Add-Failure "MainWindow still references retired raster artwork." }
if ($selection -notmatch 'VerticalContentAlignment" Value="Center"' -or $selection -notmatch 'x:Name="Box"[\s\S]*?VerticalAlignment="Center"' -or $selection -notmatch 'x:Name="Outer"[\s\S]*?VerticalAlignment="Center"') { Add-Failure "Shared checkbox and radio indicators must remain vertically aligned with their labels." }
Require-Pattern "ReplayFoundry.Desktop/Resources/Theme/Motion.xaml" 'Motion\.Reduced' "Reduced-motion duration token is missing."
Require-Pattern "ReplayFoundry.Desktop/Resources/Theme/Iconography.xaml" 'Icon\.Glyph\.ChromeClose' "Caption glyph keys are missing."
Require-Pattern "ReplayFoundry.Desktop/Resources/Theme/Iconography.xaml" '<Geometry x:Key="Icon\.Edit">' "The centered semantic edit icon is missing."
Require-Pattern "ReplayFoundry.Desktop/Presentation/Controls/IconPath.cs" 'TryFindResource' "IconPath must resolve semantic resources instead of embedding feature glyphs."

$mediaRightsDialog = Read-RepoText "ReplayFoundry.Desktop/Platform/Dialogs/MediaRightsConfirmationWindow.xaml"
foreach ($requiredDialogPattern in @(
    '<controls:WindowTitleBar Subtitle="Generate" Status="Media rights confirmation"',
    'IconKey="Icon\.Lock"',
    'Text="\{Binding SelectionSummary\}"',
    'ItemsSource="\{Binding SourceNames\}"',
    'Content="Cancel"',
    'Content="Confirm and continue"')) {
    if ($mediaRightsDialog -notmatch $requiredDialogPattern) {
        Add-Failure "Media-rights confirmation must retain shared chrome, selected-media context, and clear cancel/confirm actions ($requiredDialogPattern)."
    }
}
if ($mediaRightsDialog -match '#[0-9A-Fa-f]{6,8}' -or $mediaRightsDialog -match '[\uE000-\uF8FF]') {
    Add-Failure "Media-rights confirmation must use shared theme brushes and semantic icons."
}

$featureFiles = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot "ReplayFoundry.Desktop/Features") -Recurse -File -Include *.xaml,*.cs
foreach ($file in $featureFiles) {
    $text = Get-Content -Raw -LiteralPath $file.FullName
    if ($file.Extension -eq ".xaml" -and $text -match '#[0-9A-Fa-f]{6,8}') { Add-Failure "Feature surface contains a hard-coded color: $($file.FullName)" }
    if ($text -match '[\uE000-\uF8FF]') { Add-Failure "Feature surface contains a private-use glyph instead of a semantic icon key: $($file.FullName)" }
    $maxLength = ((Get-Content -LiteralPath $file.FullName | ForEach-Object Length | Measure-Object -Maximum).Maximum)
    if ($file.Extension -eq ".xaml" -and $maxLength -gt 240) { Add-Failure "Feature XAML line exceeds 240 characters: $($file.FullName)" }
}

$allXaml = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot "ReplayFoundry.Desktop") -Recurse -File -Filter *.xaml |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
foreach ($file in $allXaml) {
    $text = Get-Content -Raw -LiteralPath $file.FullName
    if ($text -match '[\uE000-\uF8FF]' -and $file.Name -ne "Iconography.xaml") { Add-Failure "Non-iconography XAML contains a private-use glyph: $($file.FullName)" }
}

$studio = Read-RepoText "ReplayFoundry.Desktop/Features/Studio/StudioView.xaml"
if ($studio -match '<TabControl\b' -and $selection -notmatch '<Style\s+TargetType="\{x:Type TabControl\}"') { Add-Failure "Studio compact tabs lack a themed TabControl template." }
$iconButtonFiles = $featureFiles | Where-Object { $_.Extension -eq ".xaml" -and (Get-Content -Raw -LiteralPath $_.FullName) -match "IconButtonContentTemplate" }
foreach ($file in $iconButtonFiles) {
    $text = Get-Content -Raw -LiteralPath $file.FullName
    if ($text -notmatch 'AutomationProperties\.Name') { Add-Failure "Icon-only surface lacks an automation name: $($file.FullName)" }
    if ($text -notmatch 'ToolTip=') { Add-Failure "Icon-only surface lacks a ToolTip: $($file.FullName)" }
}

$lineCount = (Get-Content -LiteralPath $MyInvocation.MyCommand.Path).Count
if ($lineCount -gt 550) { Add-Failure "UI-03 visual-system guard exceeds the 550-line limit ($lineCount)." }
if ($failures.Count -gt 0) {
    Write-Error ("UI/UX visual-system guard failed:`n- " + ($failures -join "`n- "))
    exit 1
}
Write-Host "UI/UX visual-system guard passed: theme dictionaries, WPF parts, semantic icons, chrome, state language, automation, and feature color boundaries inspected."
