[CmdletBinding()]
param([string]$RepositoryRoot)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Join-Path $PSScriptRoot '..'
}

$root = [IO.Path]::GetFullPath($RepositoryRoot)
$failures = [Collections.Generic.List[string]]::new()
$manifest = Import-PowerShellDataFile `
    -LiteralPath (Join-Path $root 'eng\ReplayFoundry.Repository.psd1')
$isPublicSnapshot = Test-Path -LiteralPath `
    (Join-Path $root '.replayfoundry-public-source') -PathType Leaf

function Add-Failure([string]$Message) { $failures.Add($Message) }

function ConvertTo-RepositoryPath([string]$Path) {
    return $Path.Replace('\', '/').TrimStart('/')
}

function Get-RepositoryPath([string]$Path) {
    return ConvertTo-RepositoryPath `
        ([IO.Path]::GetRelativePath($root, [IO.Path]::GetFullPath($Path)))
}

function Get-ManifestProjects {
    return @(
        $manifest.ProductProjects
        $manifest.ToolProjects
        $manifest.TestSupportProjects
        $manifest.DotNetTestProjects
    ) | ForEach-Object { ConvertTo-RepositoryPath $_ }
}

function Assert-SameSet(
    [string]$Name,
    [string[]]$Expected,
    [string[]]$Actual) {
    $missing = @($Expected | Where-Object { $_ -notin $Actual })
    $extra = @($Actual | Where-Object { $_ -notin $Expected })
    if ($missing.Count -or $extra.Count) {
        Add-Failure "$Name differs. Missing=[$($missing -join ', ')]; Extra=[$($extra -join ', ')]."
    }
}

function Assert-Layout {
    foreach ($directory in @('src', 'tools', 'tests', 'docs', 'eng', 'installer')) {
        if (-not (Test-Path -LiteralPath (Join-Path $root $directory) -PathType Container)) {
            Add-Failure "Required repository directory is missing: $directory"
        }
    }

    foreach ($legacy in @('ReplayFoundry.Desktop', 'ReplayFoundry.DeveloperTools')) {
        if (Test-Path -LiteralPath (Join-Path $root $legacy)) {
            Add-Failure "Legacy root project directory returned: $legacy"
        }
    }

    if (Test-Path -LiteralPath (Join-Path $root 'ReplayFoundry.Production.slnx')) {
        Add-Failure 'The duplicate production solution returned.'
    }
}

function Assert-ProjectInventory {
    $declared = @(Get-ManifestProjects)
    $presentDeclared = @($declared | Where-Object {
        Test-Path -LiteralPath (Join-Path $root $_) -PathType Leaf
    })
    $actual = @(Get-ChildItem (Join-Path $root 'src'), (Join-Path $root 'tools'), `
        (Join-Path $root 'tests') -Recurse -Filter '*.csproj' -File |
        ForEach-Object { Get-RepositoryPath $_.FullName })
    Assert-SameSet 'Project manifest' $presentDeclared $actual

    [xml]$solution = Get-Content -Raw -LiteralPath `
        (Join-Path $root $manifest.RepositorySolution)
    $solutionProjects = @($solution.SelectNodes('//Project') |
        ForEach-Object { ConvertTo-RepositoryPath $_.Path })
    Assert-SameSet 'Solution project set' $presentDeclared $solutionProjects

    foreach ($projectNode in $solution.SelectNodes('//Project')) {
        $project = ConvertTo-RepositoryPath $projectNode.Path
        $actualFolder = [string]$projectNode.ParentNode.GetAttribute('Name')
        $expectedFolder = Get-ExpectedSolutionFolder $project
        if ($actualFolder -ne $expectedFolder) {
            Add-Failure "Solution folder differs for $project. Expected='$expectedFolder'; Actual='$actualFolder'."
        }
    }
}

function Get-ExpectedSolutionFolder([string]$Project) {
    if ($Project -in $manifest.ProductProjects) { return '/Product/' }
    if ($Project -like 'tools/ReplayFoundry.RuntimeInstaller/*') {
        return '/Product Tools/'
    }
    if ($Project -like 'tools/ReplayFoundry.DeveloperTools/*') {
        return '/Developer Console/'
    }
    if ($Project -match 'ReplayFoundry\.(Composition|Inspection|Preparation)Tests') {
        return '/Verification/Desktop/'
    }
    if ($Project -match 'ReplayFoundry\.(DeveloperTools\.Tests|EvidenceTests)') {
        return '/Verification/Developer Console/'
    }
    if ($Project -match 'ReplayFoundry\.RuntimePacks\.Tests') {
        return '/Verification/Runtime Packs/'
    }
    if ($Project -in $manifest.TestSupportProjects) {
        return '/Verification/Shared/'
    }
    return ''
}

function Assert-ProjectReferences {
    foreach ($project in Get-ChildItem (Join-Path $root 'src'), `
        (Join-Path $root 'tools'), (Join-Path $root 'tests') `
        -Recurse -Filter '*.csproj' -File) {
        Assert-ProjectReference $project
    }
}

function Assert-ProjectReference([IO.FileInfo]$Project) {
    [xml]$document = Get-Content -Raw -LiteralPath $Project.FullName
    foreach ($reference in $document.SelectNodes('//ProjectReference')) {
        $target = [IO.Path]::GetFullPath((Join-Path $Project.DirectoryName $reference.Include))
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
            Add-Failure "Broken project reference in $($Project.Name): $($reference.Include)"
        }

        Assert-DependencyDirection (Get-RepositoryPath $Project.FullName) `
            (Get-RepositoryPath $target)
    }
}

function Assert-DependencyDirection([string]$Project, [string]$Target) {
    if ($Project.StartsWith('src/') -and $Target -match '^(tools|tests)/') {
        Add-Failure "Product project depends on non-product code: $Project -> $Target"
    }
    if ($Project.StartsWith('tools/') -and $Target.StartsWith('tests/')) {
        Add-Failure "Tool project depends on test code: $Project -> $Target"
    }
}

function Assert-TestInfrastructure {
    $support = 'tests/ReplayFoundry.Testing/ReplayFoundry.Testing.csproj'
    foreach ($relative in $manifest.DotNetTestProjects) {
        $path = Join-Path $root $relative
        if (-not (Test-Path -LiteralPath $path)) { continue }
        [xml]$project = Get-Content -Raw -LiteralPath $path
        $references = @($project.SelectNodes('//ProjectReference') |
            ForEach-Object { Get-RepositoryPath (Join-Path (Split-Path $path) $_.Include) })
        if ($support -notin $references) {
            Add-Failure "Executable test project bypasses shared test support: $relative"
        }
    }

    $duplicates = @(Get-ChildItem (Join-Path $root 'tests') -Recurse -Filter '*.cs' -File |
        Where-Object { $_.FullName -notmatch '[\\/]ReplayFoundry\.Testing[\\/]' } |
        Select-String -Pattern '(record|class)\s+TestCase\b|class\s+TestAssert\b')
    if ($duplicates.Count) {
        Add-Failure "Duplicated test infrastructure returned: $($duplicates.Path -join ', ')"
    }
}

function Assert-DocumentationPlacement {
    $rootDocuments = @(
        'README.md', 'CHANGELOG.md', 'SECURITY.md'
    )
    $misplaced = @(Get-ChildItem $root -Recurse -Filter '*.md' -File |
        Where-Object {
            $_.FullName -notmatch '[\\/](\.git|\.vs|bin|obj|outputs|tmp)[\\/]'
        } |
        ForEach-Object { Get-RepositoryPath $_.FullName } |
        Where-Object {
            $_ -notin $rootDocuments -and $_ -ne 'docs/README.md' -and
            -not $_.StartsWith('docs/distribution/')
        })
    if ($misplaced.Count) {
        Add-Failure "Markdown is outside the documentation boundary: $($misplaced -join ', ')"
    }
}

function Assert-NoGenericSourceFolders {
    $genericNames = @('Common', 'Helpers', 'Utils', 'Managers', 'Misc', 'Everything')
    $generic = @(Get-ChildItem (Join-Path $root 'src'), (Join-Path $root 'tools') `
        -Recurse -Directory | Where-Object Name -In $genericNames)
    if ($generic.Count) {
        Add-Failure "Generic source folder found: $($generic.FullName -join ', ')"
    }
}

function Assert-SourceResponsibilityBudgets {
    $defaultLineBudget = 1000
    # Publish still owns a broad WPF binding surface. Keep its current ceiling
    # explicit and shrinking instead of normalizing that size for new files.
    $lineBudgetOverrides = @{
        'src/ReplayFoundry.Desktop/Features/Publish/PublishViewModel.cs' = 2080
    }
    $sourceFiles = @(Get-ChildItem (Join-Path $root 'src'), `
        (Join-Path $root 'tools') -Recurse -File |
        Where-Object {
            $_.Extension -in @('.cs', '.py') -and
            $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
        })
    foreach ($file in $sourceFiles) {
        $relative = Get-RepositoryPath $file.FullName
        $budget = if ($lineBudgetOverrides.ContainsKey($relative)) {
            $lineBudgetOverrides[$relative]
        } else {
            $defaultLineBudget
        }
        $lineCount = @(Get-Content -LiteralPath $file.FullName).Count
        if ($lineCount -gt $budget) {
            Add-Failure "Source responsibility budget exceeded: $relative has $lineCount lines (limit $budget)."
        }
    }

    $partialViewModels = @($sourceFiles |
        Where-Object Extension -eq '.cs' |
        Select-String -Pattern '\bpartial\s+class\s+\w*ViewModel\b')
    if ($partialViewModels.Count) {
        Add-Failure ("View-model responsibility was hidden behind partial files: " +
            ($partialViewModels.Path -join ', '))
    }
}

function Assert-RepositoryToolingBoundaries {
    $console = Get-Content -Raw -LiteralPath (Join-Path $root 'eng\ReplayFoundry.ps1')
    if ($console -match '\$python\?\.Source' -or
        $console -notmatch '\$pythonCandidates\s*=\s*@\(Get-Command\s+python' -or
        $console -notmatch 'foreach\s*\(\$python\s+in\s+\$pythonCandidates\)' -or
        $console -notmatch 'Microsoft.*WindowsApps.*python' -or
        $console -notmatch 'WindowsApps[\s\S]{0,300}continue[\s\S]{0,150}return\s+\$source') {
        Add-Failure 'Repository Python resolution must inspect every discovered application, skip WindowsApps aliases, and return the first usable executable under strict mode.'
    }

    $exportRoots = @($manifest.PublicExport.Roots |
        ForEach-Object { (ConvertTo-RepositoryPath $_).TrimEnd('/') })
    $exactFiles = @($manifest.PublicExport.ExactFiles |
        ForEach-Object { ConvertTo-RepositoryPath $_ })
    $required = @($manifest.PublicExport.RequiredFiles |
        ForEach-Object { ConvertTo-RepositoryPath $_ })
    $replacementTemplates = @($manifest.PublicExport.PublicSourceReplacements |
        ForEach-Object { ConvertTo-RepositoryPath ([string]$_.Template) })
    $replacementDestinations = @($manifest.PublicExport.PublicSourceReplacements |
        ForEach-Object { ConvertTo-RepositoryPath ([string]$_.Destination) })
    Assert-SameSet 'Public-source replacement destinations' @(
        '.github/workflows/desktop-ci.yml'
        'Directory.Build.props'
        'eng/ReplayFoundry.ProductionVisualHost.psd1'
        'eng/ReplayFoundry.Repository.psd1'
        'eng/ReplayFoundry.ps1'
        'eng/Test-ReleaseDataBoundary.ps1'
        'eng/Test-RepositoryArchitecture.ps1'
        'eng/Test-RepositoryPayloadGuard.ps1'
        'src/ReplayFoundry.Desktop/AssemblyInfo.cs'
        'src/ReplayFoundry.Desktop/Platform/Intelligence/TasteLearningService.cs'
        'src/ReplayFoundry.VisualSemanticHost/replayfoundry_visual_semantic/editorial/writer/runtime.py'
    ) $replacementDestinations
    Assert-SameSet 'Private neural implementation roots' @(
        'src/ReplayFoundry.Desktop/Media/Intelligence/Learning'
        'src/ReplayFoundry.VisualSemanticHost/replayfoundry_visual_semantic/editorial/writer'
    ) @($manifest.PublicExport.PrivateRoots)
    Assert-SameSet 'Public neural integration contracts' @(
        'src/ReplayFoundry.Desktop/Media/Intelligence/Learning/TasteContracts.cs'
        'src/ReplayFoundry.Desktop/Media/Intelligence/Learning/TasteMomentCorrection.cs'
        'src/ReplayFoundry.VisualSemanticHost/replayfoundry_visual_semantic/editorial/writer/__init__.py'
        'src/ReplayFoundry.VisualSemanticHost/replayfoundry_visual_semantic/editorial/writer/capture.py'
        'src/ReplayFoundry.VisualSemanticHost/replayfoundry_visual_semantic/editorial/writer/data.py'
        'src/ReplayFoundry.VisualSemanticHost/replayfoundry_visual_semantic/editorial/writer/runtime.py'
    ) @($manifest.PublicExport.PublicContracts)
    if (@($replacementTemplates | Where-Object {
        -not $_.StartsWith(
            'eng/PublicSourceTemplates/',
            [StringComparison]::OrdinalIgnoreCase)
    }).Count -ne 0 -or
        @($replacementTemplates | Where-Object { Test-PublicExportPath $_ }).Count -ne 0) {
        Add-Failure 'Public-source replacement templates must stay in their private exact-template boundary.'
    }
    foreach ($recursiveBoundary in @('.github', 'eng')) {
        if ($recursiveBoundary -in $exportRoots) {
            Add-Failure "The public export recursively exposes $recursiveBoundary instead of its exact allowlist."
        }
    }
    Assert-SameSet 'Public GitHub allowlist' @(
        '.github/assets/buy-me-a-coffee-qr.png'
        '.github/assets/replayfoundry-demo-poster.jpg'
        '.github/assets/replayfoundry-workflow-hero.gif'
        '.github/assets/setup-base-advanced.png'
        '.github/assets/workflow-01-generate.gif'
        '.github/assets/workflow-02-studio.gif'
        '.github/assets/workflow-03-library.gif'
        '.github/assets/workflow-04-publish.gif'
        '.github/workflows/desktop-ci.yml'
        '.github/workflows/redistributable-ffmpeg.yml'
    ) @($exactFiles | Where-Object { $_.StartsWith('.github/') })
    Assert-SameSet 'Public documentation exact allowlist' @(
        'docs/README.md'
    ) @($exactFiles | Where-Object {
        $_.StartsWith('docs/')
    })
    Assert-SameSet 'Public engineering allowlist' @(
        'eng/Assert-ReplayFoundryRuntimePackCatalogBinding.ps1'
        'eng/Build-ReplayFoundryInstaller.ps1'
        'eng/Build-ReplayFoundryRuntimePacks.ps1'
        'eng/Copy-VerifiedAudioEvidenceModel.ps1'
        'eng/Prepare-AudioEvidenceModel.py'
        'eng/Copy-ReplayFoundryProductionVisualHost.ps1'
        'eng/Invoke-ReplayFoundryArtifactSigning.ps1'
        'eng/New-PythonRuntimeNotices.ps1'
        'eng/New-AccelerateSecurityWheel.py'
        'eng/New-ReplayFoundryBrandAssets.ps1'
        'eng/New-ReplayFoundryInstallerBranding.ps1'
        'eng/New-ReplayFoundryRuntimePackCatalog.ps1'
        'eng/Publish-ReplayFoundryWindows.ps1'
        'eng/Resolve-ReplayFoundryWinSparkle.ps1'
        'eng/ReplayFoundry.Updates.psd1'
        'eng/Initialize-ReplayFoundryUpdateSigning.ps1'
        'eng/New-ReplayFoundryUpdateAppcast.ps1'
        'eng/ReplayFoundry.ps1'
        'eng/ReplayFoundry.ProductionVisualHost.psd1'
        'eng/ReplayFoundry.Repository.psd1'
        'eng/Resolve-ReplayFoundryArtifactSigningClient.ps1'
        'eng/Test-CreativeCommerceArchitecture.ps1'
        'eng/Test-AiRuntimeDependencies.ps1'
        'eng/Test-GenerateWorkflowArchitecture.ps1'
        'eng/Test-InstallerBranding.ps1'
        'eng/Test-MomentFinderArchitecture.ps1'
        'eng/Test-ReleaseDataBoundary.ps1'
        'eng/Test-ReleaseEngineering.ps1'
        'eng/Test-RepositoryArchitecture.ps1'
        'eng/Test-RepositoryPayloadGuard.ps1'
        'eng/Test-SecurityBoundaries.ps1'
        'eng/Test-UiUxArchitecture.ps1'
        'eng/Test-UiUxHumanCenteredExperience.ps1'
        'eng/Test-UiUxVisualSystem.ps1'
    ) @($exactFiles | Where-Object { $_.StartsWith('eng/') })
    if ($isPublicSnapshot) {
        $actualWorkflowFiles = @(Get-ChildItem `
            -LiteralPath (Join-Path $root '.github\workflows') `
            -Recurse -File | ForEach-Object { Get-RepositoryPath $_.FullName })
        Assert-SameSet 'Public workflow files' @($exactFiles | Where-Object {
            $_.StartsWith('.github/workflows/')
        }) $actualWorkflowFiles

        $actualEngineeringFiles = @(Get-ChildItem `
            -LiteralPath (Join-Path $root 'eng') `
            -Recurse -File | ForEach-Object { Get-RepositoryPath $_.FullName })
        Assert-SameSet 'Public engineering files' @($exactFiles | Where-Object {
            $_.StartsWith('eng/')
        }) $actualEngineeringFiles

        $actualPublicationDocs = @(Get-ChildItem `
            -LiteralPath (Join-Path $root 'docs') `
            -Recurse -File | ForEach-Object { Get-RepositoryPath $_.FullName })
        Assert-SameSet 'Public publication documentation files' @(
            'docs/README.md'
            'docs/distribution/windows.md'
            'docs/distribution/third-party-compliance.md'
        ) $actualPublicationDocs
    }
    $developmentDocsExposed = @($exportRoots | Where-Object {
        $_ -eq 'docs' -or $_ -eq 'docs/development' -or
        'docs/development'.StartsWith($_ + '/', [StringComparison]::OrdinalIgnoreCase)
    })
    if ($developmentDocsExposed.Count) {
        Add-Failure 'The public export profile exposes the private developer-console guide.'
    }
    foreach ($file in @(
        'CHANGELOG.md'
        '.github/workflows/desktop-ci.yml'
        '.github/workflows/redistributable-ffmpeg.yml'
        'docs/README.md'
        'eng/Assert-ReplayFoundryRuntimePackCatalogBinding.ps1'
        'eng/Copy-ReplayFoundryProductionVisualHost.ps1'
    )) {
        if ($file -notin $required) {
            Add-Failure "The public export profile does not require production release script: $file"
        }
    }

    if (-not $isPublicSnapshot) {
        $exporter = Get-Content -Raw -LiteralPath `
            (Join-Path $root 'eng\Export-ReplayFoundryProductionRepository.ps1')
        if ($exporter -notmatch '(?s)\$exportDefinitionFiles\s*=\s*@\(.*\$repository\.ProductionVisualHostManifest.*\)') {
            Add-Failure 'The production-host selection manifest is not protected by the staged export-definition freshness gate.'
        }
        if ($exporter -notmatch '\$publicExport\.ExactFiles' -or
            $exporter -notmatch '\$publicExactFiles') {
            Add-Failure 'The production exporter does not materialize the exact public-file allowlist.'
        }
        if ($exporter -notmatch '\$publicExport\.PublicSourceReplacements' -or
            $exporter -notmatch 'Install-PublicSourceReplacements' -or
            $exporter -notmatch 'checkout-index\s+"--prefix=\$checkoutPrefix"\s+--\s+\$template') {
            Add-Failure 'The production exporter does not install its reviewed public-source replacements from the Git index.'
        }
        if ($exporter -notmatch '(?s)git\s+-C\s+\$sourceRoot\s+diff\s+--quiet\s+--\s+@batch' -or
            $exporter -notmatch 'ls-files\s+--others\s+--exclude-standard\s+-z') {
            Add-Failure 'The production exporter does not reject unstaged or untracked selected payload changes.'
        }
    }

    if (-not $isPublicSnapshot) {
        $documentationIndex = Get-Content -Raw -LiteralPath `
            (Join-Path $root 'docs\README.md')
        if ($documentationIndex -match 'development/developer-console\.md') {
            Add-Failure 'The public documentation index links to its private developer-console guide.'
        }
    }
}

function Test-PublicExportPath([string]$RelativePath) {
    $path = ConvertTo-RepositoryPath $RelativePath
    $excluded = @($manifest.PublicExport.ExcludedFiles |
        ForEach-Object { ConvertTo-RepositoryPath $_ })
    if ($path -in $excluded) { return $false }
    $exact = @(
        $manifest.PublicExport.RootFiles
        $manifest.PublicExport.ExactFiles
    ) | ForEach-Object { ConvertTo-RepositoryPath $_ }
    if ($path -in $exact) { return $true }
    foreach ($publicRoot in $manifest.PublicExport.Roots) {
        $normalizedRoot = (ConvertTo-RepositoryPath $publicRoot).TrimEnd('/')
        if ($path.Equals($normalizedRoot, [StringComparison]::OrdinalIgnoreCase) -or
            $path.StartsWith($normalizedRoot + '/', [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    return $false
}

function Assert-PublicDocumentationLinksAndWorkflowPins {
    $markdownFiles = @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.md' |
        Where-Object { Test-PublicExportPath (Get-RepositoryPath $_.FullName) })
    $linkPatterns = @(
        '!?(?:\[[^\]]*\])\((?<target>[^)\s]+)(?:\s+["''][^)]+)?\)'
        '(?i)(?:src|href)\s*=\s*["''](?<target>[^"'']+)["'']'
    )
    foreach ($document in $markdownFiles) {
        $text = Get-Content -Raw -LiteralPath $document.FullName
        foreach ($linkPattern in $linkPatterns) {
            foreach ($match in [regex]::Matches($text, $linkPattern)) {
                $target = $match.Groups['target'].Value.Trim('<', '>')
                if ([string]::IsNullOrWhiteSpace($target) -or
                    $target.StartsWith('#') -or
                    $target -match '^(?i)(https?|mailto|data|tel):') {
                    continue
                }
                $relativeTarget = $target.Split('#', 2)[0]
                $relativeTarget = [Uri]::UnescapeDataString($relativeTarget)
                $resolved = [IO.Path]::GetFullPath((Join-Path $document.DirectoryName $relativeTarget))
                $rootPrefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar) +
                    [IO.Path]::DirectorySeparatorChar
                $resolvedRelative = if ($resolved.StartsWith(
                        $rootPrefix,
                        [StringComparison]::OrdinalIgnoreCase)) {
                    Get-RepositoryPath $resolved
                } else {
                    $null
                }
                if (-not $resolved.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase) -or
                    -not (Test-Path -LiteralPath $resolved) -or
                    -not (Test-PublicExportPath $resolvedRelative)) {
                    Add-Failure "Public Markdown link is missing or escapes the export: $(Get-RepositoryPath $document.FullName) -> $target"
                }
            }
        }
    }

    $workflowFiles = @($manifest.PublicExport.ExactFiles |
        ForEach-Object { ConvertTo-RepositoryPath $_ } |
        Where-Object { $_ -match '^\.github/workflows/[^/]+\.ya?ml$' })
    foreach ($workflow in $workflowFiles) {
        $workflowPath = Join-Path $root $workflow
        if (-not (Test-Path -LiteralPath $workflowPath -PathType Leaf)) { continue }
        $text = Get-Content -Raw -LiteralPath $workflowPath
        foreach ($match in [regex]::Matches(
            $text,
            '(?m)^\s*uses:\s*(?<reference>[^\s#]+)')) {
            $reference = $match.Groups['reference'].Value.Trim('"', "'")
            if ($reference.StartsWith('./') -or $reference.StartsWith('docker://')) {
                continue
            }
            if ($reference -notmatch '^[^@\s]+@[0-9A-Fa-f]{40}$') {
                Add-Failure "Public workflow action is not pinned to a full commit SHA: $workflow -> $reference"
            }
        }
    }
}

function Assert-ProductionVisualHostBoundary {
    $manifestRelative = ConvertTo-RepositoryPath `
        $manifest.ProductionVisualHostManifest
    $manifestPath = Join-Path $root $manifestRelative
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        Add-Failure "Production visual-host manifest is missing: $manifestRelative"
        return
    }

    $visualHostManifest = Import-PowerShellDataFile -LiteralPath $manifestPath
    $sourceRoot = (ConvertTo-RepositoryPath $visualHostManifest.SourceRoot).TrimEnd('/')
    $testRoot = (ConvertTo-RepositoryPath $visualHostManifest.TestRoot).TrimEnd('/')
    $exportRoots = @($manifest.PublicExport.Roots |
        ForEach-Object { (ConvertTo-RepositoryPath $_).TrimEnd('/') })
    foreach ($exactRoot in @($sourceRoot, $testRoot)) {
        if (@($exportRoots | Where-Object {
            $exactRoot -eq $_ -or
            $exactRoot.StartsWith($_ + '/', [StringComparison]::OrdinalIgnoreCase)
        }).Count) {
            Add-Failure "Exact visual-host root is also exported recursively: $exactRoot"
        }
    }

    $expectedSource = @(
        "$sourceRoot/$($visualHostManifest.EntryPoint)"
        @($visualHostManifest.Assets | ForEach-Object { "$sourceRoot/$_" })
        @($visualHostManifest.Modules | ForEach-Object {
            "$sourceRoot/replayfoundry_visual_semantic/$_"
        })
    ) | ForEach-Object { ConvertTo-RepositoryPath $_ } | Sort-Object -Unique
    $expectedTests = @($visualHostManifest.PublicTests | ForEach-Object {
        ConvertTo-RepositoryPath "$testRoot/$_"
    }) | Sort-Object -Unique

    foreach ($relative in @($expectedSource + $expectedTests)) {
        if (-not (Test-Path -LiteralPath (Join-Path $root $relative) -PathType Leaf)) {
            Add-Failure "Production visual-host allowlist file is missing: $relative"
        }
    }

    if (-not $isPublicSnapshot) { return }
    $actualSource = @(Get-ChildItem -LiteralPath (Join-Path $root $sourceRoot) `
        -Recurse -File | ForEach-Object { Get-RepositoryPath $_.FullName })
    $actualTests = @(Get-ChildItem -LiteralPath (Join-Path $root $testRoot) `
        -Recurse -File | ForEach-Object { Get-RepositoryPath $_.FullName })
    Assert-SameSet 'Public visual-host source allowlist' $expectedSource $actualSource
    Assert-SameSet 'Public visual-host test allowlist' $expectedTests $actualTests
}

Assert-Layout
Assert-ProjectInventory
Assert-ProjectReferences
Assert-TestInfrastructure
Assert-DocumentationPlacement
Assert-NoGenericSourceFolders
Assert-SourceResponsibilityBudgets
Assert-RepositoryToolingBoundaries
Assert-PublicDocumentationLinksAndWorkflowPins
Assert-ProductionVisualHostBoundary

if ($failures.Count) {
    Write-Error ("Repository architecture guard failed:`n- " + ($failures -join "`n- "))
    exit 1
}

Write-Host 'Repository architecture guard passed: layout, manifest, solution, dependency direction, shared testing, and documentation boundaries are coherent.'
