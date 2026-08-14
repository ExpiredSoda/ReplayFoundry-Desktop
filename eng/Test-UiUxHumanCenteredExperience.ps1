param()

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$failures = [System.Collections.Generic.List[string]]::new()

function Read-RepoText { param([string]$Path) Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot $Path) }
function Require-Path { param([string]$Path) if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $Path))) { $failures.Add("Missing UI-04 path: $Path") } }
function Require-Pattern { param([string]$Path, [string]$Pattern, [string]$Message) if ((Read-RepoText $Path) -notmatch $Pattern) { $failures.Add($Message) } }

$requiredPaths = @(
    "ReplayFoundry.Desktop/app.manifest",
    "ReplayFoundry.Desktop/Shell/Windowing/WindowStartupPolicy.cs",
    "ReplayFoundry.Desktop/Shell/Windowing/WindowWorkAreaCalculator.cs",
    "ReplayFoundry.Desktop/Shell/Windowing/MainWindowNativeBehavior.cs",
    "ReplayFoundry.Desktop/Shell/Windowing/WindowChromeInteraction.cs",
    "ReplayFoundry.Desktop/Shell/Windowing/ResponsiveReadabilityState.cs",
    "ReplayFoundry.Desktop/Shell/Guidance/GuidanceViewModels.cs",
    "ReplayFoundry.Desktop/Shell/Guidance/GuidanceViews.xaml",
    "ReplayFoundry.Desktop/Assets/Branding/ReplayFoundry-App-Icon-1024.png",
    "ReplayFoundry.Desktop/Assets/Branding/favicon.svg",
    "ReplayFoundry.Desktop/Features/Settings/Sections/SettingsSectionViews.xaml.cs",
    "ReplayFoundry.Desktop/Features/Studio/StudioSectionViews.xaml.cs",
    "ReplayFoundry.Desktop/Features/Library/Sections/LibrarySectionViews.xaml.cs",
    "ReplayFoundry.Desktop/Features/Publish/Sections/PublishSectionViews.xaml.cs",
    "ReplayFoundry.Desktop/Features/Publish/Sections/PublishOutputSettingsView.xaml",
    "ReplayFoundry.Desktop/Presentation/Feedback/UserFacingIssue.cs",
    "ReplayFoundry.Desktop/Presentation/Controls/IssuePanel.xaml",
    "ReplayFoundry.Desktop/Presentation/Accessibility/CursorPolicy.cs",
    "ReplayFoundry.Desktop/Presentation/Accessibility/FocusOnLoadBehavior.cs")
foreach ($path in $requiredPaths) { Require-Path $path }

$window = Read-RepoText "ReplayFoundry.Desktop/Shell/MainWindow.xaml"
foreach ($pattern in @(
    'WindowState="Maximized"',
    'ShowInTaskbar="True"',
    'Topmost="False"',
    'ResizeMode="CanResize"',
    'WindowChrome\.WindowChrome',
    'Panel\.ZIndex="100"',
    'Panel\.ZIndex="90"',
    'MinWidth="500"',
    'MinHeight="420"',
    'CaptionHelpButton',
    'Key="F1"',
    'Key="K" Modifiers="Control"',
    'Key="Oem2" Modifiers="Control"',
    'ActiveOverlay',
    'Text.TitleBarSecondary')) {
    if ($window -notmatch $pattern) { $failures.Add("MainWindow is missing human-centered shell contract: $pattern") }
}
if ($window -match 'Cursor="Hand"') { $failures.Add("Global hand cursor policy must remain removed.") }
if (($window | Select-String -Pattern 'ContentControl' -AllMatches).Matches.Count -ne 1) { $failures.Add("MainWindow must retain exactly one workspace ContentControl.") }
foreach ($caption in @("CaptionMinimizeButton", "CaptionMaximizeButton", "CaptionCloseButton")) {
    Require-Pattern "ReplayFoundry.Desktop/Shell/MainWindow.xaml" $caption "Caption control is missing: $caption"
}
if ($window -notmatch 'shell:WindowChrome\.IsHitTestVisibleInChrome="True"') { $failures.Add("Custom chrome controls must opt into chrome hit testing.") }
if ($window -notmatch 'Brush\.WindowGrid') { $failures.Add("Every workspace must retain the shared scalable brand grid.") }
if ($window -match 'MainWindowBackground|Assets/Icons/Dock') { $failures.Add("The shell must not restore retired neon raster artwork.") }

Require-Pattern "ReplayFoundry.Desktop/app.manifest" "PerMonitorV2" "PerMonitorV2 DPI awareness must be declared in the manifest."
Require-Pattern "ReplayFoundry.Desktop/ReplayFoundry.Desktop.csproj" "ApplicationManifest" "The desktop project must consume its DPI manifest."
Require-Pattern "ReplayFoundry.Desktop/Shell/Windowing/MainWindowNativeBehavior.cs" "WmNcHitTest|WmGetMinMaxInfo" "Native window behavior must own hit testing and max bounds."
Require-Pattern "ReplayFoundry.Desktop/Shell/Windowing/MainWindowNativeBehavior.cs" "HtMaxButton|9" "Snap Layouts require HTMAXBUTTON hit testing."
Require-Pattern "ReplayFoundry.Desktop/Shell/Windowing/MainWindowNativeBehavior.cs" "GetMonitorInfo|MonitorWorkArea" "Max bounds must use the monitor work area, including taskbar exclusion."
Require-Pattern "ReplayFoundry.Desktop/Shell/Windowing/WindowWorkAreaCalculator.cs" "DipToPixels" "Mixed-DPI minimum tracking must convert device-independent units."
Require-Pattern "ReplayFoundry.Desktop/Shell/Windowing/ResponsiveReadabilityState.cs" "TextScale|Dpi|Height" "Responsive state must include width, height, text scale, and DPI."
Require-Pattern "ReplayFoundry.Desktop/Resources/Theme/Motion.xaml" "Motion\.Reduced" "The shared reduced-motion policy must remain available."
Require-Pattern "ReplayFoundry.Desktop/Shell/Dock/FloatingDock.xaml" "IconPath|Icon\.Spark|Icon\.Settings" "The dock must use scalable semantic icons."

Require-Pattern "ReplayFoundry.Desktop/Shell/Guidance/GuidanceViewModels.cs" "FoundryGuideViewModel|ShortcutReferenceViewModel|CommandPaletteViewModel" "Searchable guidance view models are required."
Require-Pattern "ReplayFoundry.Desktop/Shell/Guidance/GuidanceViewModels.cs" "FilteredEntries|SearchText" "Guidance surfaces must expose searchable collections."
Require-Pattern "ReplayFoundry.Desktop/Shell/Guidance/GuidanceViews.xaml" "AutomationProperties.Name|KeyboardNavigation.TabNavigation" "Guidance controls need automation names and a bounded keyboard loop."
Require-Pattern "ReplayFoundry.Desktop/Shell/Guidance/GuidanceViews.xaml" "FocusOnLoadBehavior|Key="Enter"" "Search surfaces must receive focus and command palette must support Enter."
Require-Pattern "ReplayFoundry.Desktop/Shell/MainWindowViewModel.cs" "OpenGuideCommand|OpenCommandPaletteCommand|OpenShortcutReferenceCommand" "Guidance commands must stay in the shell view model."
Require-Pattern "ReplayFoundry.Desktop/Presentation/Feedback/UserFacingIssue.cs" "RF-\[A-Z\].*000|IssueReference" "Stable issue references must be validated."
Require-Pattern "ReplayFoundry.Desktop/Presentation/Controls/IssuePanel.xaml" "Expander|Technical details|LiveSetting" "Issue details must be collapsed and announced accessibly."
Require-Pattern "ReplayFoundry.Desktop/Features/Generate/Progress/GenerationProgressView.xaml" 'Control\.DisclosureExpander' "Generate failure details need an explicit disclosure control."
Require-Pattern "ReplayFoundry.Desktop/Features/Generate/Progress/GenerationProgressView.xaml" 'HorizontalScrollBarVisibility="Disabled"' "Generate failure details must not expose a scrollbar-corner resize lookalike."
Require-Pattern "ReplayFoundry.Desktop/Features/Generate/Progress/GenerationProgressView.xaml" 'TextWrapping="Wrap"' "Generate failure details must wrap within the issue card."
Require-Pattern "ReplayFoundry.Desktop/Resources/Theme/HighContrast.xaml" "SystemColors" "High contrast resources must use system colors."
Require-Pattern "ReplayFoundry.Desktop/Resources/Controls/ButtonStyles.xaml" 'Cursor" Value="Arrow"' "Interactive buttons must not force a hand cursor."
Require-Pattern "ReplayFoundry.Desktop/Resources/Theme/Dimensions.xaml" "InteractiveTarget.*40" "The default interactive target must be at least 40 device-independent pixels."
Require-Pattern "ReplayFoundry.Desktop/Features/Studio/StudioView.xaml" "RF-STU-001" "Studio errors must use a stable user-facing reference."
Require-Pattern "ReplayFoundry.Desktop/Features/Settings/SettingsView.xaml" "SettingsSectionHostView" "Settings must render the shared section host in standard and compact layouts."
Require-Pattern "ReplayFoundry.Desktop/Features/Settings/Sections/SettingsSectionViews.xaml.cs" "InitializeComponent" "Settings section views must initialize their XAML content."
Require-Pattern "ReplayFoundry.Desktop/Features/Studio/StudioSectionViews.xaml.cs" "InitializeComponent" "Studio section views must initialize their XAML content."
Require-Pattern "ReplayFoundry.Desktop/Features/Library/Sections/LibrarySectionViews.xaml.cs" "InitializeComponent" "Library section views must initialize their XAML content."
Require-Pattern "ReplayFoundry.Desktop/Features/Publish/Sections/PublishSectionViews.xaml.cs" "InitializeComponent" "Publish section views must initialize their XAML content."
foreach ($viewModel in @(
    "ReplayFoundry.Desktop/Features/Studio/StudioViewModel.cs",
    "ReplayFoundry.Desktop/Features/Library/LibraryViewModel.cs",
    "ReplayFoundry.Desktop/Features/Publish/PublishViewModel.cs")) {
    Require-Pattern $viewModel 'ShouldShowPlaceholder => IsUnavailable \|\| IsError' "$viewModel must preserve useful workspace anatomy while content is empty."
}

$testText = Read-RepoText "ReplayFoundry.PreparationTests/UiUxApplicationSurfaceTests.cs"
foreach ($testName in @("Ui04StartupPolicyIsExplicit", "Ui04CaptionHitTestingIsExplicit", "Ui04WorkAreaBoundsPreserveWorkArea", "Ui04ResponsiveReadabilityIsExplicit", "Ui04GuidanceSurfacesAreSearchable", "Ui04IssueReferencesAreStable")) {
    if ($testText -notmatch $testName) { $failures.Add("Focused UI-04 harness test is missing: $testName") }
}
foreach ($testName in @("EmptyWorkspacesPreserveAnatomy", "PublishOutputDraftUpdatesChecklist")) {
    if ($testText -notmatch $testName) { $failures.Add("Workspace feature harness test is missing: $testName") }
}

foreach ($sourcePath in @(
    "ReplayFoundry.Desktop/Shell/MainWindow.xaml.cs",
    "ReplayFoundry.Desktop/Shell/Windowing/MainWindowNativeBehavior.cs",
    "ReplayFoundry.Desktop/Shell/Windowing/WindowChromeInteraction.cs")) {
    $lineCount = (Get-Content -LiteralPath (Join-Path $repositoryRoot $sourcePath)).Count
    $limit = if ($sourcePath -like "*MainWindow.xaml.cs") { 120 } elseif ($sourcePath -like "*MainWindowNativeBehavior.cs") { 250 } else { 260 }
    if ($lineCount -gt $limit) { $failures.Add("$sourcePath exceeds its UI-04 line limit ($lineCount/$limit).") }
}

if ($failures.Count -gt 0) {
    Write-Error ("UI/UX human-centered experience guard failed: " + ($failures -join " | "))
    exit 1
}

Write-Host "UI/UX human-centered experience guard passed: windowing, DPI, Snap hit testing, guidance, issues, targets, motion, high contrast, and focused tests inspected."
