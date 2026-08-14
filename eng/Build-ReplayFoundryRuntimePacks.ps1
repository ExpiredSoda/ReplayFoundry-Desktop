[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Base', 'Advanced')]
    [string]$Profile,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [Parameter(Mandatory = $true)]
    [DateTimeOffset]$CreatedAtUtc,

    [Parameter(Mandatory = $true)]
    [string]$MediaToolsRoot,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$MediaToolsArchiveSha256,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^https://')]
    [string]$MediaToolsArchiveUrl,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^https://')]
    [string]$MediaToolsSourceArchiveUrl,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$MediaToolsSourceArchiveSha256,

    [string]$SileroModelPath,
    [string]$SileroLicensePath,
    [string]$WhisperVadModelPath,
    [string]$WhisperRuntimeRoot,
    [string]$WhisperModelRoot,
    [string]$PythonHome,
    [string]$PythonSitePackages,
    [string]$PythonNoticesRoot,
    [string]$QwenHostScriptPath,
    [string]$QwenModelRoot,
    [string]$QwenModelManifestPath,
    [string]$QwenPromptManifestPath,
    [string]$QwenQualificationLockPath,
    [string]$QwenLicensePath
)

$ErrorActionPreference = 'Stop'
if ($CreatedAtUtc.Offset -ne [TimeSpan]::Zero) { throw 'CreatedAtUtc must use UTC.' }
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) {
    if (@(Get-ChildItem -LiteralPath $outputRoot -Force).Count -gt 0) { throw "Runtime-pack output must be empty: $outputRoot" }
} else { New-Item -ItemType Directory -Path $outputRoot | Out-Null }
$packRoots = Join-Path $outputRoot 'packs'
$recipeRoot = Join-Path $outputRoot 'recipes'
$archiveRoot = Join-Path $outputRoot 'archives'
New-Item -ItemType Directory -Path $packRoots,$recipeRoot,$archiveRoot | Out-Null

function Assert-File([string]$PathValue, [string]$Label) {
    if ([string]::IsNullOrWhiteSpace($PathValue) -or -not (Test-Path -LiteralPath $PathValue -PathType Leaf)) { throw "$Label is required and must exist." }
    return [IO.Path]::GetFullPath($PathValue)
}
function Assert-Directory([string]$PathValue, [string]$Label) {
    if ([string]::IsNullOrWhiteSpace($PathValue) -or -not (Test-Path -LiteralPath $PathValue -PathType Container)) { throw "$Label is required and must exist." }
    return [IO.Path]::GetFullPath($PathValue)
}
function File-Hash([string]$PathValue) { return (Get-FileHash -Algorithm SHA256 -LiteralPath $PathValue).Hash }
function Assert-RedistributableFfmpeg([string]$MediaRoot) {
    $ffmpeg = Assert-File (Join-Path $MediaRoot 'bin\ffmpeg.exe') 'FFmpeg executable'
    $ffprobe = Assert-File (Join-Path $MediaRoot 'bin\ffprobe.exe') 'FFprobe executable'
    $buildConfiguration = (& $ffmpeg -hide_banner -buildconf 2>&1) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw 'FFmpeg build-configuration inspection failed.' }
    foreach ($required in @('--enable-shared', '--disable-static', '--disable-libopenh264')) {
        if ($buildConfiguration -notmatch [regex]::Escape($required)) {
            throw "FFmpeg must declare $required before it can be sealed into a media pack."
        }
    }
    foreach ($forbidden in @('--enable-gpl', '--enable-nonfree', '--enable-libopenh264')) {
        if ($buildConfiguration -match [regex]::Escape($forbidden)) {
            throw "FFmpeg contains a forbidden release configuration: $forbidden."
        }
    }
    $encoders = (& $ffmpeg -hide_banner -encoders 2>&1) -join "`n"
    if ($LASTEXITCODE -ne 0 -or $encoders -notmatch '(?m)^\s*V.*\sh264_mf\s') {
        throw 'FFmpeg must expose the Windows Media Foundation H.264 encoder.'
    }
    $decoders = (& $ffmpeg -hide_banner -decoders 2>&1) -join "`n"
    if ($LASTEXITCODE -ne 0 -or $decoders -notmatch '(?m)^\s*A\S*\s+opus\s') {
        throw 'FFmpeg must retain its native Opus decoder.'
    }
    if ($decoders -match '(?m)^\s*A\S*\s+libopus\s') {
        throw 'FFmpeg must not link the external libopus decoder.'
    }
    & $ffprobe -hide_banner -version | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'FFprobe launch verification failed.' }
}
function Copy-Tree([string]$Source, [string]$Destination, [string[]]$ExcludedDirectoryNames = @()) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    $sourceRoot = [IO.Path]::GetFullPath($Source).TrimEnd([IO.Path]::DirectorySeparatorChar)
    foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Force) {
        $relative = [IO.Path]::GetRelativePath($sourceRoot, $file.FullName)
        $segments = $relative -split '[\\/]'
        if ($file.Extension -ieq '.pyc' -or
            $segments -contains '__pycache__' -or
            ($segments | Where-Object { $ExcludedDirectoryNames -contains $_ })) { continue }
        $target = Join-Path $Destination $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }
}
function Seal-Pack([string]$Id, [hashtable]$Recipe) {
    $packRoot = Join-Path $packRoots $Id
    $recipePath = Join-Path $recipeRoot "$Id.json"
    $Recipe | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $recipePath -Encoding utf8NoBOM
    dotnet run --project (Join-Path $repoRoot 'ReplayFoundry.RuntimeInstaller\ReplayFoundry.RuntimeInstaller.csproj') -- `
        create-manifest --source $packRoot --recipe $recipePath | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw "Manifest generation failed for $Id." }
    dotnet run --project (Join-Path $repoRoot 'ReplayFoundry.RuntimeInstaller\ReplayFoundry.RuntimeInstaller.csproj') -- `
        verify --source $packRoot | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw "Pack verification failed for $Id." }
    $archive = Join-Path $archiveRoot "$Id.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($packRoot, $archive, [IO.Compression.CompressionLevel]::NoCompression, $false)
    return [ordered]@{
        packageId = $Id
        root = $packRoot
        archive = $archive
        byteLength = (Get-Item -LiteralPath $archive).Length
        sha256 = File-Hash $archive
        manifest = (Get-Content -Raw -LiteralPath (Join-Path $packRoot 'runtime-pack-manifest.json') | ConvertFrom-Json)
    }
}
function Recipe-Base([string]$Id, [string]$Kind, [string]$Version, [string]$Name, [string]$Backend, [hashtable]$Entries, [object[]]$Licenses, [object[]]$Sources, [object[]]$Dependencies = @()) {
    return [ordered]@{
        packageId = $Id; kind = $Kind; semanticVersion = $Version; displayName = $Name; backend = $Backend
        entries = $Entries; dependencies = $Dependencies; licenses = $Licenses; sources = $Sources
        replayFoundryMinimumVersion = '0.1.0'; replayFoundryMaximumVersionExclusive = '1.0.0'
        createdAtUtc = $CreatedAtUtc.ToString('O')
    }
}
function Test-QwenRuntimeHost([string]$RuntimeRoot, [string]$MediaRoot) {
    $saved = @{
        PYTHONHOME = $env:PYTHONHOME
        PYTHONPATH = $env:PYTHONPATH
        PATH = $env:PATH
        HF_HUB_OFFLINE = $env:HF_HUB_OFFLINE
        TRANSFORMERS_OFFLINE = $env:TRANSFORMERS_OFFLINE
        PYTHONDONTWRITEBYTECODE = $env:PYTHONDONTWRITEBYTECODE
    }
    try {
        $env:PYTHONHOME = Join-Path $RuntimeRoot 'python'
        $pythonHome = Join-Path $RuntimeRoot 'python'
        $sitePackages = Join-Path $RuntimeRoot 'site-packages'
        $env:PYTHONPATH = (Join-Path $RuntimeRoot 'host') + [IO.Path]::PathSeparator + $sitePackages
        $env:PATH = @(
            $pythonHome
            (Join-Path $pythonHome 'DLLs')
            (Join-Path $sitePackages 'torch\lib')
            (Join-Path $sitePackages 'tvm_ffi\lib')
            (Join-Path $MediaRoot 'bin')
            (Join-Path $env:WINDIR 'System32')
            $env:WINDIR
        ) -join [IO.Path]::PathSeparator
        $env:HF_HUB_OFFLINE = '1'
        $env:TRANSFORMERS_OFFLINE = '1'
        $env:PYTHONDONTWRITEBYTECODE = '1'
        & (Join-Path $RuntimeRoot 'python\python.exe') -B `
            (Join-Path $RuntimeRoot 'host\qwen3_vl_batch_host.py') --help | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Packaged Qwen host launch failed with exit code $LASTEXITCODE." }
        $bytecodeFiles = @(Get-ChildItem -LiteralPath $RuntimeRoot -Recurse -File -Filter '*.pyc')
        if ($bytecodeFiles.Count -ne 0) {
            throw "Packaged Qwen host launch created $($bytecodeFiles.Count) Python bytecode file(s) inside the runtime pack."
        }
    }
    finally {
        $env:PYTHONHOME = $saved.PYTHONHOME
        $env:PYTHONPATH = $saved.PYTHONPATH
        $env:PATH = $saved.PATH
        $env:HF_HUB_OFFLINE = $saved.HF_HUB_OFFLINE
        $env:TRANSFORMERS_OFFLINE = $saved.TRANSFORMERS_OFFLINE
        $env:PYTHONDONTWRITEBYTECODE = $saved.PYTHONDONTWRITEBYTECODE
    }
}
function Assert-QwenDeploymentQualification([string]$RuntimeRoot, [string]$QualificationLockPath) {
    $lock = Get-Content -Raw -LiteralPath $QualificationLockPath | ConvertFrom-Json
    if ($lock.schemaVersion -ne 'visual-semantic-editorial-structured-decoding-lock-1.0' -or
        $lock.capabilitySucceeded -ne $true -or
        $lock.unconstrainedFallbackPermitted -ne $false -or
        $lock.semanticRepairPermitted -ne $false) {
        throw 'QwenQualificationLockPath is not a successful strict structured-decoding deployment lock.'
    }
    $packagedPythonHash = File-Hash (Join-Path $RuntimeRoot 'python\python.exe')
    if ($packagedPythonHash -ne $lock.pythonExecutableSha256) {
        throw "QwenQualificationLockPath authorizes Python $($lock.pythonExecutableSha256), not packaged Python $packagedPythonHash."
    }
}

$mediaSource = Assert-Directory $MediaToolsRoot 'MediaToolsRoot'
Assert-RedistributableFfmpeg $mediaSource
$mediaPack = Join-Path $packRoots 'replayfoundry-media-tools'
New-Item -ItemType Directory -Path (Join-Path $mediaPack 'bin') -Force | Out-Null
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $mediaSource 'bin') -File | Where-Object Name -ne 'ffplay.exe') {
    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $mediaPack 'bin')
}
$mediaLicense = Assert-File (Join-Path $mediaSource 'LICENSE.txt') 'FFmpeg license'
Copy-Item -LiteralPath $mediaLicense -Destination (Join-Path $mediaPack 'LICENSE-FFmpeg.txt')
$media = Seal-Pack 'replayfoundry-media-tools' (Recipe-Base `
    'replayfoundry-media-tools' 'MediaTools' '8.1.2.32' 'Replay Foundry FFmpeg media tools' 'Cpu' `
    @{ FfmpegExecutable = 'bin/ffmpeg.exe'; FfprobeExecutable = 'bin/ffprobe.exe' } `
    @([ordered]@{ componentName='FFmpeg shared LGPL build'; licenseIdentifier='LGPL-3.0-or-later'; textRelativePath='LICENSE-FFmpeg.txt'; textSha256=(File-Hash (Join-Path $mediaPack 'LICENSE-FFmpeg.txt')); sourceUrl=$MediaToolsSourceArchiveUrl; redistributionNotes='Replay Foundry pinned LGPL shared build with OpenH264, GPL, and nonfree components excluded. Exact corresponding source and build evidence are published at sourceUrl.' }) `
    @(
        [ordered]@{ officialUrl=$MediaToolsArchiveUrl; revision='ffmpeg-8c9502e9b048e21e1cae96477e338ac0635645ba'; artifactSha256=$MediaToolsArchiveSha256.ToUpperInvariant() },
        [ordered]@{ officialUrl=$MediaToolsSourceArchiveUrl; revision='corresponding-source-8c9502e9b048e21e1cae96477e338ac0635645ba'; artifactSha256=$MediaToolsSourceArchiveSha256.ToUpperInvariant() }
    ))

$results = [Collections.Generic.List[object]]::new()
$results.Add($media)
if ($Profile -eq 'Advanced') {
    $sileroModel = Assert-File $SileroModelPath 'SileroModelPath'
    $sileroLicense = Assert-File $SileroLicensePath 'SileroLicensePath'
    $whisperVadModel = Assert-File $WhisperVadModelPath 'WhisperVadModelPath'
    $whisperVadHash = File-Hash $whisperVadModel
    if ($whisperVadHash -ne '2AA269B785EEB53A82983A20501DDF7C1D9C48E33AB63A41391AC6C9F7FB6987') {
        throw "WhisperVadModelPath must be the pinned official ggml-silero-v6.2.0 model; received $whisperVadHash."
    }
    $sileroPack = Join-Path $packRoots 'replayfoundry-silero-vad'
    New-Item -ItemType Directory -Path $sileroPack | Out-Null
    Copy-Item $sileroModel (Join-Path $sileroPack 'silero_vad.onnx')
    Copy-Item $whisperVadModel (Join-Path $sileroPack 'ggml-silero-v6.2.0.bin')
    Copy-Item $sileroLicense (Join-Path $sileroPack 'LICENSE-Silero.txt')
    $silero = Seal-Pack 'replayfoundry-silero-vad' (Recipe-Base `
        'replayfoundry-silero-vad' 'SpeechActivity' '6.2.1.1' 'Silero speech timing' 'Cpu' `
        @{ SpeechActivityModel='silero_vad.onnx'; WhisperVadModel='ggml-silero-v6.2.0.bin' } `
        @([ordered]@{componentName='Silero VAD';licenseIdentifier='MIT';textRelativePath='LICENSE-Silero.txt';textSha256=(File-Hash (Join-Path $sileroPack 'LICENSE-Silero.txt'));sourceUrl='https://github.com/snakers4/silero-vad/tree/v6.2.1';redistributionNotes='Official v6.2.1 ONNX model plus the official whisper.cpp GGML conversion of Silero v6.2.0 for timestamp-aligned transcription.'}) `
        @([ordered]@{officialUrl='https://github.com/snakers4/silero-vad/tree/v6.2.1';revision='v6.2.1';artifactSha256=(File-Hash $sileroModel)},[ordered]@{officialUrl='https://huggingface.co/ggml-org/whisper-vad/resolve/main/ggml-silero-v6.2.0.bin';revision='ggml-silero-v6.2.0';artifactSha256=$whisperVadHash}))
    $results.Add($silero)

    $whisperRuntimeSource = Assert-Directory $WhisperRuntimeRoot 'WhisperRuntimeRoot'
    $whisperRuntimePack = Join-Path $packRoots 'replayfoundry-whisper-cpp'
    New-Item -ItemType Directory -Path $whisperRuntimePack | Out-Null
    Get-ChildItem -LiteralPath $whisperRuntimeSource -File | Where-Object Name -notmatch 'manifest' | Copy-Item -Destination $whisperRuntimePack
    $whisperLicenseName = (Get-ChildItem -LiteralPath $whisperRuntimePack -File | Where-Object Name -match 'LICENSE' | Select-Object -First 1).Name
    $whisperRuntime = Seal-Pack 'replayfoundry-whisper-cpp' (Recipe-Base `
        'replayfoundry-whisper-cpp' 'TranscriptionRuntime' '1.9.1' 'whisper.cpp CPU runtime' 'Cpu' `
        @{ WhisperExecutable='whisper-cli.exe' } `
        @([ordered]@{componentName='whisper.cpp';licenseIdentifier='MIT';textRelativePath=$whisperLicenseName;textSha256=(File-Hash (Join-Path $whisperRuntimePack $whisperLicenseName));sourceUrl='https://github.com/ggml-org/whisper.cpp/tree/f049fff95a089aa9969deb009cdd4892b3e74916';redistributionNotes='Official Windows x64 CPU release.'}) `
        @([ordered]@{officialUrl='https://github.com/ggml-org/whisper.cpp/releases/download/v1.9.1/whisper-bin-x64.zip';revision='f049fff95a089aa9969deb009cdd4892b3e74916';artifactSha256='7D8BE46ECD31828E1EB7A2ECDD0D6B314FEAFD82163038AB6092594B0A063539'}))
    $results.Add($whisperRuntime)

    $whisperModelSource = Assert-Directory $WhisperModelRoot 'WhisperModelRoot'
    $whisperModelPack = Join-Path $packRoots 'replayfoundry-whisper-small-multilingual'
    New-Item -ItemType Directory -Path $whisperModelPack | Out-Null
    Get-ChildItem -LiteralPath $whisperModelSource -File | Where-Object Name -notmatch 'manifest' | Copy-Item -Destination $whisperModelPack
    $whisperModel = Seal-Pack 'replayfoundry-whisper-small-multilingual' (Recipe-Base `
        'replayfoundry-whisper-small-multilingual' 'TranscriptionModel' '1.0.0' 'Whisper multilingual small model' 'General' `
        @{ WhisperModel='ggml-small.bin' } `
        @([ordered]@{componentName='OpenAI Whisper model weights';licenseIdentifier='MIT';textRelativePath='LICENSE-openai-whisper.txt';textSha256=(File-Hash (Join-Path $whisperModelPack 'LICENSE-openai-whisper.txt'));sourceUrl='https://github.com/openai/whisper';redistributionNotes='Official GGML conversion from the pinned whisper.cpp model repository. Selected after a bounded five-clip creator-footage comparison; this is not a universal accuracy claim.'}) `
        @([ordered]@{officialUrl='https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-small.bin';revision='5359861c739e955e79d9a303bcbc70fb988958b1';artifactSha256='1BE3A9B2063867B937E64E2EC7483364A79917E157FA98C5D94B5C1FFFEA987B'}) `
        @([ordered]@{packageId='replayfoundry-whisper-cpp';minimumVersion='1.9.1';requiredManifestHash=$whisperRuntime.manifest.manifestHash}))
    $results.Add($whisperModel)

    $pythonRoot = Assert-Directory $PythonHome 'PythonHome'
    $sitePackages = Assert-Directory $PythonSitePackages 'PythonSitePackages'
    $pythonNotices = Assert-Directory $PythonNoticesRoot 'PythonNoticesRoot'
    $hostScript = Assert-File $QwenHostScriptPath 'QwenHostScriptPath'
    $hostRoot = Split-Path -Parent $hostScript
    if (-not (Test-Path -LiteralPath (Join-Path $hostRoot 'replayfoundry_visual_semantic\cli.py') -PathType Leaf)) {
        throw 'QwenHostScriptPath must belong to the complete Replay Foundry visual-semantic host tree.'
    }
    $visualRuntimePack = Join-Path $packRoots 'replayfoundry-qwen3-vl-runtime'
    Copy-Tree $pythonRoot (Join-Path $visualRuntimePack 'python') @('site-packages','Doc','include','Tools','tcl')
    Copy-Tree $sitePackages (Join-Path $visualRuntimePack 'site-packages') @('__pycache__')
    Copy-Tree $pythonNotices (Join-Path $visualRuntimePack 'notices')
    $packagedHostRoot = Join-Path $visualRuntimePack 'host'
    Copy-Tree $hostRoot $packagedHostRoot @('__pycache__','tests')
    Remove-Item -LiteralPath @(
        (Join-Path $packagedHostRoot 'test_qwen3_vl_output_contract.py'),
        (Join-Path $packagedHostRoot 'test_qwen3_vl_sampling_audit.py')
    ) -Force
    Set-Content -LiteralPath (Join-Path $packagedHostRoot 'replayfoundry-production-host.txt') `
        -Value 'Replay Foundry packaged production host' -Encoding utf8NoBOM
    Test-QwenRuntimeHost $visualRuntimePack $mediaPack
    $visualRuntime = Seal-Pack 'replayfoundry-qwen3-vl-runtime' (Recipe-Base `
        'replayfoundry-qwen3-vl-runtime' 'VisualRuntime' '0.8.21' 'Qwen3-VL CUDA runtime' 'Cuda' `
        @{PythonExecutable='python/python.exe';VisualHostScript='host/qwen3_vl_batch_host.py'} `
        @([ordered]@{componentName='CPython and pinned Qwen runtime wheels';licenseIdentifier='Multiple-see-notices';textRelativePath='notices/THIRD-PARTY-NOTICES.md';textSha256=(File-Hash (Join-Path $visualRuntimePack 'notices\THIRD-PARTY-NOTICES.md'));sourceUrl='https://www.python.org/downloads/windows/';redistributionNotes='Exact component inventory and retained license texts are included under notices/licenses.'}) `
        @([ordered]@{officialUrl='https://www.python.org/downloads/release/python-3119/';revision='3.11.9';artifactSha256=(File-Hash (Join-Path $pythonRoot 'python.exe'))},[ordered]@{officialUrl='https://pytorch.org/get-started/locally/';revision='torch-2.12.0+cu130';artifactSha256='07F0D0520196071C336391C174B9B9AB8AECA8518749B2A570D017521960F8D6'}) `
                @([ordered]@{packageId='replayfoundry-media-tools';minimumVersion='8.1.2.32';requiredManifestHash=$media.manifest.manifestHash}))
    $results.Add($visualRuntime)

    $modelRoot = Assert-Directory $QwenModelRoot 'QwenModelRoot'
    $modelManifest = Assert-File $QwenModelManifestPath 'QwenModelManifestPath'
    $promptManifest = Assert-File $QwenPromptManifestPath 'QwenPromptManifestPath'
    $qualificationLock = Assert-File $QwenQualificationLockPath 'QwenQualificationLockPath'
    $qwenLicense = Assert-File $QwenLicensePath 'QwenLicensePath'
    $visualModelPack = Join-Path $packRoots 'replayfoundry-qwen3-vl-4b-instruct'
    Copy-Tree $modelRoot (Join-Path $visualModelPack 'model') @('.cache')
    New-Item -ItemType Directory -Path (Join-Path $visualModelPack 'config') | Out-Null
    Copy-Item $modelManifest (Join-Path $visualModelPack 'config\model-manifest.json')
    Copy-Item $promptManifest (Join-Path $visualModelPack 'config\prompt-manifest.json')
    Copy-Item $qualificationLock (Join-Path $visualModelPack 'config\qualification-lock.json')
    Copy-Item $qwenLicense (Join-Path $visualModelPack 'LICENSE-Qwen.txt')
    Assert-QwenDeploymentQualification $visualRuntimePack $qualificationLock
    $visualModel = Seal-Pack 'replayfoundry-qwen3-vl-4b-instruct' (Recipe-Base `
        'replayfoundry-qwen3-vl-4b-instruct' 'VisualModel' '4.0.17' 'Qwen3-VL 4B Instruct' 'Cuda' `
        @{QwenModelManifest='config/model-manifest.json';QwenPromptManifest='config/prompt-manifest.json';QwenQualificationLock='config/qualification-lock.json'} `
        @([ordered]@{componentName='Qwen3-VL 4B Instruct';licenseIdentifier='Apache-2.0';textRelativePath='LICENSE-Qwen.txt';textSha256=(File-Hash (Join-Path $visualModelPack 'LICENSE-Qwen.txt'));sourceUrl='https://huggingface.co/Qwen/Qwen3-VL-4B-Instruct/tree/ebb281ec70b05090aa6165b016eac8ec08e71b17';redistributionNotes='Locally qualified for the bounded Replay Foundry workflow. Generated wording remains user-reviewable and no universal semantic-accuracy claim is made.'}) `
        @([ordered]@{officialUrl='https://huggingface.co/Qwen/Qwen3-VL-4B-Instruct/tree/ebb281ec70b05090aa6165b016eac8ec08e71b17';revision='ebb281ec70b05090aa6165b016eac8ec08e71b17';artifactSha256='2018FFABE5257D8045BD565A232D82DA382679C9E71C388F6880BFF01ACF17B4'}) `
        @([ordered]@{packageId='replayfoundry-qwen3-vl-runtime';minimumVersion='0.8.21';requiredManifestHash=$visualRuntime.manifest.manifestHash}))
    $results.Add($visualModel)
}

$index = [ordered]@{
    schemaVersion='replayfoundry-runtime-pack-build-1.0'
    profile=$Profile
    createdAtUtc=$CreatedAtUtc.ToString('O')
    packs=@($results | ForEach-Object { [ordered]@{packageId=$_.packageId;archive=$_.archive;byteLength=$_.byteLength;sha256=$_.sha256;manifestHash=$_.manifest.manifestHash} })
}
$indexPath = Join-Path $outputRoot 'runtime-pack-build-index.json'
$index | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $indexPath -Encoding utf8NoBOM
Write-Host "Built $($results.Count) verified $Profile runtime packs under $outputRoot"
Write-Host "Index: $indexPath"
