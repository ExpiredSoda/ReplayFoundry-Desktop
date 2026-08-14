param(
    [switch]$RequireTrackedSources,
    [string]$UiScopeRef
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$baseRef = "origin/codex/manual-video-layout-review"
$failures = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()

function Add-Failure { param([string]$Message) $failures.Add($Message) }
function Add-Warning { param([string]$Message) $warnings.Add($Message) }
function Read-RepoText { param([string]$RelativePath) Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot $RelativePath) }
function Test-RepoPath { param([string]$RelativePath) Test-Path -LiteralPath (Join-Path $repositoryRoot $RelativePath) }

function Get-ChangedPathSet {
    $paths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $mergeBase = (& git -C $repositoryRoot merge-base HEAD $baseRef).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($mergeBase)) { Add-Failure "Unable to resolve the UI merge base with $baseRef." }
    else {
        (& git -C $repositoryRoot diff --name-only "$mergeBase..HEAD") | ForEach-Object { [void]$paths.Add($_.Trim()) }
    }
    (& git -C $repositoryRoot diff --name-only) | ForEach-Object { [void]$paths.Add($_.Trim()) }
    (& git -C $repositoryRoot diff --cached --name-only) | ForEach-Object { [void]$paths.Add($_.Trim()) }
    (& git -C $repositoryRoot status --porcelain=v1 --untracked-files=all) | ForEach-Object {
        if ($_.Length -ge 4) { [void]$paths.Add($_.Substring(3).Trim('"')) }
    }
    return $paths
}

function Get-UiScopePathSet {
    param([System.Collections.Generic.HashSet[string]]$FallbackPaths)

    if ([string]::IsNullOrWhiteSpace($UiScopeRef)) { return $FallbackPaths }

    $paths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $resolvedRef = (& git -C $repositoryRoot rev-parse --verify "$UiScopeRef^{commit}" 2>$null).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($resolvedRef)) {
        Add-Failure "Unable to resolve the UI scope ref $UiScopeRef."
        return $paths
    }

    (& git -C $repositoryRoot diff-tree --no-commit-id --name-only -r $resolvedRef) |
        ForEach-Object { [void]$paths.Add($_.Trim()) }
    return $paths
}

function Test-Ignored([string]$RelativePath) {
    & git -C $repositoryRoot check-ignore --quiet -- $RelativePath
    return $LASTEXITCODE -eq 0
}

function Test-Tracked([string]$RelativePath) {
    $tracked = & git -C $repositoryRoot ls-files --error-unmatch -- $RelativePath 2>$null
    return $LASTEXITCODE -eq 0 -and $tracked
}

function Assert-Contains {
    param([string]$RelativePath, [string]$Pattern, [string]$Message)
    if ((Read-RepoText $RelativePath) -notmatch $Pattern) { Add-Failure $Message }
}

$requiredPublishFiles = @(
    "ReplayFoundry.Desktop/Features/Publish/PublishView.xaml",
    "ReplayFoundry.Desktop/Features/Publish/PublishView.xaml.cs",
    "ReplayFoundry.Desktop/Features/Publish/PublishViewModel.cs",
    "ReplayFoundry.Desktop/Features/Publish/Sections/PublishCalendarView.xaml")

foreach ($file in $requiredPublishFiles) {
    if (-not (Test-RepoPath $file)) { Add-Failure "Required Publish source is missing: $file" }
    if (Test-Ignored $file) { Add-Failure "Required Publish source is ignored: $file" }
    if (-not (Test-Tracked $file)) {
        if ($RequireTrackedSources) { Add-Failure "Required Publish source is not tracked: $file" }
        else { Add-Warning "Pending tracking after UI-02 commit: $file" }
    }
}

$trackedFiles = @(& git -C $repositoryRoot ls-files)
if ($trackedFiles.Count -eq 0) { Add-Failure "git ls-files returned no repository payload." }

$changedPaths = Get-ChangedPathSet
if (-not [string]::IsNullOrWhiteSpace($UiScopeRef)) {
    $uiScopePaths = Get-UiScopePathSet $changedPaths
    $protectedPattern = '^ReplayFoundry\.Desktop/(Media|Platform|DeveloperTools)/'
    foreach ($changed in $uiScopePaths) {
        if ($changed -match $protectedPattern) { Add-Failure "UI-only scope changed a protected backend path: $changed" }
    }
}

$mainWindow = Read-RepoText "ReplayFoundry.Desktop/Shell/MainWindow.xaml"
$mainWindowCode = Read-RepoText "ReplayFoundry.Desktop/Shell/MainWindow.xaml.cs"
$shellViewModel = Read-RepoText "ReplayFoundry.Desktop/Shell/MainWindowViewModel.cs"
$appXaml = Read-RepoText "ReplayFoundry.Desktop/App.xaml"
$appCode = Read-RepoText "ReplayFoundry.Desktop/App.xaml.cs"
$compositionCode = Read-RepoText "ReplayFoundry.Desktop/ApplicationCompositionRoot.cs"

if (([regex]::Matches($mainWindow, '<ContentControl\b')).Count -ne 1) { Add-Failure "MainWindow must contain exactly one ContentControl." }
Assert-Contains "ReplayFoundry.Desktop/Shell/MainWindow.xaml" 'Content="\{Binding CurrentWorkspace\}"' "MainWindow must bind its one ContentControl to CurrentWorkspace."
if ($mainWindowCode -match 'PropertyChanged|CurrentDestination|ShellDestination|\.Content\s*=|\.Visibility\s*=|WorkspaceContent') { Add-Failure "MainWindow code-behind crosses the shell MVVM boundary." }
if ($shellViewModel -match 'public\s+MainWindowViewModel\s*\(\s*GenerateViewModel\s+generateViewModel\s*\)') { Add-Failure "The one-argument shell constructor must remain removed." }
if ($appXaml -match 'x:Key="[^" ]*WorkspaceTemplate') { Add-Failure "Workspace DataTemplates must remain implicit rather than keyed." }
foreach ($type in @('Generate', 'Studio', 'Library', 'Publish', 'Settings')) {
    $templatePattern = 'DataType="{x:Type [a-z]+:' + $type + 'ViewModel}"'
    if ($appXaml -notmatch $templatePattern) { Add-Failure "Implicit DataTemplate is missing for ${type}ViewModel." }
    if ($compositionCode -notmatch "new\s+${type}ViewModel") { Add-Failure "App composition does not construct ${type}ViewModel." }
}
if ($appXaml -match 'DesignTime') { Add-Failure "Design-time types must not appear in production composition." }

$featureRoots = @('Studio', 'Library', 'Publish', 'Settings')
foreach ($feature in $featureRoots) {
    $root = "ReplayFoundry.Desktop/Features/$feature"
    $viewModels = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot $root) -Recurse -File -Filter '*ViewModel.cs' |
        Where-Object { $_.FullName -notmatch '[\\/]DesignTime[\\/]' }
    foreach ($file in $viewModels) {
        $text = Get-Content -Raw -LiteralPath $file.FullName
        if ($text -cmatch 'System\.Windows\.(?!Input\b)|\b(UserControl|Window|MessageBox|Application)\b|DeveloperTools|ReplayFoundry\.Desktop\.(Media|Platform)|ProcessStartInfo|IProcessRunner|\bpartial\s+class\s+.*ViewModel') {
            Add-Failure "Production feature ViewModel crosses a presentation-only boundary: $($file.FullName)"
        }
    }
}

$uiFilePattern = '^(ReplayFoundry\.Desktop/(App\.xaml|App\.xaml\.cs|Shell/|Features/(Generate|Studio|Library|Publish|Settings)/|Presentation/|Resources/)|ReplayFoundry\.PreparationTests/UiUxApplicationSurfaceTests\.cs|eng/Test-UiUxArchitecture\.ps1)'
foreach ($path in $changedPaths) {
    if ($path -notmatch $uiFilePattern -or -not (Test-RepoPath $path)) { continue }
    $extension = [System.IO.Path]::GetExtension($path).ToLowerInvariant()
    $content = Get-Content -LiteralPath (Join-Path $repositoryRoot $path)
    $maxLength = ($content | ForEach-Object { $_.Length } | Measure-Object -Maximum).Maximum
    if ($extension -eq '.xaml' -and $maxLength -gt 240) { Add-Failure "XAML line exceeds 240 characters: $path ($maxLength)." }
    if ($extension -eq '.cs' -and $maxLength -gt 180 -and $path -notmatch 'UiUxApplicationSurfaceTests\.cs$') { Add-Failure "UI code line exceeds 180 characters: $path ($maxLength)." }
    if ($extension -eq '.xaml' -and $path -match 'Features/(Generate|Studio|Library|Publish|Settings)/' -and ($content -join "`n") -match '#[0-9A-Fa-f]{6,8}') { Add-Failure "Feature XAML contains a hard-coded color: $path" }
    if ($path -match 'Features/(Studio|Library|Publish|Settings)/') {
        $segments = $path -split '/'
        if ($segments.Count -gt 5) { Add-Failure "Feature folder depth exceeds two levels: $path" }
    }
}

foreach ($name in @('Common', 'Shared', 'Helpers', 'Utils', 'Managers', 'Everything')) {
    if (Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'ReplayFoundry.Desktop') -Directory -Recurse | Where-Object Name -eq $name) { Add-Failure "Generic dumping-ground folder is present: $name" }
}

Assert-Contains "ReplayFoundry.Desktop/Features/Studio/Browser/StudioBrowserView.xaml" 'ToolSections' "Studio browser binding is missing."
Assert-Contains "ReplayFoundry.Desktop/Features/Library/LibraryView.xaml" 'LibraryContentView' "Library content decomposition is missing."
Assert-Contains "ReplayFoundry.Desktop/Features/Publish/PublishView.xaml" 'PublishLibraryBrowserView' "Publish Library browser decomposition is missing."
Assert-Contains "ReplayFoundry.Desktop/Features/Publish/PublishPreparationWindow.xaml" 'PublishMetadataView' "Publish preparation metadata decomposition is missing."
Assert-Contains "ReplayFoundry.Desktop/Features/Settings/SettingsView.xaml" 'SettingsSectionHostView' "Settings section decomposition is missing."
Assert-Contains "ReplayFoundry.Desktop/Features/Settings/Sections/AiModelsSettingsView.xaml" 'AiCapabilities' "Settings AI capability list is missing."
Assert-Contains "ReplayFoundry.Desktop/Features/Settings/Sections/StorageSettingsView.xaml" 'OutputRootDirectory' "Functional output-folder binding is missing."

$generateStyles = Read-RepoText "ReplayFoundry.Desktop/Features/Generate/GenerateStyles.xaml"
$generationSetupStyles = Read-RepoText "ReplayFoundry.Desktop/Features/Generate/GenerationSetup/GenerationSetupStyles.xaml"
$buttonStyles = Read-RepoText "ReplayFoundry.Desktop/Resources/Controls/ButtonStyles.xaml"
if ($generateStyles -notmatch 'x:Key="Generate\.ActionButton"\s+BasedOn="\{StaticResource Control\.ThemedButton\}"') {
    Add-Failure "Generate action buttons must inherit the shared themed-button foundation."
}
foreach ($styleName in @('StepButton', 'SecondaryButton')) {
    $pattern = 'x:Key="GenerationSetup\.' + $styleName + '"\s+BasedOn="\{StaticResource Control\.ThemedButton\}"'
    if ($generationSetupStyles -notmatch $pattern) {
        Add-Failure "Generation Setup $styleName must inherit the shared themed-button foundation."
    }
}
if ($generationSetupStyles -match 'FocusVisualStyle"\s+Value="\{x:Null\}"') {
    Add-Failure "Generation Setup buttons must retain the shared keyboard-focus visual."
}
if ($generateStyles -match 'Brush\.Generate(Panel|Border|DropZone|Button|ButtonHover|PrimaryButton|FileRow)' -or
    $generationSetupStyles -match 'Brush\.GenerationSetup') {
    Add-Failure "Generate styles must use the canonical shared theme brushes rather than local aliases."
}
if ($buttonStyles -notmatch 'Control\.IconButton[\s\S]+Dimension\.InteractiveTarget') {
    Add-Failure "Shared icon buttons must use the interactive-target dimension token."
}

$architectureGuardLines = (Get-Content -LiteralPath $MyInvocation.MyCommand.Path).Count
if ($architectureGuardLines -gt 500) { Add-Failure "UI architecture guard exceeds the 500-line maximum ($architectureGuardLines)." }

if ($warnings.Count -gt 0) { Write-Warning ($warnings -join "`n") }
if ($failures.Count -gt 0) {
    Write-Error ("UI/UX architecture guard failed:`n- " + ($failures -join "`n- "))
    exit 1
}

Write-Host "UI/UX architecture guard passed: merge-base changes, working-tree changes, Publish tracking, shell MVVM, boundaries, formatting, decomposition, and state surfaces inspected."
