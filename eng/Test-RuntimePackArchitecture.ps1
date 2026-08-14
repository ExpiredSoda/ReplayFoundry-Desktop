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

$runtimeProject = Join-Path $root 'ReplayFoundry.RuntimePacks\ReplayFoundry.RuntimePacks.csproj'
$runtimeProjectText = Get-Content -Raw -LiteralPath $runtimeProject
if ($runtimeProjectText -match 'ReplayFoundry\.Desktop|UseWPF|PresentationFramework') {
    throw 'The runtime-pack domain must remain WPF/Desktop independent.'
}

$featureRoot = Join-Path $root 'ReplayFoundry.Desktop\Features'
Assert-NoMatch $featureRoot 'ReplayFoundryRuntimePackStore|ReplayFoundryRuntimePackCatalogInstaller|HttpClient|ProcessStartInfo' `
    'Feature/ViewModel code must not install, download, hash, or launch runtime payloads.'

$views = Join-Path $root 'ReplayFoundry.Desktop\Features'
Assert-NoMatch $views 'ReplayFoundry\.RuntimePacks' `
    'Views and feature ViewModels must consume capability projections, not runtime-pack domain types.'

$launcher = Get-Content -Raw -LiteralPath (Join-Path $root 'ReplayFoundry.Desktop\Platform\RuntimePacks\RuntimePackMaintenanceLauncher.cs')
if ($launcher -notmatch 'UriSchemeHttps' -or $launcher -match 'HttpClient|Download') {
    throw 'Settings maintenance must only launch the signed HTTPS installer or local maintenance tool.'
}

$ffmpegLocator = Get-Content -Raw -LiteralPath (Join-Path $root 'ReplayFoundry.Desktop\Platform\Media\FfmpegToolLocator.cs')
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
    $releaseFfmpegLocator -notmatch 'repair the Base media-tools pack' -or
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

$qwenResolverPath = Join-Path $root 'ReplayFoundry.Desktop\Platform\RuntimePacks\QwenRuntimeResolver.cs'
$qwenResolver = Get-Content -Raw -LiteralPath $qwenResolverPath
$releaseQwenResolver = Get-ConditionalSource $qwenResolver $false
$debugQwenResolver = Get-ConditionalSource $qwenResolver $true
$releaseQwenDevelopmentCandidate =
    'REPLAYFOUNDRY_QWEN_|ExplicitRuntimeEnvironment|' +
    'ResolveDevelopmentCandidates|AppContext\.BaseDirectory|' +
    'GetEnvironmentVariable\("PATH"\)|ReplayFoundry-DeveloperArtifacts|' +
    'eng[\\/]visual-semantic-host'
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

$compositionRoot = Get-Content -Raw -LiteralPath (Join-Path $root 'ReplayFoundry.Desktop\ApplicationCompositionRoot.cs')
if ($compositionRoot -notmatch 'QwenRuntimeResolver\.Resolve\(' -or
    $compositionRoot -match 'REPLAYFOUNDRY_QWEN_') {
    throw 'The composition root must delegate Qwen selection to the build-gated runtime resolver.'
}

$runtimeEnvironment = Get-Content -Raw -LiteralPath (Join-Path $root 'ReplayFoundry.Desktop\Platform\RuntimePacks\ReplayFoundryRuntimeEnvironment.cs')
if ($runtimeEnvironment -notmatch 'MinimumQwenRuntimeVersion\s*=\s*new\(0, 8, 21\)' -or
    $runtimeEnvironment -notmatch 'MinimumQwenModelVersion\s*=\s*new\(4, 0, 17\)' -or
    $runtimeEnvironment -notmatch 'MinimumMediaToolsVersion\s*=\s*new\(8, 1, 2, 32\)' -or
    $runtimeEnvironment -notmatch 'CreateCompatibleQwenPaths\(' -or
    $runtimeEnvironment -notmatch 'HasExactCurrentDependency\(' -or
    $runtimeEnvironment -notmatch 'dependency\.RequiredManifestHash is not null' -or
    $runtimeEnvironment -notmatch 'dependency\.Accepts\(' -or
    $runtimeEnvironment -notmatch 'Repair or update Advanced AI') {
    throw 'Runtime discovery must reject stale or cross-activated Qwen, runtime, and media pack sets with actionable status.'
}

$installerBuilder = Get-Content -Raw -LiteralPath (Join-Path $root 'eng\Build-ReplayFoundryInstaller.ps1')
if ($installerBuilder -notmatch 'embeddedPayloadCeiling' -or
    $installerBuilder -notmatch 'AdvancedPayloadMode Online' -or
    $installerBuilder -notmatch 'OfferAdvancedAi' -or
    $installerBuilder -notmatch 'verify-catalog') {
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
    $installerDefinition -notmatch 'about 12\.5 GB download' -or
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
if ($packBuilder -notmatch 'replayfoundry-production-host\.txt' -or
    $packBuilder -notmatch 'test_qwen3_vl_output_contract\.py' -or
    $packBuilder -notmatch 'test_qwen3_vl_sampling_audit\.py') {
    throw 'The packaged Qwen host must expose the production command surface and exclude host test entry points.'
}
if ($packBuilder -notmatch 'Assert-RedistributableFfmpeg' -or
    $packBuilder -notmatch '--disable-libopenh264' -or
    $packBuilder -notmatch 'MediaToolsSourceArchiveSha256' -or
    $packBuilder -notmatch "'MediaTools' '8\.1\.2\.32'" -or
    $packBuilder -notmatch "'VisualRuntime' '0\.8\.21'" -or
    $packBuilder -notmatch "'VisualModel' '4\.0\.17'" -or
    $packBuilder -notmatch "packageId='replayfoundry-media-tools';minimumVersion='8\.1\.2\.32';requiredManifestHash=\`$media\.manifest\.manifestHash" -or
    $packBuilder -notmatch "packageId='replayfoundry-qwen3-vl-runtime';minimumVersion='0\.8\.21';requiredManifestHash=\`$visualRuntime\.manifest\.manifestHash") {
    throw 'Advanced packaging must seal Qwen runtime 0.8.21 and model 4.0.17 across exact active media/runtime manifest edges.'
}

Write-Output 'Runtime-pack architecture guard passed.'
