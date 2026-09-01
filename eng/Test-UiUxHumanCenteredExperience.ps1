param()

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$failures = [System.Collections.Generic.List[string]]::new()

function Read-RepoText { param([string]$Path) Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot $Path) }
function Require-Path { param([string]$Path) if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $Path))) { $failures.Add("Missing UI-04 path: $Path") } }
function Require-Pattern { param([string]$Path, [string]$Pattern, [string]$Message) if ((Read-RepoText $Path) -notmatch $Pattern) { $failures.Add($Message) } }
function Reject-Pattern { param([string]$Path, [string]$Pattern, [string]$Message) if ((Read-RepoText $Path) -match $Pattern) { $failures.Add($Message) } }

$requiredPaths = @(
    "src/ReplayFoundry.Desktop/app.manifest",
    "src/ReplayFoundry.Desktop/Shell/Windowing/WindowStartupPolicy.cs",
    "src/ReplayFoundry.Desktop/Shell/Windowing/WindowWorkAreaCalculator.cs",
    "src/ReplayFoundry.Desktop/Shell/Windowing/MainWindowNativeBehavior.cs",
    "src/ReplayFoundry.Desktop/Shell/Windowing/WindowChromeInteraction.cs",
    "src/ReplayFoundry.Desktop/Presentation/Responsive/ResponsiveLayout.cs",
    "src/ReplayFoundry.Desktop/Shell/Guidance/GuidanceViewModels.cs",
    "src/ReplayFoundry.Desktop/Shell/Guidance/GuidanceViews.xaml",
    "src/ReplayFoundry.Desktop/Assets/Branding/ReplayFoundry-App-Icon-1024.png",
    "src/ReplayFoundry.Desktop/Assets/Branding/favicon.svg",
    "src/ReplayFoundry.Desktop/Features/Settings/Sections/SettingsSectionViews.xaml.cs",
    "src/ReplayFoundry.Desktop/Features/Studio/StudioSectionViews.xaml.cs",
    "src/ReplayFoundry.Desktop/Features/Library/Sections/LibrarySectionViews.xaml.cs",
    "src/ReplayFoundry.Desktop/Features/Publish/Sections/PublishSectionViews.xaml.cs",
    "src/ReplayFoundry.Desktop/Features/Publish/Sections/PublishOutputSettingsView.xaml",
    "src/ReplayFoundry.Desktop/Presentation/Feedback/UserFacingIssue.cs",
    "src/ReplayFoundry.Desktop/Presentation/Controls/IssuePanel.xaml",
    "src/ReplayFoundry.Desktop/Presentation/Controls/TimePickerField.xaml",
    "src/ReplayFoundry.Desktop/Presentation/Accessibility/CursorPolicy.cs",
    "src/ReplayFoundry.Desktop/Presentation/Accessibility/FocusOnLoadBehavior.cs")
foreach ($path in $requiredPaths) { Require-Path $path }

$window = Read-RepoText "src/ReplayFoundry.Desktop/Shell/MainWindow.xaml"
foreach ($pattern in @(
    'WindowState="Maximized"',
    'ShowInTaskbar="True"',
    'Topmost="False"',
    'ResizeMode="CanResize"',
    'WindowChrome\.WindowChrome',
    'Panel\.ZIndex="100"',
    'Panel\.ZIndex="90"',
    'MinWidth="760"',
    'MinHeight="600"',
    'CaptionHelpButton',
    'Key="F1"',
    'Key="K" Modifiers="Control"',
    'Key="Oem2" Modifiers="Control"',
    'ActiveOverlay',
    'Text.TitleBarSecondary')) {
    if ($window -notmatch $pattern) { $failures.Add("MainWindow is missing human-centered shell contract: $pattern") }
}
$handCursor = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot "src/ReplayFoundry.Desktop") -Recurse -Filter *.xaml |
    Select-String -Pattern 'Cursor="Hand"' -SimpleMatch
if ($handCursor) { $failures.Add("Desktop XAML must follow the shared Arrow cursor policy.") }
if (($window | Select-String -Pattern 'ContentControl' -AllMatches).Matches.Count -ne 1) { $failures.Add("MainWindow must retain exactly one workspace ContentControl.") }
foreach ($caption in @("CaptionMinimizeButton", "CaptionMaximizeButton", "CaptionCloseButton")) {
    Require-Pattern "src/ReplayFoundry.Desktop/Shell/MainWindow.xaml" $caption "Caption control is missing: $caption"
}
if ($window -notmatch 'shell:WindowChrome\.IsHitTestVisibleInChrome="True"') { $failures.Add("Custom chrome controls must opt into chrome hit testing.") }
if ($window -notmatch 'Brush\.WindowGrid') { $failures.Add("Every workspace must retain the shared scalable brand grid.") }
if ($window -match 'MainWindowBackground|Assets/Icons/Dock') { $failures.Add("The shell must not restore retired neon raster artwork.") }

Require-Pattern "src/ReplayFoundry.Desktop/app.manifest" "PerMonitorV2" "PerMonitorV2 DPI awareness must be declared in the manifest."
Require-Pattern "src/ReplayFoundry.Desktop/ReplayFoundry.Desktop.csproj" "ApplicationManifest" "The desktop project must consume its DPI manifest."
Require-Pattern "src/ReplayFoundry.Desktop/Shell/Windowing/MainWindowNativeBehavior.cs" "WmNcHitTest|WmGetMinMaxInfo" "Native window behavior must own hit testing and max bounds."
Require-Pattern "src/ReplayFoundry.Desktop/Shell/Windowing/MainWindowNativeBehavior.cs" "HtMaxButton|9" "Snap Layouts require HTMAXBUTTON hit testing."
Require-Pattern "src/ReplayFoundry.Desktop/Shell/Windowing/MainWindowNativeBehavior.cs" "GetMonitorInfo|MonitorWorkArea" "Max bounds must use the monitor work area, including taskbar exclusion."
Require-Pattern "src/ReplayFoundry.Desktop/Shell/Windowing/WindowWorkAreaCalculator.cs" "DipToPixels" "Mixed-DPI minimum tracking must convert device-independent units."
Require-Pattern "src/ReplayFoundry.Desktop/Presentation/Responsive/ResponsiveLayout.cs" "StandardMinimumWidth|WideMinimumWidth|ForWidth" "Responsive width bands must have one shared implementation."
Require-Pattern "src/ReplayFoundry.Desktop/App.xaml.cs" "SystemParameters\.ClientAreaAnimation|Motion\.Hover" "Reduced motion must update the duration resources animations actually consume."
Require-Pattern "src/ReplayFoundry.Desktop/Shell/Dock/FloatingDock.xaml" "IconPath|Icon\.Spark|Icon\.Settings" "The dock must use scalable semantic icons."

Require-Pattern "src/ReplayFoundry.Desktop/Shell/Guidance/GuidanceViewModels.cs" "FoundryGuideViewModel|ShortcutReferenceViewModel|CommandPaletteViewModel" "Searchable guidance view models are required."
Require-Pattern "src/ReplayFoundry.Desktop/Shell/Guidance/GuidanceViewModels.cs" "FilteredEntries|SearchText" "Guidance surfaces must expose searchable collections."
Require-Pattern "src/ReplayFoundry.Desktop/Shell/Guidance/GuidanceViews.xaml" "AutomationProperties.Name|KeyboardNavigation.TabNavigation" "Guidance controls need automation names and a bounded keyboard loop."
Require-Pattern "src/ReplayFoundry.Desktop/Shell/Guidance/GuidanceViews.xaml" "FocusOnLoadBehavior|Key="Enter"" "Search surfaces must receive focus and command palette must support Enter."
Require-Pattern "src/ReplayFoundry.Desktop/Shell/MainWindowViewModel.cs" "OpenGuideCommand|OpenCommandPaletteCommand|OpenShortcutReferenceCommand" "Guidance commands must stay in the shell view model."
Require-Pattern "src/ReplayFoundry.Desktop/Presentation/Feedback/UserFacingIssue.cs" "RF-\[A-Z\].*000|IssueReference" "Stable issue references must be validated."
Require-Pattern "src/ReplayFoundry.Desktop/Presentation/Controls/IssuePanel.xaml" 'Expander[\s\S]*Header="More details"[\s\S]*Support reference' "Issue support details and reference must stay inside the collapsed plain-language disclosure."
Require-Pattern "src/ReplayFoundry.Desktop/Presentation/Controls/IssuePanel.xaml" "LiveSetting" "Issue summaries must be announced accessibly."

$ordinaryCopyPattern = '(?i)(?:Text|Content|Header|ToolTip|AutomationProperties\.(?:Name|HelpText))\s*=\s*"(?!\{Binding)[^"]*(?:\bcandidates?\b|\bdeterministic\b|\bheuristics?\b|\bbounded\b|\bmetadata\b|\btelemetry\b|\bdiagnostics?\b|\bproviders?\b|\bruntimes?\b|\bcapabilit(?:y|ies)\b|\bentity ID\b|\bWikidata ID\b|internal analysis traits|in-memory presentation|durable local catalog|technical details|\brerolls?\b)[^"]*"'
$ordinaryCopyMatches = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot "src/ReplayFoundry.Desktop") -Recurse -Filter *.xaml |
    Select-String -Pattern $ordinaryCopyPattern
if ($ordinaryCopyMatches) {
    $locations = $ordinaryCopyMatches | ForEach-Object { "$($_.Path):$($_.LineNumber)" }
    $failures.Add("Ordinary XAML copy must use task language instead of developer vocabulary: $($locations -join ', ')")
}

foreach ($copyContract in @(
    @{ Path = "src/ReplayFoundry.Desktop/Shell/MainWindowViewModel.cs"; Pattern = "capability boundaries" },
    @{ Path = "src/ReplayFoundry.Desktop/Features/Settings/SettingsViewModel.cs"; Pattern = "automatic telemetry|speech-assisted analysis|local video signals|reroll choice" },
    @{ Path = "src/ReplayFoundry.Desktop/Features/Settings/BugReportSettingsViewModel.cs"; Pattern = "diagnostic attachments?" },
    @{ Path = "src/ReplayFoundry.Desktop/Features/Settings/CreatorVoiceSettingsViewModel.cs"; Pattern = "drafts and rerolls" },
    @{ Path = "src/ReplayFoundry.Desktop/Features/Generate/Evidence/GenerationEvidenceProgressTranslator.cs"; Pattern = "deterministic media passes|audio stream|source evidence" },
    @{ Path = "src/ReplayFoundry.Desktop/Features/Generate/Workflow/GenerationPreflightRunner.cs"; Pattern = "Deterministic evidence ready|structural inspection|global-audio evidence" },
    @{ Path = "src/ReplayFoundry.Desktop/Composition/EditorialComposition.cs"; Pattern = "Qualified local AI metadata could not start" },
    @{ Path = "src/ReplayFoundry.Desktop/Composition/LocalIntelligenceComposition.cs"; Pattern = "Qualified local AI metadata could not start" },
    @{ Path = "src/ReplayFoundry.Desktop/Platform/Dialogs/LocalDataCleanupConfirmationWindow.xaml.cs"; Pattern = "Temporary cache files|Diagnostic logs|Library catalog records" },
    @{ Path = "src/ReplayFoundry.Desktop/Platform/Media/FfmpegStudioClipRenderingService.cs"; Pattern = "candidate before rendering" },
    @{ Path = "src/ReplayFoundry.Desktop/Platform/Media/FfmpegToolLocator.cs"; Pattern = "verified .* runtime|development copy under" },
    @{ Path = "src/ReplayFoundry.Desktop/Platform/Media/FfmpegEvidenceOutputLimitEstimator.cs"; Pattern = "in-memory metadata limit" },
    @{ Path = "src/ReplayFoundry.Desktop/Platform/RuntimePacks/RuntimePackMaintenanceLauncher.cs"; Pattern = "runtime maintenance tool" },
    @{ Path = "src/ReplayFoundry.Desktop/Platform/YouTube/YouTubePublishingService.cs"; Pattern = "requested metadata were accepted" })) {
    Reject-Pattern $copyContract.Path $copyContract.Pattern "User-facing copy regressed to developer vocabulary in $($copyContract.Path)."
}
Require-Pattern "src/ReplayFoundry.Desktop/Features/Generate/Progress/GenerationProgressView.xaml" 'Control\.DisclosureExpander' "Generate failure details need an explicit disclosure control."
Require-Pattern "src/ReplayFoundry.Desktop/Features/Generate/Progress/GenerationProgressView.xaml" 'HorizontalScrollBarVisibility="Disabled"' "Generate failure details must not expose a scrollbar-corner resize lookalike."
Require-Pattern "src/ReplayFoundry.Desktop/Features/Generate/Progress/GenerationProgressView.xaml" 'TextWrapping="Wrap"' "Generate failure details must wrap within the issue card."
Require-Pattern "src/ReplayFoundry.Desktop/Resources/Theme/HighContrast.xaml" "SystemColors" "High contrast resources must use system colors."
Require-Pattern "src/ReplayFoundry.Desktop/Presentation/Accessibility/HighContrastThemeController.cs" "Brush\.TextPrimary|SystemColors\.WindowTextBrush|RestoreBrandPalette" "High contrast must replace and restore semantic feature brushes."
Require-Pattern "src/ReplayFoundry.Desktop/Resources/Controls/ButtonStyles.xaml" 'Cursor" Value="Arrow"' "Interactive buttons must not force a hand cursor."
Require-Pattern "src/ReplayFoundry.Desktop/Resources/Theme/Dimensions.xaml" "InteractiveTarget.*40" "The default interactive target must be at least 40 device-independent pixels."
Require-Pattern "src/ReplayFoundry.Desktop/Presentation/Controls/TimePickerField.xaml" 'x:Name="DoneButton"[\s\S]*?Height="40"' "The time-picker completion action must retain a 40-DIP interaction target."
Require-Pattern "src/ReplayFoundry.Desktop/Features/Studio/StudioView.xaml" "RF-STU-001" "Studio errors must use a stable user-facing reference."
Require-Pattern "src/ReplayFoundry.Desktop/Features/Settings/SettingsView.xaml" "SettingsSectionHostView" "Settings must render its shared section host."
if (($settings = Read-RepoText "src/ReplayFoundry.Desktop/Features/Settings/SettingsView.xaml") -and (($settings | Select-String -Pattern 'SettingsSectionHostView' -AllMatches).Matches.Count -ne 1)) { $failures.Add("Settings must construct exactly one responsive section host.") }
Require-Pattern "src/ReplayFoundry.Desktop/Features/Settings/Sections/SettingsSectionViews.xaml.cs" "InitializeComponent" "Settings section views must initialize their XAML content."
Require-Pattern "src/ReplayFoundry.Desktop/Features/Studio/StudioSectionViews.xaml.cs" "InitializeComponent" "Studio section views must initialize their XAML content."
Require-Pattern "src/ReplayFoundry.Desktop/Features/Library/Sections/LibrarySectionViews.xaml.cs" "InitializeComponent" "Library section views must initialize their XAML content."
Require-Pattern "src/ReplayFoundry.Desktop/Features/Publish/Sections/PublishSectionViews.xaml.cs" "InitializeComponent" "Publish section views must initialize their XAML content."
foreach ($viewModel in @(
    "src/ReplayFoundry.Desktop/Features/Studio/StudioViewModel.cs",
    "src/ReplayFoundry.Desktop/Features/Library/LibraryViewModel.cs",
    "src/ReplayFoundry.Desktop/Features/Publish/PublishViewModel.cs")) {
    Require-Pattern $viewModel 'ShouldShowPlaceholder => IsUnavailable \|\| IsError' "$viewModel must preserve useful workspace anatomy while content is empty."
}

$testText = Read-RepoText "tests/ReplayFoundry.PreparationTests/UiUxApplicationSurfaceTests.cs"
foreach ($testName in @("Ui04StartupPolicyIsExplicit", "Ui04CaptionHitTestingIsExplicit", "Ui04WorkAreaBoundsPreserveWorkArea", "Ui04AvoidsFalseTextScaleState", "Ui04GuidanceSurfacesAreSearchable", "Ui04IssueReferencesAreStable")) {
    if ($testText -notmatch $testName) { $failures.Add("Focused UI-04 harness test is missing: $testName") }
}
foreach ($testName in @("EmptyWorkspacesPreserveAnatomy", "PublishOutputDraftUpdatesChecklist")) {
    if ($testText -notmatch $testName) { $failures.Add("Workspace feature harness test is missing: $testName") }
}

foreach ($sourcePath in @(
    "src/ReplayFoundry.Desktop/Shell/MainWindow.xaml.cs",
    "src/ReplayFoundry.Desktop/Shell/Windowing/MainWindowNativeBehavior.cs",
    "src/ReplayFoundry.Desktop/Shell/Windowing/WindowChromeInteraction.cs")) {
    $lineCount = (Get-Content -LiteralPath (Join-Path $repositoryRoot $sourcePath)).Count
    $limit = if ($sourcePath -like "*MainWindow.xaml.cs") { 120 } elseif ($sourcePath -like "*MainWindowNativeBehavior.cs") { 250 } else { 260 }
    if ($lineCount -gt $limit) { $failures.Add("$sourcePath exceeds its UI-04 line limit ($lineCount/$limit).") }
}

if ($failures.Count -gt 0) {
    Write-Error ("UI/UX human-centered experience guard failed: " + ($failures -join " | "))
    exit 1
}

Write-Host "UI/UX human-centered experience guard passed: windowing, DPI, Snap hit testing, guidance, issues, targets, motion, high contrast, and focused tests inspected."
