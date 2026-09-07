[CmdletBinding()]
param([string]$RepositoryRoot)

$ErrorActionPreference = 'Stop'
$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Join-Path $scriptDirectory '..'
}
$root = [IO.Path]::GetFullPath($RepositoryRoot)

function Assert-NoMatch([string]$Path, [string]$Pattern, [string]$Message) {
    $matches = @(Get-ChildItem -LiteralPath $Path -Recurse -File -Filter '*.cs' |
        Select-String -Pattern $Pattern)
    if ($matches.Count -ne 0) {
        throw "$Message`n$($matches | ForEach-Object { "$($_.Path):$($_.LineNumber)" } | Out-String)"
    }
}

function Get-ConditionalSource(
    [string]$Text,
    [bool]$DebugEnabled) {
    $result = [Collections.Generic.List[string]]::new()
    $frames = [Collections.Generic.List[object]]::new()
    $include = $true

    foreach ($line in ($Text -split "`r?`n")) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '#if DEBUG') {
            $frames.Add([pscustomobject]@{
                ParentInclude = $include
                Condition = $DebugEnabled
                HasElse = $false
            })
            $include = $include -and $DebugEnabled
            continue
        }
        if ($trimmed -eq '#else') {
            if ($frames.Count -eq 0) {
                throw 'Conditional source contains an unmatched #else directive.'
            }
            $frame = $frames[$frames.Count - 1]
            if ($frame.HasElse) {
                throw 'Conditional source contains a duplicate #else directive.'
            }
            $frame.HasElse = $true
            $include = $frame.ParentInclude -and -not $frame.Condition
            continue
        }
        if ($trimmed -eq '#endif') {
            if ($frames.Count -eq 0) {
                throw 'Conditional source contains an unmatched #endif directive.'
            }
            $frame = $frames[$frames.Count - 1]
            $frames.RemoveAt($frames.Count - 1)
            $include = $frame.ParentInclude
            continue
        }
        if ($trimmed.StartsWith('#if ', [StringComparison]::Ordinal)) {
            throw "Conditional-source guard does not recognize directive '$trimmed'."
        }
        if ($include) {
            $result.Add($line)
        }
    }

    if ($frames.Count -ne 0) {
        throw 'Conditional source contains an unmatched #if DEBUG directive.'
    }
    return [string]::Join("`n", $result)
}

$runtimeProject = Join-Path $root 'src\ReplayFoundry.RuntimePacks\ReplayFoundry.RuntimePacks.csproj'
$runtimeProjectText = Get-Content -Raw -LiteralPath $runtimeProject
if ($runtimeProjectText -match 'ReplayFoundry\.Desktop|UseWPF|PresentationFramework') {
    throw 'The runtime-pack domain must remain WPF/Desktop independent.'
}

$featureRoot = Join-Path $root 'src\ReplayFoundry.Desktop\Features'
Assert-NoMatch $featureRoot 'ReplayFoundryRuntimePackStore|ReplayFoundryRuntimePackCatalogInstaller|HttpClient|ProcessStartInfo' `
    'Feature/ViewModel code must not install, download, hash, or launch runtime payloads.'

$views = Join-Path $root 'src\ReplayFoundry.Desktop\Features'
Assert-NoMatch $views 'ReplayFoundry\.RuntimePacks' `
    'Views and feature ViewModels must consume capability projections, not runtime-pack domain types.'

$launcher = Get-Content -Raw -LiteralPath (Join-Path $root 'src\ReplayFoundry.Desktop\Platform\RuntimePacks\RuntimePackMaintenanceLauncher.cs')
$maintenanceShutdown = Get-Content -Raw -LiteralPath (Join-Path $root 'src\ReplayFoundry.Desktop\Platform\RuntimePacks\RuntimeMaintenanceShutdownCoordinator.cs')
if ($launcher -notmatch 'UriSchemeHttps' -or $launcher -match 'HttpClient|Download') {
    throw 'Settings maintenance must only launch the signed HTTPS installer or local maintenance tool.'
}
if ($launcher -notmatch '"--wait-for-parent"' -or
    $launcher -notmatch 'Environment\.ProcessId' -or
    $launcher -notmatch 'RuntimeMaintenanceShutdownCoordinator\.Request' -or
    $maintenanceShutdown -notmatch 'Application\.Current\?\.MainWindow' -or
    $maintenanceShutdown -notmatch 'window\.Close\(\)' -or
    $maintenanceShutdown -notmatch 'Application\.Current\?\.Shutdown\(\)') {
    throw 'Advanced AI removal must hand off to maintenance and request the cancelable main-window close path before deletion.'
}

$runtimeStore = Get-Content -Raw -LiteralPath `
    (Join-Path $root 'src\ReplayFoundry.RuntimePacks\RuntimePackStore.cs')
$mutationLease = Get-Content -Raw -LiteralPath `
    (Join-Path $root 'src\ReplayFoundry.RuntimePacks\RuntimePackStoreMutationLease.cs')
foreach ($method in @(
    'InstallAsync',
    'RepairAsync',
    'RemoveAsync',
    'PruneInactiveAsync',
    'ActivateInstalledAsync',
    'CleanupAbandonedStagingAsync',
    'CleanupEmptyStoreAsync')) {
    $pattern = '(?s)public\s+async\s+Task(?:<[^>]+>)?\s+' +
        [Regex]::Escape($method) +
        '\b.{0,500}?=>\s*await\s+ExecuteMutationAsync\('
    if ($runtimeStore -notmatch $pattern) {
        throw "Public runtime-pack mutation bypasses the shared mutation boundary: $method"
    }
}
if ($runtimeStore -notmatch 'RuntimePackStoreMutationLease\.AcquireAsync' -or
    $mutationLease -notmatch 'FileShare\.None' -or
    $mutationLease -notmatch 'FileOptions\.DeleteOnClose' -or
    $mutationLease -notmatch 'AcquisitionTimeout') {
    throw 'Every runtime-pack store mutation must hold the bounded cross-process store lease.'
}

$ffmpegLocator = Get-Content -Raw -LiteralPath (Join-Path $root 'src\ReplayFoundry.Desktop\Platform\Media\FfmpegToolLocator.cs')
$releaseFfmpegLocator = Get-ConditionalSource $ffmpegLocator $false
$debugFfmpegLocator = Get-ConditionalSource $ffmpegLocator $true
$releaseDevelopmentFallback =
    'ExplicitRuntimeEnvironment\.Read|AppContext\.BaseDirectory|' +
    'GetEnvironmentVariable\("PATH"\)|Tools\\FFmpeg|' +
    'FfprobeOverrideVariable|FfmpegOverrideVariable|' +
    'GetOverrideEnvironmentVariable'
if ($releaseFfmpegLocator -match $releaseDevelopmentFallback -or
    $releaseFfmpegLocator -notmatch '_runtimeEnvironment\.FfprobePath' -or
    $releaseFfmpegLocator -notmatch '_runtimeEnvironment\.FfmpegPath' -or
    $releaseFfmpegLocator -notmatch 'Repair installed tools' -or
    $releaseFfmpegLocator -notmatch '(?s)public FfmpegToolLocator\(\).*ReplayFoundryRuntimeEnvironment\.Current') {
    throw 'Release media-tool resolution must use only the verified active media-tools pack and direct failures to pack repair.'
}
$debugOverride = $debugFfmpegLocator.IndexOf(
    'ExplicitRuntimeEnvironment.Read',
    [StringComparison]::Ordinal)
$debugVerifiedPack = $debugFfmpegLocator.IndexOf(
    '_runtimeEnvironment.FfprobePath',
    [StringComparison]::Ordinal)
$debugApplicationFallback = $debugFfmpegLocator.IndexOf(
    'AppContext.BaseDirectory',
    [StringComparison]::Ordinal)
$debugPathFallback = $debugFfmpegLocator.IndexOf(
    'GetEnvironmentVariable("PATH")',
    [StringComparison]::Ordinal)
if ($debugOverride -lt 0 -or
    $debugVerifiedPack -lt 0 -or
    $debugApplicationFallback -lt 0 -or
    $debugPathFallback -lt 0 -or
    $debugOverride -gt $debugVerifiedPack -or
    $debugVerifiedPack -gt $debugApplicationFallback -or
    $debugApplicationFallback -gt $debugPathFallback) {
    throw 'Debug media-tool resolution must retain override, verified-pack, application, and PATH candidates in that order.'
}

$qwenResolverPath = Join-Path $root 'src\ReplayFoundry.Desktop\Platform\RuntimePacks\QwenRuntimeResolver.cs'
$qwenResolver = Get-Content -Raw -LiteralPath $qwenResolverPath
$releaseQwenResolver = Get-ConditionalSource $qwenResolver $false
$debugQwenResolver = Get-ConditionalSource $qwenResolver $true
$releaseQwenDevelopmentCandidate =
    'REPLAYFOUNDRY_QWEN_|ExplicitRuntimeEnvironment|' +
    'ResolveDevelopmentCandidates|AppContext\.BaseDirectory|' +
    'GetEnvironmentVariable\("PATH"\)|ReplayFoundry-DeveloperArtifacts|' +
    'eng[\\/]visual-semantic-host|src[\\/]ReplayFoundry\.VisualSemanticHost'
if ($releaseQwenResolver -match $releaseQwenDevelopmentCandidate -or
    $releaseQwenResolver -notmatch 'FromVerifiedActivePack' -or
    $releaseQwenResolver -notmatch 'verifiedActivePack\.PythonExecutablePath' -or
    $releaseQwenResolver -notmatch 'verifiedActivePack\.HostScriptPath' -or
    $releaseQwenResolver -notmatch 'verifiedActivePack\.ModelManifestPath' -or
    $releaseQwenResolver -notmatch 'verifiedActivePack\.PromptManifestPath' -or
    $releaseQwenResolver -notmatch 'verifiedActivePack\.QualificationLockPath' -or
    $releaseQwenResolver -notmatch 'verifiedActivePack\.EnvironmentVariables') {
    throw 'Release Qwen resolution must use only the verified active runtime/model-pack projection.'
}
$debugQwenVariables = @(
    'REPLAYFOUNDRY_QWEN_PYTHON',
    'REPLAYFOUNDRY_QWEN_HOST_SCRIPT',
    'REPLAYFOUNDRY_QWEN_FFMPEG_SHARED',
    'REPLAYFOUNDRY_QWEN_MODEL_MANIFEST',
    'REPLAYFOUNDRY_QWEN_PROMPT_MANIFEST',
    'REPLAYFOUNDRY_QWEN_QUALIFICATION_LOCK'
)
if ($debugQwenResolver -notmatch 'ExplicitRuntimeEnvironment\.Read' -or
    $debugQwenResolver -notmatch 'DevelopmentOverrideOptInVariable' -or
    $debugQwenResolver -notmatch '"1"' -or
    $debugQwenResolver -notmatch 'ResolveDevelopmentCandidates' -or
    $debugQwenResolver -notmatch 'explicitPython \?\?' -or
    $debugQwenResolver -notmatch 'verifiedActivePack\?\.PythonExecutablePath' -or
    @($debugQwenVariables | Where-Object { $debugQwenResolver -notmatch [regex]::Escape($_) }).Count -ne 0) {
    throw 'Debug Qwen resolution must retain all explicit field overrides before verified-pack fallback.'
}

$compositionRoot = Get-Content -Raw -LiteralPath (Join-Path $root 'src\ReplayFoundry.Desktop\ApplicationCompositionRoot.cs')
$localIntelligenceComposition = Get-Content -Raw -LiteralPath (Join-Path $root `
    'src\ReplayFoundry.Desktop\Composition\LocalIntelligenceComposition.cs')
if ($compositionRoot -notmatch `
        'LocalIntelligenceComposition\.CreateVisualReviewServices\(' -or
    $localIntelligenceComposition -notmatch 'QwenRuntimeResolver\.Resolve\(' -or
    ($compositionRoot + $localIntelligenceComposition) -match `
        'REPLAYFOUNDRY_QWEN_') {
    throw 'The composition root must delegate Qwen selection to the build-gated runtime resolver.'
}

$runtimeEnvironment = Get-Content -Raw -LiteralPath (Join-Path $root 'src\ReplayFoundry.Desktop\Platform\RuntimePacks\ReplayFoundryRuntimeEnvironment.cs')
if ($runtimeEnvironment -notmatch 'MinimumQwenRuntimeVersion\s*=\s*new\(0, 8, 25\)' -or
    $runtimeEnvironment -notmatch 'MinimumQwenModelVersion\s*=\s*new\(4, 0, 21\)' -or
    $runtimeEnvironment -notmatch 'MinimumMediaToolsVersion\s*=\s*new\(8, 1, 2, 32\)' -or
    $runtimeEnvironment -notmatch 'CreateCompatibleQwenPaths\(' -or
    $runtimeEnvironment -notmatch `
        '(?s)Installed version\s*"\s*\+\s*\$"\{installedVersion\}; required version \{minimumVersion\} or newer\.' -or
    $runtimeEnvironment -notmatch 'QwenUnavailableReason' -or
    $runtimeEnvironment -notmatch 'HasExactCurrentDependency\(' -or
    $runtimeEnvironment -notmatch 'dependency\.RequiredManifestHash is not null' -or
    $runtimeEnvironment -notmatch 'dependency\.Accepts\(' -or
    $runtimeEnvironment -notmatch 'Repair or update Advanced AI') {
    throw 'Runtime discovery must reject stale or cross-activated Qwen, runtime, and media pack sets with actionable status.'
}
if ($localIntelligenceComposition -notmatch 'runtime\.QwenUnavailableReason') {
    throw 'Generation setup must retain the precise Advanced AI runtime incompatibility reason.'
}

$installerBuilder = Get-Content -Raw -LiteralPath (Join-Path $root 'eng\Build-ReplayFoundryInstaller.ps1')
if ($installerBuilder -notmatch 'embeddedPayloadCeiling' -or
    $installerBuilder -notmatch 'AdvancedPayloadMode Online' -or
    $installerBuilder -notmatch 'OfferAdvancedAi' -or
    $installerBuilder -notmatch 'verify-catalog' -or
    $installerBuilder -notmatch 'Assert-ReplayFoundryRuntimePackCatalogBinding\.ps1') {
    throw 'The installer build must reject an oversized embedded profile and direct it to the verified online catalog path.'
}
$catalogBuilder = Get-Content -Raw -LiteralPath (Join-Path $root 'eng\New-ReplayFoundryRuntimePackCatalog.ps1')
if ($catalogBuilder -notmatch 'ConvertFrom-Json -DateKind String' -or
    $catalogBuilder -notmatch '\[DateTimeOffset\]::Parse' -or
    $catalogBuilder -notmatch 'createdAtUtc = \$createdAtUtc\.ToUniversalTime\(\)\.ToString\(''O''') {
    throw 'Runtime-pack catalog generation must preserve the signed build timestamp as canonical UTC.'
}
if ($catalogBuilder -notmatch "schemaVersion = 'replayfoundry-runtime-pack-catalog-1\.1'" -or
    $catalogBuilder -notmatch 'manifestHash = \$manifest\.manifestHash') {
    throw 'Online catalogs must bind each archive to the exact installed manifest hash.'
}
if ($installerBuilder -notmatch "ReleaseChannel -eq 'Production'.*SigningMode -ne 'ArtifactSigning'" -or
    $installerBuilder -notmatch 'Invoke-ReplayFoundryArtifactSigning\.ps1' -or
    $installerBuilder -notmatch 'replayfoundry-installer-release-manifest-1\.1') {
    throw 'Production installers must require Artifact Signing, verify the result, and emit a release manifest.'
}

$installerDefinition = Get-Content -Raw -LiteralPath (Join-Path $root 'installer\ReplayFoundry.iss')
if ($installerDefinition -match '\[LEGAL PUBLISHER NAME\]|YOUR-DOMAIN\.example' -or
    $installerDefinition -notmatch '#define MyAppPublisher "Expired Soda Studios LLC"' -or
    $installerDefinition -notmatch 'AppPublisherURL=https://replayfoundry\.com/' -or
    $installerDefinition -notmatch 'SignedUninstaller=yes' -or
    $installerDefinition -notmatch 'SignTool=\{#ReplayFoundrySignToolName\}') {
    throw 'Installer publisher metadata and signed-uninstaller integration must remain release-ready.'
}
if ($installerDefinition -notmatch 'Name: "advancedai"' -or
    $installerDefinition -notmatch "WizardIsTaskSelected\('advancedai'\)" -or
    $installerDefinition -notmatch 'about \{#AdvancedDownloadSizeGb\} GB download' -or
    $installerDefinition -notmatch 'ReplayFoundry-Setup\.exe') {
    throw 'The signed installer must expose the optional Advanced AI download and retain the same installer for later maintenance.'
}

$publisher = Get-Content -Raw -LiteralPath (Join-Path $root 'eng\Publish-ReplayFoundryWindows.ps1')
if ($publisher -notmatch 'replayfoundry-release-manifest-1\.1' -or
    $publisher -notmatch 'Get-AuthenticodeSignature' -or
    $publisher -notmatch 'sourceTreeDirty' -or
    $publisher -notmatch "ReleaseChannel -eq 'Production'.*SigningMode -ne 'ArtifactSigning'") {
    throw 'Published app binaries must be signed before their hashes are sealed into the release manifest.'
}

$installerScript = Get-Content -Raw -LiteralPath (Join-Path $root 'installer\ReplayFoundry.iss')
$lastPackInstall = $installerScript.LastIndexOf(
    'install-catalog --catalog',
    [StringComparison]::Ordinal)
$inactivePrune = $installerScript.IndexOf(
    'prune-inactive --store-root',
    [StringComparison]::Ordinal)
if ($lastPackInstall -lt 0 -or
    $inactivePrune -lt 0 -or
    $inactivePrune -lt $lastPackInstall) {
    throw 'The installer must prune inactive packs only after the complete selected pack set has installed.'
}

$signer = Get-Content -Raw -LiteralPath (Join-Path $root 'eng\Invoke-ReplayFoundryArtifactSigning.ps1')
if ($signer -notmatch '\.codesigning\.azure\.net' -or
    $signer -notmatch 'http://timestamp\.acs\.microsoft\.com' -or
    $signer -notmatch 'Get-AuthenticodeSignature' -or
    $signer -notmatch 'TimeStamperCertificate') {
    throw 'Artifact Signing must use the official endpoint family, Microsoft timestamp authority, and post-signing verification.'
}

$packBuilder = Get-Content -Raw -LiteralPath (Join-Path $root 'eng\Build-ReplayFoundryRuntimePacks.ps1')
$hostAssembler = Get-Content -Raw -LiteralPath `
    (Join-Path $root 'eng\Copy-ReplayFoundryProductionVisualHost.ps1')
if ($hostAssembler -notmatch 'ReplayFoundry\.ProductionVisualHost\.psd1' -or
    $hostAssembler -notmatch '\$hostManifest\.Modules' -or
    $hostAssembler -match '(?s)\$runtimeModules\s*=\s*@\(\s*''') {
    throw 'Production packaging and public export must share the exact visual-host file manifest.'
}
$qwenHostCheck = $packBuilder.IndexOf(
    'Test-QwenRuntimeHost $visualRuntimePack $mediaPack',
    [StringComparison]::Ordinal)
$qwenSeal = $packBuilder.IndexOf(
    "Seal-Pack 'replayfoundry-qwen3-vl-runtime'",
    [StringComparison]::Ordinal)
if ($qwenHostCheck -lt 0 -or
    $qwenSeal -lt 0 -or
    $qwenHostCheck -gt $qwenSeal -or
    $packBuilder -notmatch "python\\python\.exe'\) -B") {
    throw 'The Qwen runtime launch check must run without writing bytecode and before the pack is sealed and hashed.'
}
if ($packBuilder -notmatch 'Copy-ReplayFoundryProductionVisualHost\.ps1' -or
    $packBuilder -match 'test_qwen3_vl_output_contract\.py|test_qwen3_vl_sampling_audit\.py') {
    throw 'The packaged Qwen host must expose the production command surface and exclude host test entry points.'
}
$pythonCopy = $packBuilder.IndexOf(
    "Copy-Tree `$pythonRoot (Join-Path `$visualRuntimePack 'python')",
    [StringComparison]::Ordinal)
$pythonResidueCleanup = $packBuilder.LastIndexOf(
    'Remove-PythonInstallationResidue $visualRuntimePack',
    [StringComparison]::Ordinal)
$pythonResidueAssertion = $packBuilder.LastIndexOf(
    'Assert-NoPythonInstallationResidue $visualRuntimePack',
    [StringComparison]::Ordinal)
$portableModelManifest = $packBuilder.LastIndexOf(
    'Copy-PackagedModelManifest',
    [StringComparison]::Ordinal)
$visualModelSeal = $packBuilder.IndexOf(
    "Seal-Pack 'replayfoundry-qwen3-vl-4b-instruct'",
    [StringComparison]::Ordinal)
if ($pythonCopy -lt 0 -or
    $pythonResidueCleanup -le $pythonCopy -or
    $pythonResidueAssertion -le $pythonResidueCleanup -or
    $pythonResidueAssertion -gt $qwenHostCheck -or
    $portableModelManifest -lt 0 -or
    $visualModelSeal -le $portableModelManifest -or
    $packBuilder -notmatch "@\('site-packages','Doc','include','Tools','tcl','test'\)" -or
    $packBuilder -notmatch "-Filter 'direct_url\.json'" -or
    $packBuilder -notmatch "modelDirectoryPath -cne 'model'") {
    throw 'Advanced packaging must remove Python installation/test residue and rewrite only the packaged model location to a portable path before sealing.'
}
if ($packBuilder -notmatch 'Assert-RedistributableFfmpeg' -or
    $packBuilder -notmatch '--disable-libopenh264' -or
    $packBuilder -notmatch 'MediaToolsSourceArchiveSha256' -or
    $packBuilder -notmatch "'MediaTools' '8\.1\.2\.32'" -or
    $packBuilder -notmatch "'VisualRuntime' '0\.8\.26'" -or
    $packBuilder -notmatch "'VisualModel' '4\.0\.22'" -or
    $packBuilder -notmatch "packageId='replayfoundry-media-tools';minimumVersion='8\.1\.2\.32';requiredManifestHash=\`$media\.manifest\.manifestHash" -or
    $packBuilder -notmatch "packageId='replayfoundry-qwen3-vl-runtime';minimumVersion='0\.8\.26';requiredManifestHash=\`$visualRuntime\.manifest\.manifestHash") {
    throw 'Advanced packaging must seal Qwen runtime 0.8.26 and model 4.0.22 across exact active media/runtime manifest edges.'
}

function Assert-ProductionHostImportClosure([string]$HostRoot) {
    $pythonFiles = @(Get-ChildItem -LiteralPath $HostRoot -Recurse -File -Filter '*.py')
    foreach ($file in $pythonFiles) {
        $relative = [IO.Path]::GetRelativePath($HostRoot, $file.FullName)
        $directoryParts = @((Split-Path -Parent $relative) -split '[\\/]' |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        foreach ($line in Get-Content -LiteralPath $file.FullName) {
            if ($line -notmatch '^\s*from\s+(\.+)([A-Za-z0-9_.]*)\s+import') {
                continue
            }
            $ascend = $Matches[1].Length - 1
            if ($ascend -gt $directoryParts.Count) {
                throw "Invalid relative import in packaged module '$relative': $line"
            }
            $targetParts = @($directoryParts)
            if ($ascend -gt 0) {
                $targetParts = @($targetParts[0..($targetParts.Count - $ascend - 1)])
            }
            if (-not [string]::IsNullOrWhiteSpace($Matches[2])) {
                $targetParts += @($Matches[2] -split '\.')
            }
            $target = Join-Path $HostRoot ([string]::Join(
                [IO.Path]::DirectorySeparatorChar,
                $targetParts))
            if (-not (Test-Path -LiteralPath ($target + '.py') -PathType Leaf) -and
                -not (Test-Path -LiteralPath (Join-Path $target '__init__.py') -PathType Leaf)) {
                throw "Packaged module '$relative' imports missing production module '$($Matches[2])'."
            }
        }
    }
}

$hostSmoke = Join-Path ([IO.Path]::GetTempPath()) `
    ('ReplayFoundry-ProductionHostGuard-' + [guid]::NewGuid().ToString('N'))
try {
    & (Join-Path $root 'eng\Copy-ReplayFoundryProductionVisualHost.ps1') `
        -SourceRoot (Join-Path $root 'src\ReplayFoundry.VisualSemanticHost') `
        -DestinationRoot $hostSmoke | Out-Null
    $packagedPaths = @(Get-ChildItem -LiteralPath $hostSmoke -Recurse -File |
        ForEach-Object { [IO.Path]::GetRelativePath($hostSmoke, $_.FullName).Replace('\', '/') })
    $requiredHostPaths = @(
        'qwen3_vl_batch_host.py'
        'replayfoundry-production-host.txt'
        'replayfoundry-editorial-metadata-prompt-1.47.txt'
        'replayfoundry-grounded-editorial-rephrase-policy-2.10.txt'
        'replayfoundry_visual_semantic/cli.py'
        'replayfoundry_visual_semantic/grounded_pass_diagnostics.py'
        'replayfoundry_visual_semantic/editorial/grounded_metadata_isolated_fields.py'
        'replayfoundry_visual_semantic/editorial/grounded_packet_handoff.py'
        'replayfoundry_visual_semantic/editorial/grounded_metadata_synthesis_messages.py'
        'replayfoundry_visual_semantic/editorial/qualification_lock.py'
        'replayfoundry_visual_semantic/runtime_video_sampling.py'
    )
    $forbiddenHostPattern =
        '(^|/)(__pycache__|test[^/]*)(/|$)|\.pyc$|' +
        '(^|/)sampling_(audit|capture|timing)\.py$|' +
        '(^|/)development_commands\.py$|' +
        '(^|/)development_cli\.py$|' +
        '(^|/)editorial/(constrained_)?(development|pilot)_command\.py$|' +
        '(^|/)editorial/(pilot_protocol|sampling_authorization)\.py$'
    if (@($requiredHostPaths | Where-Object { $_ -notin $packagedPaths }).Count -ne 0 -or
        @($packagedPaths | Where-Object { $_ -match $forbiddenHostPattern }).Count -ne 0 -or
        'replayfoundry-grounded-editorial-rephrase-policy-2.1.txt' -in $packagedPaths) {
        throw 'The production Qwen host assembly did not preserve the exact runtime-only payload.'
    }
    $packagedEntry = Get-Content -Raw -LiteralPath `
        (Join-Path $hostSmoke 'qwen3_vl_batch_host.py')
    $packagedCli = Get-Content -Raw -LiteralPath `
        (Join-Path $hostSmoke 'replayfoundry_visual_semantic\cli.py')
    $parserStart = $packagedCli.IndexOf('def _build_parser(', [StringComparison]::Ordinal)
    $mainStart = $packagedCli.IndexOf(
        'def main(',
        [StringComparison]::Ordinal)
    if ($parserStart -lt 0 -or $mainStart -le $parserStart) {
        throw 'The packaged Qwen CLI parser could not be inspected.'
    }
    $productionParser = $packagedCli.Substring(
        $parserStart,
        $mainStart - $parserStart)
    $productionCommands = @([regex]::Matches(
        $productionParser,
        'add_parser\(\s*"([^"]+)"') |
        ForEach-Object { $_.Groups[1].Value })
    $expectedProductionCommands = @(
        'verify-editorial-structured-decoding'
        'run-qualified-editorial-batch'
        'run-grounded-editorial-metadata-batch'
    )
    $packagedCommands = Get-Content -Raw -LiteralPath `
        (Join-Path $hostSmoke 'replayfoundry_visual_semantic\commands.py')
    if (@($productionCommands | Where-Object { $_ -notin $expectedProductionCommands }).Count -ne 0 -or
        @($expectedProductionCommands | Where-Object { $_ -notin $productionCommands }).Count -ne 0 -or
        $productionCommands.Count -ne $expectedProductionCommands.Count -or
        $packagedEntry -notmatch 'raise SystemExit\(main\(\)\)' -or
        $packagedCli -match 'audit-video-sampling|run-editorial-development|development_cli|production_only' -or
        $packagedCommands -match '_audit_video_sampling|sampling_audit|def\s+_(probe|run)\(') {
        throw 'The packaged Qwen host exposes a developer-only command or omits a supported production command.'
    }
    Assert-ProductionHostImportClosure $hostSmoke
} finally {
    if ((Test-Path -LiteralPath $hostSmoke) -and
        $hostSmoke.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase)) {
        [IO.Directory]::Delete($hostSmoke, $true)
    }
}

$catalogSmoke = Join-Path ([IO.Path]::GetTempPath()) `
    ('ReplayFoundry-CatalogBindingGuard-' + [guid]::NewGuid().ToString('N'))
try {
    $packageId = 'replayfoundry-smoke-pack'
    $manifestDirectory = Join-Path $catalogSmoke "packs\$packageId"
    New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null
    $manifest = [ordered]@{
        identity = [ordered]@{
            packageId = $packageId
            kind = 'VisualRuntime'
            semanticVersion = '1.2.3'
        }
        manifestHash = ('A' * 64)
    }
    $pack = [ordered]@{
        packageId = $packageId
        byteLength = 123
        sha256 = ('B' * 64)
        manifestHash = $manifest.manifestHash
    }
    $catalogPack = [ordered]@{
        packageId = $packageId
        kind = $manifest.identity.kind
        semanticVersion = $manifest.identity.semanticVersion
        byteLength = $pack.byteLength
        sha256 = $pack.sha256
        manifestHash = $pack.manifestHash
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath `
        (Join-Path $manifestDirectory 'runtime-pack-manifest.json') -Encoding utf8NoBOM
    ([ordered]@{ profile = 'Advanced'; packs = @($pack) }) |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath `
        (Join-Path $catalogSmoke 'runtime-pack-build-index.json') -Encoding utf8NoBOM
    $catalogPath = Join-Path $catalogSmoke 'runtime-pack-catalog.json'
    ([ordered]@{ profile = 'Advanced'; packs = @($catalogPack) }) |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $catalogPath -Encoding utf8NoBOM
    & (Join-Path $root 'eng\Assert-ReplayFoundryRuntimePackCatalogBinding.ps1') `
        -RuntimePackBuildRoot $catalogSmoke -CatalogPath $catalogPath | Out-Null
    $catalogPack.semanticVersion = '9.9.9'
    ([ordered]@{ profile = 'Advanced'; packs = @($catalogPack) }) |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $catalogPath -Encoding utf8NoBOM
    $rejected = $false
    try {
        & (Join-Path $root 'eng\Assert-ReplayFoundryRuntimePackCatalogBinding.ps1') `
            -RuntimePackBuildRoot $catalogSmoke -CatalogPath $catalogPath | Out-Null
    } catch {
        $rejected = $true
    }
    if (-not $rejected) {
        throw 'Catalog binding accepted a semantic-version mismatch.'
    }
    $catalogPack.semanticVersion = $manifest.identity.semanticVersion
    $extraCatalogPack = [ordered]@{
        packageId = 'replayfoundry-unindexed-pack'
        kind = $catalogPack.kind
        semanticVersion = $catalogPack.semanticVersion
        byteLength = $catalogPack.byteLength
        sha256 = $catalogPack.sha256
        manifestHash = $catalogPack.manifestHash
    }
    ([ordered]@{ profile = 'Advanced'; packs = @($pack, $pack) }) |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath `
        (Join-Path $catalogSmoke 'runtime-pack-build-index.json') -Encoding utf8NoBOM
    ([ordered]@{ profile = 'Advanced'; packs = @($catalogPack, $extraCatalogPack) }) |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $catalogPath -Encoding utf8NoBOM
    $rejected = $false
    try {
        & (Join-Path $root 'eng\Assert-ReplayFoundryRuntimePackCatalogBinding.ps1') `
            -RuntimePackBuildRoot $catalogSmoke -CatalogPath $catalogPath | Out-Null
    } catch {
        $rejected = $true
    }
    if (-not $rejected) {
        throw 'Catalog binding accepted duplicate index IDs and an unindexed catalog package.'
    }
} finally {
    if ((Test-Path -LiteralPath $catalogSmoke) -and
        $catalogSmoke.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase)) {
        [IO.Directory]::Delete($catalogSmoke, $true)
    }
}

Write-Output 'Runtime-pack architecture guard passed.'
