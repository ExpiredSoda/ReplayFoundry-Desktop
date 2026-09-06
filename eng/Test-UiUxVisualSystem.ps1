param()

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$failures = [System.Collections.Generic.List[string]]::new()
$isPublicSnapshot = Test-Path -LiteralPath `
    (Join-Path $repositoryRoot '.replayfoundry-public-source') -PathType Leaf

function Add-Failure { param([string]$Message) $failures.Add($Message) }
function Read-RepoText { param([string]$Path) Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot $Path) }
function Require-Path { param([string]$Path) if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $Path))) { Add-Failure "Required UI-03 path is missing: $Path" } }
function Require-Pattern { param([string]$Path, [string]$Pattern, [string]$Message) if ((Read-RepoText $Path) -notmatch $Pattern) { Add-Failure $Message } }

$requiredPaths = @(
    "src/ReplayFoundry.Desktop/Assets/Icons/Application/ReplayFoundry.ico",
    "src/ReplayFoundry.Desktop/Assets/Branding/ReplayFoundry-App-Icon-1024.png",
    "src/ReplayFoundry.Desktop/Assets/Branding/favicon.svg",
    "eng/New-ReplayFoundryBrandAssets.ps1",
    "eng/New-ReplayFoundryInstallerBranding.ps1",
    "eng/Test-InstallerBranding.ps1",
    "src/ReplayFoundry.Desktop/Resources/Theme/Colors.xaml",
    "src/ReplayFoundry.Desktop/Resources/Theme/Brushes.xaml",
    "src/ReplayFoundry.Desktop/Resources/Theme/Iconography.xaml",
    "src/ReplayFoundry.Desktop/Resources/Theme/Motion.xaml",
    "src/ReplayFoundry.Desktop/Resources/Controls/ButtonStyles.xaml",
    "src/ReplayFoundry.Desktop/Resources/Controls/InputStyles.xaml",
    "src/ReplayFoundry.Desktop/Resources/Controls/SelectionStyles.xaml",
    "src/ReplayFoundry.Desktop/Resources/Controls/ScrollStyles.xaml",
    "src/ReplayFoundry.Desktop/Resources/Controls/MenuPopupStyles.xaml",
    "src/ReplayFoundry.Desktop/Resources/Controls/RangeProgressStyles.xaml",
    "src/ReplayFoundry.Desktop/Resources/Controls/ValidationStyles.xaml",
    "src/ReplayFoundry.Desktop/Resources/Controls/WindowChromeStyles.xaml",
    "src/ReplayFoundry.Desktop/Platform/Dialogs/MediaRightsConfirmationWindow.xaml",
    "src/ReplayFoundry.Desktop/Presentation/Controls/IconPath.cs",
    "src/ReplayFoundry.Desktop/Presentation/Controls/AudioSignalWaveform.cs")
foreach ($path in $requiredPaths) { Require-Path $path }

$applicationIconPath = Join-Path $repositoryRoot "src/ReplayFoundry.Desktop/Assets/Icons/Application/ReplayFoundry.ico"
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

$desktopProject = Read-RepoText "src/ReplayFoundry.Desktop/ReplayFoundry.Desktop.csproj"
$installerDefinition = Read-RepoText "installer/ReplayFoundry.iss"
if ($desktopProject -notmatch '<ApplicationIcon>Assets\\Icons\\Application\\ReplayFoundry\.ico</ApplicationIcon>') {
    Add-Failure "Desktop publishing must point ApplicationIcon at the canonical multi-size ReplayFoundry.ico."
}
if ($installerDefinition -notmatch 'SetupIconFile=\{#RepoRoot\}\\src\\ReplayFoundry\.Desktop\\Assets\\Icons\\Application\\ReplayFoundry\.ico') {
    Add-Failure "The Windows setup executable must use the same canonical ReplayFoundry.ico as Desktop."
}
$installerBranding = Read-RepoText "eng/New-ReplayFoundryInstallerBranding.ps1"
foreach ($brandingPattern in @(
    'ReplayFoundry-App-Icon-1024\.png',
    '#071014', '#1599C8', '#58D6FF', '#FFC85A',
    '1988', '1440', 'installer-branding-manifest\.json')) {
    if ($installerBranding -notmatch $brandingPattern) {
        Add-Failure "Installer branding generator is missing canonical visual contract: $brandingPattern"
    }
}
foreach ($installerPattern in @(
    'WizardStyle=modern dark windows11 hidebevels includetitlebar',
    'WizardBackImageFile=\{#WizardBackImagePath\}',
    'WizardImageFile=\{#WizardImagePath\}',
    'WizardSmallImageFile=\{#WizardSmallImagePath\}',
    'HighContrastActive')) {
    if ($installerDefinition -notmatch $installerPattern) {
        Add-Failure "Installer definition is missing branded/high-contrast presentation: $installerPattern"
    }
}

$app = Read-RepoText "src/ReplayFoundry.Desktop/App.xaml"
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

$mainWindow = Read-RepoText "src/ReplayFoundry.Desktop/Shell/MainWindow.xaml"
$mainWindowCode = Read-RepoText "src/ReplayFoundry.Desktop/Shell/MainWindow.xaml.cs"
$chromeInteraction = Read-RepoText "src/ReplayFoundry.Desktop/Shell/Windowing/WindowChromeInteraction.cs"
Require-Pattern "src/ReplayFoundry.Desktop/Shell/MainWindow.xaml" 'WindowStyle="None"' "Custom window chrome must remove the default frame."
Require-Pattern "src/ReplayFoundry.Desktop/Shell/MainWindow.xaml" 'AllowsTransparency="False"' "Custom window chrome must preserve native composition."
Require-Pattern "src/ReplayFoundry.Desktop/Shell/MainWindow.xaml" 'WindowChrome\.WindowChrome' "MainWindow must apply the WindowChrome resource."
Require-Pattern "src/ReplayFoundry.Desktop/Shell/MainWindow.xaml" 'WindowChrome\.IsHitTestVisibleInChrome' "Title-bar hit testing must be explicit."
Require-Pattern "src/ReplayFoundry.Desktop/Shell/MainWindow.xaml" 'Content="\{Binding CurrentWorkspace\}"' "The shell must retain one workspace host."
foreach ($caption in @("CaptionMinimizeButton", "CaptionMaximizeButton", "CaptionCloseButton")) {
    if ($mainWindow -notmatch [regex]::Escape($caption) -or $mainWindow -notmatch 'AutomationProperties\.Name="[^"]*window') { Add-Failure "Caption button automation coverage is incomplete: $caption" }
}
foreach ($nativeCommand in @("MinimizeWindow", "MaximizeWindow", "RestoreWindow", "CloseWindow", "ShowSystemMenu")) {
    if ($chromeInteraction -notmatch [regex]::Escape("SystemCommands.$nativeCommand")) { Add-Failure "Native caption behavior is missing SystemCommands.$nativeCommand." }
}
if ($mainWindowCode -match 'CurrentWorkspace|CurrentDestination|ShellDestination|\.Content\s*=|\.Visibility\s*=') { Add-Failure "MainWindow code-behind crosses the shell MVVM boundary." }

$scroll = Read-RepoText "src/ReplayFoundry.Desktop/Resources/Controls/ScrollStyles.xaml"
$input = Read-RepoText "src/ReplayFoundry.Desktop/Resources/Controls/InputStyles.xaml"
$selection = Read-RepoText "src/ReplayFoundry.Desktop/Resources/Controls/SelectionStyles.xaml"
$chromeTheme = Read-RepoText "src/ReplayFoundry.Desktop/Resources/Controls/WindowChromeStyles.xaml"
$popup = Read-RepoText "src/ReplayFoundry.Desktop/Resources/Controls/MenuPopupStyles.xaml"
$range = Read-RepoText "src/ReplayFoundry.Desktop/Resources/Controls/RangeProgressStyles.xaml"
$progressBehavior = Read-RepoText "src/ReplayFoundry.Desktop/Presentation/Controls/IndeterminateProgressBehavior.cs"
$buttons = Read-RepoText "src/ReplayFoundry.Desktop/Resources/Controls/ButtonStyles.xaml"
$cards = Read-RepoText "src/ReplayFoundry.Desktop/Resources/Controls/CardStyles.xaml"
foreach ($part in @("PART_Track", "Thumb", "RepeatButton")) { if ($scroll -notmatch $part) { Add-Failure "Scroll theme is missing required template part $part." } }
foreach ($part in @("PART_Popup", "PART_ContentHost")) { if ($input -notmatch $part) { Add-Failure "Input theme is missing required ComboBox/TextBox part $part." } }
foreach ($control in @("CheckBox", "RadioButton", "ListBoxItem", "TabControl", "GridSplitter")) { if ($selection -notmatch [regex]::Escape($control)) { Add-Failure "Selection theme is missing $control." } }
if ($popup -notmatch "ToolTip") { Add-Failure "Popup theme is missing ToolTip." }
$desktopXaml = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot "src/ReplayFoundry.Desktop") -Recurse -Filter *.xaml |
    Get-Content -Raw
if ($desktopXaml -match '<(ContextMenu|MenuItem|Menu)\b') { Add-Failure "A menu control is in use but its dormant shared styles were removed." }
foreach ($control in @("Slider", "ProgressBar", "PART_Indicator", "RangeThumb")) { if ($range -notmatch $control) { Add-Failure "Range/progress theme is missing $control." } }
foreach ($progressStyle in @("Control.ProgressBar", "Control.ProgressBar.Compact", "Control.ProgressBar.Standard", "Control.ProgressBar.Featured")) {
    if ($range -notmatch [regex]::Escape($progressStyle)) { Add-Failure "Shared progress style is missing $progressStyle." }
}
if ($range -notmatch 'x:Name="PART_Track"' -or
    $range -notmatch 'x:Name="PART_Indicator"') {
    Add-Failure "Shared determinate progress must retain WPF's PART_Track and PART_Indicator sizing contract."
}
foreach ($kineticContract in @(
    @{ Text = $input; Pattern = 'Control\.InlineSelectorComboBox[\s\S]*Control\.InlineSearchTextBox'; Message = 'Shared inline selector and search styles are missing.' },
    @{ Text = $input; Pattern = 'DropDownCaret[\s\S]*x:Name="PART_Popup"[\s\S]*PopupAnimation="None"[\s\S]*x:Name="PopupSurface"[\s\S]*Property="IsDropDownOpen"[\s\S]*TargetName="DropDownCaret"'; Message = 'Shared ComboBox must keep a stateful caret and one bounded, nonanimated popup.' },
    @{ Text = $selection; Pattern = 'Control\.PreferenceChoice[\s\S]*Control\.CanvasRailListBoxItem'; Message = 'Shared preference choices or bounded navigation selection are missing.' },
    @{ Text = $buttons; Pattern = 'Control\.KeyboardFocus[\s\S]*Control\.GhostButton'; Message = 'Ghost actions and the shared keyboard-only focus foundation are missing.' },
    @{ Text = $cards; Pattern = 'Control\.CanvasPane[\s\S]*Control\.KineticMediaCard'; Message = 'Tonal panes or kinetic media cards are missing.' },
    @{ Text = $range; Pattern = 'ThumbSurface[\s\S]*IsDragging[\s\S]*Style TargetType="\{x:Type Slider\}" BasedOn="\{StaticResource Control\.KeyboardFocus\}"'; Message = 'Slider drag feedback and its keyboard-only focus adorner are missing.' })) {
    if ($kineticContract.Text -notmatch $kineticContract.Pattern) { Add-Failure $kineticContract.Message }
}
if ($buttons -notmatch 'x:Name="Surface"' -or
    $buttons -notmatch 'Property="IsMouseOver"[\s\S]*TargetName="Surface"' -or
    $buttons -notmatch 'Property="IsPressed"[\s\S]*TargetName="Surface"' -or
    $buttons -match 'HoverScale|PressScale|DoubleAnimation|DropShadowEffect') {
    Add-Failure 'Shared buttons must retain one stable surface with hover/press feedback and no motion or shadow layers.'
}
if ($buttons -match 'x:Name="(?:KineticAura|HoverAuraRoot|PressAuraGate)"' -or
    $buttons -match 'IsKeyboardFocusWithin[\s\S]{0,220}Property="BorderBrush"') {
    Add-Failure 'Shared buttons must not stack an aura or template focus border with the keyboard-only focus adorner.'
}
if ($buttons -match 'x:Name="AccentLeak"') {
    Add-Failure 'Shared buttons must not restore the detached underline hover treatment.'
}
$kineticMediaCard = [regex]::Match(
    $cards,
    'x:Key="Control\.KineticMediaCard"[\s\S]*?(?=<Style x:Key="Control\.StudioClipCard")').Value
if ($kineticMediaCard -match 'DropShadowEffect' -or
    $kineticMediaCard -match 'IsMouseOver[\s\S]{0,220}(?:BorderBrush|Brush\.KineticGlow)') {
    Add-Failure 'Media-card hover must use its one tonal surface without a stacked glow or contour.'
}
if ($selection -match 'IsSelected[\s\S]{0,260}Property="BorderBrush"' -or
    $selection -notmatch 'x:Name="SelectionRail"') {
    Add-Failure 'Shared selected rows and tabs must use one tonal surface or rail instead of another full border.'
}
if ($input -match 'x:Name="(?:FocusRail|OpenRail)"') {
    Add-Failure 'Text inputs and ComboBoxes must use their complete bounded surface for interaction feedback, not partial underline rails.'
}
Require-Pattern "src/ReplayFoundry.Desktop/Features/Library/Sections/LibraryFilterBarView.xaml" 'Control\.InlineSearchTextBox[\s\S]*Control\.InlineSelectorComboBox' "Library filters must reuse the shared borderless editorial controls."
Require-Pattern "src/ReplayFoundry.Desktop/Features/Library/Sections/LibraryCategoryRailView.xaml" 'Control\.CanvasRailListBoxItem' "Library category selection must reuse the shared bounded selection surface."
Require-Pattern "src/ReplayFoundry.Desktop/Features/Generate/CompositionReview/CompositionRegionEditor.xaml" 'CompositionReview\.CropMark' "Layout Review selected regions must retain precise crop-mark corners."
Require-Pattern "src/ReplayFoundry.Desktop/Features/Publish/Sections/PublishCalendarView.xaml" 'Control\.CanvasPane[\s\S]*Brush\.KineticGlowSoft' "Publish scheduling must preserve its tonal pane and cyan selected-day cues."
$publishCalendar = Read-RepoText "src/ReplayFoundry.Desktop/Features/Publish/Sections/PublishCalendarView.xaml"
$selectedDayTrigger = [regex]::Match(
    $publishCalendar,
    '<DataTrigger Binding="\{Binding IsSelected,[^>]+>[\s\S]*?</DataTrigger>').Value
if ($selectedDayTrigger -notmatch 'Property="Background"[\s\S]*Brush\.KineticGlowSoft' -or
    $selectedDayTrigger -match 'Property="BorderBrush"' -or
    $publishCalendar -match 'Height="1\.5"') {
    Add-Failure 'Publish calendar selection must use one tonal day surface without another border under keyboard focus.'
}
Require-Pattern "src/ReplayFoundry.Desktop/Features/Publish/Sections/PublishLibraryBrowserView.xaml" 'Control\.InlineSearchTextBox[\s\S]*Control\.InlineSelectorComboBox' "Publish Library filters must reuse shared editorial controls."
Require-Pattern "src/ReplayFoundry.Desktop/Resources/Controls/ButtonStyles.xaml" 'Control\.StudioQuietButton[\s\S]*Control\.StudioPrimaryButton[\s\S]*Control\.StudioSecondaryButton[\s\S]*Control\.StudioDestructiveButton[\s\S]*Control\.StudioQuietIconButton[\s\S]*Control\.StudioDestructiveIconButton' "Studio action hierarchy styles are missing."
$studioBrowser = Read-RepoText "src/ReplayFoundry.Desktop/Features/Studio/Browser/StudioBrowserView.xaml"
if ($studioBrowser -notmatch 'StudioBrowser\.ClipItem[\s\S]*SelectedValue="\{Binding SelectedAsset\.Id[\s\S]*SelectionChanged="BrowserItems_SelectionChanged"' -or
    $studioBrowser -notmatch 'Control\.StudioClipCard' -or
    $studioBrowser -notmatch 'Control\.StudioSecondaryButton' -or
    $studioBrowser -notmatch 'Control\.StudioDestructiveButton' -or
    $studioBrowser -notmatch 'Text="EXCLUDED"') {
    Add-Failure "Studio Browser must make the complete card selectable while keeping its nested secondary and exclusion actions distinct."
}
if ($studioBrowser -match 'Control\.(?:KineticMediaCard|IconButton|StudioCardHitTarget)') {
    Add-Failure "Studio clip cards must not stack kinetic card and icon-button contours around one selection."
}
$studioView = Read-RepoText "src/ReplayFoundry.Desktop/Features/Studio/StudioView.xaml"
$studioCaptionEditor = Read-RepoText "src/ReplayFoundry.Desktop/Features/Studio/Inspector/StudioCaptionEditorView.xaml"
$studioClipEditor = Read-RepoText "src/ReplayFoundry.Desktop/Features/Studio/Inspector/StudioClipEditorView.xaml"
if ($studioView -notmatch 'Control\.StudioSecondaryButton[\s\S]{0,900}HiddenMoments\.OpenButtonText' -or
    $studioCaptionEditor -notmatch 'ApplyCaptionLookToAllText[\s\S]{0,180}Control\.StudioSecondaryButton' -or
    $studioClipEditor -notmatch 'Content="Apply clip changes"[\s\S]{0,100}Control\.StudioPrimaryButton' -or
    $studioClipEditor -notmatch 'Content="Restore suggested cut"[\s\S]{0,100}Control\.StudioSecondaryButton') {
    Add-Failure "Studio review, cross-clip, apply, and restore actions must use a visible primary/secondary button hierarchy."
}
$gameContext = Read-RepoText "src/ReplayFoundry.Desktop/Features/Generate/GenerationSetup/Steps/GameContext/GameContextStepView.xaml"
if ($mainWindow -match 'WikidataStatusIndicator' -or
    $gameContext -notmatch 'x:Name="PublicLookupStateRow"[\s\S]*SelectedSource\.IsPublicLookupAvailable' -or
    $gameContext -notmatch 'IsEnabled="\{Binding SelectedSource\.CanUsePublicLookup\}"') {
    Add-Failure "Public game lookup must report stable capability beside its Generate opt-in instead of a global permission-state icon."
}
Require-Pattern "src/ReplayFoundry.Desktop/Features/Studio/Preview/StudioPreviewView.xaml" 'Control\.CanvasPane[\s\S]*Control\.CanvasGhostZone' "Studio preview and transport must reuse shared tonal and ghost surfaces."
Require-Pattern "src/ReplayFoundry.Desktop/Features/Studio/Inspector/StudioInspectorView.xaml" 'Control\.CanvasPane[\s\S]*Control\.CanvasInsetCard' "Studio Inspector must reuse shared tonal editor groups."
Require-Pattern "src/ReplayFoundry.Desktop/Features/Settings/SettingsView.xaml" 'Control\.CanvasPane[\s\S]*Control\.CanvasRailListBoxItem' "Settings navigation must reuse the shared canvas pane and bounded selection surface."
Require-Pattern "src/ReplayFoundry.Desktop/Resources/Controls/WindowChromeStyles.xaml" 'ResizeBorderThickness="6"' "WindowChrome must retain a native resize border."
Require-Pattern "src/ReplayFoundry.Desktop/Resources/Controls/WindowChromeStyles.xaml" 'Control\.CaptionButton' "Caption buttons must have a shared theme."
if ($chromeTheme -notmatch 'Text\.CaptionGlyph[\s\S]*?FontSize" Value="8"') { Add-Failure "Caption glyphs must stay compact without shrinking their button hit targets." }
if ($chromeTheme -notmatch 'Control\.CaptionButton[\s\S]*?Width" Value="46"[\s\S]*?Height" Value="40"') { Add-Failure "Caption buttons must retain their 46 by 40 DIP hit targets." }
foreach ($chromeSurface in @(
    "src/ReplayFoundry.Desktop/Shell/MainWindow.xaml",
    "src/ReplayFoundry.Desktop/Presentation/Controls/WindowTitleBar.xaml")) {
    $surfaceText = Read-RepoText $chromeSurface
    if ([regex]::Matches($surfaceText, 'Text\.CaptionGlyph').Count -lt 3) { Add-Failure "$chromeSurface must use the shared compact style for all three native caption glyphs." }
    if ($surfaceText -notmatch '<Image[\s\S]*?Width="28"[\s\S]*?Height="28"[\s\S]*?Source="\{Binding Icon, RelativeSource=\{RelativeSource AncestorType=') { Add-Failure "$chromeSurface must project the application icon at 28 DIP from its owning Window." }
}
Require-Pattern "src/ReplayFoundry.Desktop/Resources/Theme/Colors.xaml" '#0B0F14[\s\S]*#58D6FF[\s\S]*#1599C8[\s\S]*#FFC85A' "The shared theme must preserve graphite ink with the cyan, blue, and yellow brand accents."
Require-Pattern "src/ReplayFoundry.Desktop/Resources/Theme/Brushes.xaml" 'x:Key="Brush\.WindowGrid"' "The scalable brand grid brush is missing."
Require-Pattern "src/ReplayFoundry.Desktop/Shell/Dock/FloatingDock.xaml" 'controls:IconPath' "The dock must use scalable semantic icons instead of raster artwork."
$floatingDock = Read-RepoText "src/ReplayFoundry.Desktop/Shell/Dock/FloatingDock.xaml"
$floatingDockStyles = Read-RepoText "src/ReplayFoundry.Desktop/Resources/Controls/FloatingDockStyles.xaml"
if ($floatingDock -match 'DropShadowEffect' -or
    $floatingDock -notmatch 'x:Name="DockFrame"') {
    Add-Failure "The compact dock must use one frame without offset shadow layers."
}
if ($floatingDock -notmatch '<Grid ClipToBounds="True">' -or
    $floatingDockStyles -notmatch 'x:Name="ButtonSurface"[\s\S]*Property="dock:FloatingDock\.IsActive"[\s\S]*TargetName="ButtonSurface" Property="Background" Value="\{DynamicResource Brush\.InteractiveSelected\}"' -or
    $floatingDockStyles -notmatch 'Control\.DockKeyboardFocus') {
    Add-Failure "Dock selection must stay inside its single button surface with a distinct keyboard focus cue."
}
$activeDockTrigger = [regex]::Match($floatingDockStyles, '<Trigger Property="dock:FloatingDock\.IsActive"[\s\S]*?</Trigger>').Value
if ($activeDockTrigger -match 'BorderBrush|BorderThickness') {
    Add-Failure "Dock selection must use one tonal active surface without a second outline beneath keyboard focus."
}
if ($selection -match 'x:Name="SelectionWash"') {
    Add-Failure "Checkboxes and radio buttons must not paint an oversized selection wash behind their labels."
}
if ($range -match 'LaserGuide|ThumbGlow') {
    Add-Failure "Slider thumbs must not paint stray guide lines or detached glow shapes."
}
if ($scroll -match 'ThumbGlow' -or
    $scroll -match 'IsKeyboardFocusWithin[\s\S]{0,220}Property="BorderBrush"') {
    Add-Failure "Scrollbars must use one thumb surface and the shared keyboard-only focus adorner without a glow or second focus contour."
}
$workspaceStyles = Read-RepoText "src/ReplayFoundry.Desktop/Resources/Controls/WorkspaceStyles.xaml"
$tabStyles = Read-RepoText "src/ReplayFoundry.Desktop/Resources/Controls/TabStyles.xaml"
$timePicker = Read-RepoText "src/ReplayFoundry.Desktop/Presentation/Controls/TimePickerField.xaml"
$generationSetupStyles = Read-RepoText "src/ReplayFoundry.Desktop/Features/Generate/GenerationSetup/GenerationSetupStyles.xaml"
if ($workspaceStyles -notmatch 'Control\.KeyboardFocusAdorner[\s\S]*Brush\.BorderFocus[\s\S]*BorderThickness="2"' -or
    $workspaceStyles -notmatch 'Control\.KeyboardFocus[\s\S]*FocusVisualStyle') {
    Add-Failure "The shared keyboard-only focus adorner is missing or no longer high-contrast brush driven."
}
foreach ($focusSurface in @(
    @{ Name = "selection controls"; Text = $selection },
    @{ Name = "Studio navigation"; Text = $tabStyles },
    @{ Name = "time picker"; Text = $timePicker },
    @{ Name = "generation setup"; Text = $generationSetupStyles })) {
    if ($focusSurface.Text -match '(?:IsKeyboardFocusWithin|IsKeyboardFocused|IsFocused)[\s\S]{0,220}Property="BorderBrush"') {
        Add-Failure "$($focusSurface.Name) must not mutate an inner border for keyboard focus in addition to FocusVisualStyle."
    }
}
$libraryContent = Read-RepoText "src/ReplayFoundry.Desktop/Features/Library/Sections/LibraryContentView.xaml"
if ($libraryContent -notmatch 'LibraryGridItemContainer[\s\S]*Property="BorderThickness" Value="0"' -or
    $libraryContent -notmatch 'Background="\{DynamicResource Brush\.BorderFocus\}"[\s\S]*IsSelected' -or
    $libraryContent -match 'IsSelected[\s\S]{0,260}Property="BorderBrush"') {
    Add-Failure "Library card mode must keep its ListBoxItem container transparent and use one tonal selected card with one rail."
}
$publishChecklist = Read-RepoText "src/ReplayFoundry.Desktop/Features/Publish/Sections/PublishChecklistView.xaml"
$publishOutputSettings = Read-RepoText "src/ReplayFoundry.Desktop/Features/Publish/Sections/PublishOutputSettingsView.xaml"
$publishQueueHistory = Read-RepoText "src/ReplayFoundry.Desktop/Features/Publish/Sections/PublishQueueHistoryView.xaml"
foreach ($publishInnerSurface in @($publishChecklist, $publishOutputSettings, $publishQueueHistory)) {
    if ($publishInnerSurface -match 'Control\.InsetCard') {
        Add-Failure "Publish sections inside SectionCard must use borderless CanvasInsetCard groups instead of card-on-card rims."
    }
}
if ($scroll -notmatch 'x:Name="PART_ScrollContentPresenter"[\s\S]*?x:Name="ScrollCorner"') {
    Add-Failure "Scroll viewers must theme both the content presenter and scrollbar corner instead of exposing the white default square."
}
Require-Pattern "src/ReplayFoundry.Desktop/Features/Generate/GenerationSetup/Steps/Audio/AudioStepView.xaml" 'controls:AudioSignalWaveform[\s\S]*Peaks="\{Binding WaveformPeaks\}"[\s\S]*Progress="\{Binding AuditionProgress\}"' "Audio setup must render its inspected peak envelope against real playback progress."
Require-Pattern "src/ReplayFoundry.Desktop/Presentation/Controls/AudioSignalWaveform.cs" 'OnRender[\s\S]*peaks\[index\][\s\S]*progressX[\s\S]*DrawLine' "The waveform must paint actual inspected peaks and a playback-bound playhead."
Require-Pattern "src/ReplayFoundry.Desktop/Platform/Media/WpfAudioStreamAuditionService.cs" 'DispatcherTimer[\s\S]*_player\.Position[\s\S]*PlaybackChanged' "Audio waveform progress must follow MediaPlayer position rather than decorative timing."
if ($progressBehavior -notmatch 'Motion\.Ambient' -or
    $progressBehavior -notmatch 'SystemParameters\.ClientAreaAnimation' -or
    $progressBehavior -notmatch '_progress\.IsVisible' -or
    $progressBehavior -notmatch 'Control\.TemplateProperty') {
    Add-Failure "Indeterminate signal motion must be visible when enabled, stop while hidden, survive retemplating, and honor the Windows reduced-motion setting."
}
if ($range -notmatch 'IndeterminateSignal[\s\S]*RenderTransformOrigin="0\.5,0\.5"[\s\S]*ScaleTransform ScaleX="0\.28"' -or
    $range -notmatch 'IndeterminateSignal[\s\S]*Background="\{TemplateBinding Foreground\}"' -or
    $range -notmatch 'IndeterminateProgressBehavior\.IsEnabled' -or
    $range -notmatch 'Trigger Property="IsIndeterminate"[\s\S]*Setter TargetName="IndeterminateSignal" Property="Opacity" Value="1"' -or
    $range -match 'IndeterminateFallback|IndeterminateBrushTransform|IndeterminateStoryboard' -or
    $progressBehavior -notmatch 'PointAnimation[\s\S]*Point\(-0\.5, 0\.5\)[\s\S]*Point\(1\.5, 0\.5\)[\s\S]*RepeatBehavior\.Forever[\s\S]*BeginAnimation') {
    Add-Failure "Shared indeterminate progress must keep a reduced-motion marker and move a width-independent signal when animation is enabled."
}
$progressSurfaces = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot "src/ReplayFoundry.Desktop/Features") -Recurse -File -Filter *.xaml
foreach ($file in $progressSurfaces) {
    $text = Get-Content -Raw -LiteralPath $file.FullName
    foreach ($match in [regex]::Matches($text, '<ProgressBar\b[\s\S]*?/>')) {
        $element = $match.Value
        if ($element -notmatch 'Style="\{DynamicResource Control\.ProgressBar\.(?:Compact|Standard|Featured)\}"') { Add-Failure "ProgressBar must select a shared size: $($file.FullName)" }
        if ($element -match '\bHeight="') { Add-Failure "ProgressBar height must come from its shared style: $($file.FullName)" }
        if ($element -notmatch 'AutomationProperties\.Name=') { Add-Failure "ProgressBar needs an accessible name: $($file.FullName)" }
    }
}
$generationProgress = Read-RepoText "src/ReplayFoundry.Desktop/Features/Generate/Progress/GenerationProgressView.xaml"
if ($generationProgress -notmatch 'Text="\{Binding Title\}"[\s\S]*Text="\{Binding Detail\}"[\s\S]*Control\.ProgressBar\.Featured' -or
    $generationProgress -match 'GenerationProgress\.ProgressBar') {
    Add-Failure "Generator feedback must remain separate from, and reuse, the shared featured progress bar."
}
if ($mainWindow -match 'MainWindowBackground|Assets/Icons/Dock') { Add-Failure "MainWindow still references retired raster artwork." }
if ($selection -notmatch 'VerticalContentAlignment" Value="Center"' -or $selection -notmatch 'x:Name="Box"[\s\S]*?VerticalAlignment="Center"' -or $selection -notmatch 'x:Name="Outer"[\s\S]*?VerticalAlignment="Center"') { Add-Failure "Shared checkbox and radio indicators must remain vertically aligned with their labels." }
Require-Pattern "src/ReplayFoundry.Desktop/App.xaml.cs" 'ClientAreaAnimation[\s\S]*Motion\.Hover[\s\S]*TimeSpan\.Zero' "Reduced motion must zero the duration tokens active animations consume."
Require-Pattern "src/ReplayFoundry.Desktop/Resources/Theme/Iconography.xaml" 'Icon\.Glyph\.ChromeClose' "Caption glyph keys are missing."
Require-Pattern "src/ReplayFoundry.Desktop/Resources/Theme/Iconography.xaml" '<Geometry x:Key="Icon\.Edit">' "The centered semantic edit icon is missing."
Require-Pattern "src/ReplayFoundry.Desktop/Presentation/Controls/IconPath.cs" 'TryFindResource' "IconPath must resolve semantic resources instead of embedding feature glyphs."

$mediaRightsDialog = Read-RepoText "src/ReplayFoundry.Desktop/Platform/Dialogs/MediaRightsConfirmationWindow.xaml"
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

$featureFiles = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot "src/ReplayFoundry.Desktop/Features") -Recurse -File -Include *.xaml,*.cs
foreach ($file in $featureFiles) {
    $text = Get-Content -Raw -LiteralPath $file.FullName
    if ($file.Extension -eq ".xaml" -and $text -match '#[0-9A-Fa-f]{6,8}') { Add-Failure "Feature surface contains a hard-coded color: $($file.FullName)" }
    if ($text -match '[\uE000-\uF8FF]') { Add-Failure "Feature surface contains a private-use glyph instead of a semantic icon key: $($file.FullName)" }
    $maxLength = ((Get-Content -LiteralPath $file.FullName | ForEach-Object Length | Measure-Object -Maximum).Maximum)
    if ($file.Extension -eq ".xaml" -and $maxLength -gt 240) { Add-Failure "Feature XAML line exceeds 240 characters: $($file.FullName)" }
}

$allXaml = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot "src/ReplayFoundry.Desktop") -Recurse -File -Filter *.xaml |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
foreach ($file in $allXaml) {
    $text = Get-Content -Raw -LiteralPath $file.FullName
    if ($text -match '[\uE000-\uF8FF]' -and $file.Name -ne "Iconography.xaml") { Add-Failure "Non-iconography XAML contains a private-use glyph: $($file.FullName)" }
}

$studio = Read-RepoText "src/ReplayFoundry.Desktop/Features/Studio/StudioView.xaml"
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
