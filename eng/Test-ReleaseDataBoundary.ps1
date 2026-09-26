[CmdletBinding()]
param(
    [ValidateSet(
        'Repository',
        'Publish',
        'RuntimePacks',
        'Installer',
        'SourceExport',
        'DistributionAssets')]
    [string]$Profile = 'Repository',

    [string[]]$Path,

    [string]$RuntimePackBuildRoot,

    [string]$AdvancedCatalogPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$forbiddenFileNames = @(
    'RecentGenerationProjects.json',
    'GenerationAudioRoles.json',
    'studio-project.json',
    'studio-project.json.bak',
    'library-catalog.json',
    'game-context-memory.json',
    'clip-preferences.json',
    'taste-state.json',
    'taste-training-report.json',
    'taste-evaluation-report.json',
    'editorial-metadata-preference-consent.json',
    'editorial-metadata-preferences.json',
    'editorial-reroll-preference.json',
    'bug-report-consent.json',
    'generation-output-location.json',
    'hidden-moment-decisions.json',
    'research-feedback.json',
    'research-participation.json',
    'studio-candidate-decisions.json',
    'youtube-connection-permission.json',
    'youtube-publish-drafts.json',
    'youtube-publish-history.json',
    'youtube-publish-preferences.json',
    'pending-local-data-reset.json'
    'updates-eddsa.dpapi'
)
$forbiddenMediaExtensions = @('.avi', '.m4a', '.mkv', '.mov', '.mp3', '.mp4', '.wav', '.webm')
$textExtensions = @(
    '.cs', '.css', '.html', '.js', '.json', '.map', '.md', '.mjs', '.props',
    '.ps1', '.psd1', '.py', '.svg', '.targets', '.ts', '.tsx', '.txt',
    '.vtt', '.xaml', '.xml', '.yaml', '.yml'
)
$localStateSchemas = @(
    'replayfoundry-game-knowledge-snapshot-',
    'studio-project-1.',
    'user-report-1.0',
    'clip-preference-store-1.0',
    'foundry-taste-local-state-1',
    'foundry-taste-example-1',
    'foundry-taste-checkpoint-1',
    'foundry-taste-training-1',
    'replayfoundry-editorial-reroll-preference-1.0',
    'editorial-metadata-preference-store-1.0',
    'editorial-metadata-preference-learning-consent-1.0',
    'replayfoundry-generation-output-location-1.0',
    'replayfoundry-game-context-memory-1.',
    'replayfoundry-hidden-moment-decisions-1.0',
    'replayfoundry-studio-candidate-decisions-1.0',
    'replayfoundry-library-catalog-1.',
    'replayfoundry-youtube-connection-permission-1.0',
    'replayfoundry-youtube-publish-drafts-1.',
    'replayfoundry-youtube-publish-history-1.',
    'replayfoundry-youtube-publish-preferences-1.0',
    'bug-report-consent-1.0',
    'research-participation-1.0',
    'local-data-reset-1.0'
)
$violations = [Collections.Generic.List[string]]::new()
$personalPathFragments = @(
    [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile),
    $env:USERPROFILE
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
$configurationPathFragments = @($repositoryRoot)
$sourceExportTopologyNames = @(
    'UmVwbGF5Rm91bmRyeS5EZXZlbG9wZXJUb29scw=='
    'UmVwbGF5Rm91bmRyeS5FdmlkZW5jZVRlc3Rz'
    'RGV2ZWxvcGVyIENvbnNvbGU='
    'VmlzdWFsU2VtYW50aWNSZXNlYXJjaA=='
    'RXhwb3J0LVJlcGxheUZvdW5kcnlQcm9kdWN0aW9uUmVwb3NpdG9yeQ=='
    'Q29weS1SZXBsYXlGb3VuZHJ5RGV2ZWxvcG1lbnRTdGF0ZQ=='
    'cXdlbjNfdmxfZGV2ZWxvcG1lbnRfaG9zdC5weQ=='
    'cmVwbGF5Zm91bmRyeV92aXN1YWxfc2VtYW50aWMvZGV2ZWxvcG1lbnRfY2xpLnB5'
    'cmVwbGF5Zm91bmRyeV92aXN1YWxfc2VtYW50aWMvZGV2ZWxvcG1lbnRfY29tbWFuZHMucHk='
    'cmVwbGF5Zm91bmRyeV92aXN1YWxfc2VtYW50aWMvc2FtcGxpbmdfYXVkaXQucHk='
    'cmVwbGF5Zm91bmRyeV92aXN1YWxfc2VtYW50aWMvc2FtcGxpbmdfY2FwdHVyZS5weQ=='
    'cmVwbGF5Zm91bmRyeV92aXN1YWxfc2VtYW50aWMvc2FtcGxpbmdfdGltaW5nLnB5'
    'cmVwbGF5Zm91bmRyeV92aXN1YWxfc2VtYW50aWMvZWRpdG9yaWFsL2NvbnN0cmFpbmVkX2RldmVsb3BtZW50X2NvbW1hbmQucHk='
    'cmVwbGF5Zm91bmRyeV92aXN1YWxfc2VtYW50aWMvZWRpdG9yaWFsL2NvbnN0cmFpbmVkX3BpbG90X2NvbW1hbmQucHk='
    'cmVwbGF5Zm91bmRyeV92aXN1YWxfc2VtYW50aWMvZWRpdG9yaWFsL2RldmVsb3BtZW50X2NvbW1hbmQucHk='
    'cmVwbGF5Zm91bmRyeV92aXN1YWxfc2VtYW50aWMvZWRpdG9yaWFsL3BpbG90X2NvbW1hbmQucHk='
    'cmVwbGF5Zm91bmRyeV92aXN1YWxfc2VtYW50aWMvZWRpdG9yaWFsL3BpbG90X3Byb3RvY29sLnB5'
    'cmVwbGF5Zm91bmRyeV92aXN1YWxfc2VtYW50aWMvZWRpdG9yaWFsL3NhbXBsaW5nX2F1dGhvcml6YXRpb24ucHk='
    'dGVzdF9xd2VuM192bF9zYW1wbGluZ19hdWRpdC5weQ=='
    'dGVzdHMvZ2VuZXJhdGVfcHJvbXB0Ml9hdHRlbXB0X2ZpeHR1cmVzLnB5'
    'dGVzdHMvdGVzdF9kZXZlbG9wbWVudF9ob3N0X3N1cmZhY2UucHk='
    'dGVzdHMvdGVzdF9xd2VuM192bF9jb25zdHJhaW5lZF9kZXZlbG9wbWVudC5weQ=='
    'dGVzdHMvdGVzdF9xd2VuM192bF9lZGl0b3JpYWxfY29udHJhY3QucHk='
    'dGVzdHMvdGVzdF9xd2VuM192bF9lZGl0b3JpYWxfZGV2ZWxvcG1lbnQucHk='
    'dGVzdHMvdGVzdF9xd2VuM192bF9lZGl0b3JpYWxfcGlsb3QucHk='
    'dGVzdHMvdGVzdF9xd2VuM192bF90cnVzdGVkX2lkZW50aXR5X2F0dGVtcHQucHk='
    'VGVzdC1NZWRpYUV2aWRlbmNlQXJjaGl0ZWN0dXJlLnBzMQ=='
    'VGVzdC1WaXN1YWxTZW1hbnRpY0FyY2hpdGVjdHVyZS5wczE='
    'VGVzdC1WaXN1YWxTZW1hbnRpY1Byb21wdDJBcmNoaXRlY3R1cmUucHMx'
    'VGVzdC1WaXN1YWxTZW1hbnRpY1N0cnVjdHVyZWREZWNvZGluZ0FyY2hpdGVjdHVyZS5wczE='
    'VGVzdC1SZWxlYXNlRGF0YUJvdW5kYXJ5QXJjaGl0ZWN0dXJlLnBzMQ=='
) | ForEach-Object {
    [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($_))
}

function Convert-ToPortablePath([string]$Value) {
    return $Value.Replace('\', '/').TrimStart('/')
}

function Test-ForbiddenPath([string]$RelativePath) {
    $portable = Convert-ToPortablePath $RelativePath
    $name = [IO.Path]::GetFileName($portable)
    if ($forbiddenFileNames -contains $name) { return $true }
    if ([IO.Path]::GetExtension($name) -ieq '.dpapi') { return $true }
    if ($name -match '(?i)\.taste-(example|model)\.json$') { return $true }
    if ($portable -match '(?i)(^|/)(Cache/(GameKnowledge|StudioPreview)|Diagnostics/(Outbox|VisualSemanticFailures))(/|$)') {
        return $true
    }
    if ($forbiddenMediaExtensions -contains [IO.Path]::GetExtension($name).ToLowerInvariant()) {
        return $true
    }
    return $false
}

function Test-LocalStateJson([string]$Text) {
    foreach ($schema in $localStateSchemas) {
        if ($Text.Contains($schema, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Get-NormalizedInspectionText([string]$Text) {
    return $Text.Replace('\\', '\').Replace('/', '\')
}

function Test-MachineSpecificText(
    [string]$Text,
    [string]$RelativePath,
    [string]$Extension) {
    $normalized = Get-NormalizedInspectionText $Text
    foreach ($fragment in $personalPathFragments) {
        if ($normalized.Contains($fragment, [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    if ($normalized -match '(?i)\b[A-Z]:\\Users\\[A-Z0-9]{1,6}~[0-9]+(?:\\|\b)') {
        return $true
    }
    if ($Extension -in @('.json', '.props', '.psd1', '.targets') -and
        $normalized -match '(?i)\bC:\\Users\\[^\\\r\n"'']+') {
        return $true
    }
    if ($Extension -in @('.json', '.props', '.psd1', '.targets')) {
        foreach ($fragment in $configurationPathFragments) {
            if ($normalized.Contains($fragment, [StringComparison]::OrdinalIgnoreCase)) {
                return $true
            }
        }
    }
    if ([IO.Path]::GetFileName($RelativePath) -ieq 'launchSettings.json' -and
        $normalized -match '(?i)\b[A-D]:\\') {
        return $true
    }
    return $false
}

function Test-ExcludedSourceTopology([string]$Text) {
    if ($Profile -ne 'SourceExport') { return $false }
    $portable = Convert-ToPortablePath $Text
    foreach ($name in $sourceExportTopologyNames) {
        if ($portable.Contains($name, [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    return $false
}

function Add-FileViolation([string]$Container, [string]$RelativePath, [string]$Reason) {
    $violations.Add("$Container::$((Convert-ToPortablePath $RelativePath)) ($Reason)")
}

function Test-TextStream(
    [IO.Stream]$Stream,
    [string]$Container,
    [string]$RelativePath,
    [long]$Length,
    [string]$Extension) {
    if ($Length -gt 16MB) {
        if ($Extension -ieq '.json') {
            Add-FileViolation $Container $RelativePath 'unexpected oversized JSON'
        }
        return
    }
    $reader = [IO.StreamReader]::new($Stream, [Text.UTF8Encoding]::new($false), $true, 4096, $true)
    try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($Extension -ieq '.json' -and (Test-LocalStateJson $text)) {
        Add-FileViolation $Container $RelativePath 'mutable local-state schema'
    }
    if (Test-MachineSpecificText $text $RelativePath $Extension) {
        Add-FileViolation $Container $RelativePath 'machine-specific absolute path'
    }
    if (Test-ExcludedSourceTopology $text) {
        Add-FileViolation $Container $RelativePath 'excluded source topology'
    }
}

function Get-FileSha256([string]$PathValue) {
    $stream = [IO.FileStream]::new($PathValue, [IO.FileMode]::Open, [IO.FileAccess]::Read,
        [IO.FileShare]::Read, 1048576, [IO.FileOptions]::SequentialScan)
    try { return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
    finally { $stream.Dispose() }
}

function Open-BufferedReleaseArchive([string]$ArchivePath) {
    $stream = [IO.FileStream]::new($ArchivePath, [IO.FileMode]::Open, [IO.FileAccess]::Read,
        [IO.FileShare]::Read, 1048576, [IO.FileOptions]::SequentialScan)
    try { return [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read, $false) }
    catch { $stream.Dispose(); throw }
}

function Get-StreamSha256([IO.Stream]$Stream) {
    $hasher = [Security.Cryptography.SHA256]::Create()
    try {
        return [Convert]::ToHexString($hasher.ComputeHash($Stream))
    } finally {
        $hasher.Dispose()
    }
}

function Test-Sha256Equal([string]$Left, [string]$Right) {
    return $Left.Equals($Right, [StringComparison]::OrdinalIgnoreCase)
}

function Resolve-SafeChild([string]$Root, [string]$RelativePath) {
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or
        [IO.Path]::IsPathFullyQualified($RelativePath) -or
        (Convert-ToPortablePath $RelativePath).Split('/') -contains '..') {
        throw "A release manifest contains an unsafe relative path: $RelativePath"
    }
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $full = [IO.Path]::GetFullPath((Join-Path $rootFull $RelativePath))
    if (-not $full.StartsWith(
        $rootFull + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "A release manifest path escaped its root: $RelativePath"
    }
    return $full
}

function Assert-ManifestFileRecords(
    [string]$Root,
    [object[]]$Records,
    [string[]]$ExcludedRelativePaths) {
    $recordMap = [Collections.Generic.Dictionary[string, object]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($record in $Records) {
        $relative = Convert-ToPortablePath ([string]$record.path)
        [void](Resolve-SafeChild $Root $relative)
        if (-not $recordMap.TryAdd($relative, $record)) {
            throw "A release manifest contains a duplicate path: $relative"
        }
    }
    $excluded = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($relative in $ExcludedRelativePaths) { [void]$excluded.Add($relative) }
    $actual = @(Get-ChildItem -LiteralPath $Root -Recurse -File -Force |
        ForEach-Object {
            Convert-ToPortablePath ([IO.Path]::GetRelativePath($Root, $_.FullName))
        } | Where-Object { -not $excluded.Contains($_) })
    $actualSet = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($relative in $actual) { [void]$actualSet.Add($relative) }
    if ($actualSet.Count -ne $actual.Count) {
        throw "Release payload contains case-insensitive duplicate paths under $Root."
    }
    if ($actualSet.Count -ne $recordMap.Count -or
        @($actualSet | Where-Object { -not $recordMap.ContainsKey($_) }).Count -ne 0) {
        throw "Release payload files differ from their declarative manifest under $Root."
    }
    foreach ($relative in $actualSet) {
        $file = Resolve-SafeChild $Root $relative
        $record = $recordMap[$relative]
        $expectedLength = if ($null -ne $record.size) {
            [long]$record.size
        } else {
            [long]$record.byteLength
        }
        if ((Get-Item -LiteralPath $file).Length -ne $expectedLength -or
            -not (Test-Sha256Equal (Get-FileSha256 $file) ([string]$record.sha256))) {
            throw "Release payload failed manifest verification: $relative"
        }
    }
}

function Assert-PublishManifest([string]$Root) {
    $manifestPath = Join-Path $Root 'release-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Publish boundary requires release-manifest.json under $Root."
    }
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 'replayfoundry-release-manifest-1.1') {
        throw 'Publish boundary found an unsupported release manifest.'
    }
    $expectedChannel = if ($manifest.releaseChannel -eq 'Production') {
        'Production'
    } elseif ($manifest.releaseChannel -eq 'Development') {
        'Development'
    } else {
        throw 'Publish release channel is invalid.'
    }
    if ($manifest.dataChannel -ne $expectedChannel) {
        throw 'Publish release and mutable-data channels do not match.'
    }
    Assert-ManifestFileRecords $Root @($manifest.files) @('release-manifest.json')
    $manifestFiles = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($file in @($manifest.files)) {
        [void]$manifestFiles.Add((Convert-ToPortablePath ([string]$file.path)))
    }
    foreach ($signature in @($manifest.signing.files)) {
        if (-not $manifestFiles.Contains((Convert-ToPortablePath ([string]$signature.path)))) {
            throw "A signed-file record is not sealed by the release manifest: $($signature.path)"
        }
    }
    if ($manifest.releaseChannel -eq 'Production') {
        if ($manifest.sourceTreeDirty -or
            -not $manifest.signing.required -or
            $manifest.signing.mode -ne 'ArtifactSigning' -or
            @($manifest.signing.files | Where-Object { $_.status -ne 'Valid' }).Count -ne 0) {
            throw 'Production publish manifest does not attest a clean, valid signed payload.'
        }
        foreach ($signature in @($manifest.signing.files)) {
            $signedFile = Resolve-SafeChild $Root ([string]$signature.path)
            $actual = Get-AuthenticodeSignature -LiteralPath $signedFile
            if ($actual.Status.ToString() -ne 'Valid' -or
                $null -eq $actual.SignerCertificate -or
                $actual.SignerCertificate.Thumbprint -ne [string]$signature.signerThumbprint) {
                throw "Production Authenticode verification failed: $($signature.path)"
            }
        }
    }
}

function Test-ZipArchive([string]$ArchivePath, [string]$Container) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = Open-BufferedReleaseArchive $ArchivePath
    try {
        foreach ($entry in $archive.Entries) {
            if ([string]::IsNullOrEmpty($entry.Name)) { continue }
            $relative = Convert-ToPortablePath $entry.FullName
            if ($relative -match '(^|/)\.\.(/|$)' -or [IO.Path]::IsPathFullyQualified($relative)) {
                Add-FileViolation $Container $relative 'unsafe archive path'
                continue
            }
            if (Test-ForbiddenPath $relative) {
                Add-FileViolation $Container $relative 'mutable or captured user payload'
            }
            $extension = [IO.Path]::GetExtension($relative).ToLowerInvariant()
            if ($extension -in $textExtensions) {
                $stream = $entry.Open()
                try {
                    Test-TextStream $stream $Container $relative $entry.Length $extension
                } finally { $stream.Dispose() }
            }
        }
    } finally {
        $archive.Dispose()
    }
}

function Test-PayloadFile([string]$FullPath, [string]$Container, [string]$RelativePath) {
    if (Test-ExcludedSourceTopology $RelativePath) {
        Add-FileViolation $Container $RelativePath 'excluded source topology'
    }
    if (Test-ForbiddenPath $RelativePath) {
        Add-FileViolation $Container $RelativePath 'mutable or captured user payload'
    }
    $extension = [IO.Path]::GetExtension($FullPath)
    if ($extension -ieq '.zip') {
        Test-ZipArchive $FullPath $Container
    } elseif ($extension.ToLowerInvariant() -in $textExtensions) {
        $stream = [IO.File]::OpenRead($FullPath)
        try {
            Test-TextStream $stream $Container $RelativePath $stream.Length $extension
        } finally { $stream.Dispose() }
    }
}

function Assert-RuntimeArchiveManifest(
    [string]$ArchivePath,
    [string]$PackageId,
    [string]$ManifestHash) {
    $archive = Open-BufferedReleaseArchive $ArchivePath
    try {
        $entries = @($archive.Entries | Where-Object {
            -not [string]::IsNullOrEmpty($_.Name)
        })
        $manifestEntries = @($entries | Where-Object {
            (Convert-ToPortablePath $_.FullName) -ceq 'runtime-pack-manifest.json'
        })
        if ($manifestEntries.Count -ne 1) {
            throw "Runtime archive has no unique manifest: $PackageId"
        }
        $reader = [IO.StreamReader]::new(
            $manifestEntries[0].Open(),
            [Text.UTF8Encoding]::new($false),
            $true)
        try { $manifestText = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $manifest = $manifestText | ConvertFrom-Json
        if ($manifest.identity.packageId -cne $PackageId -or
            -not (Test-Sha256Equal ([string]$manifest.manifestHash) $ManifestHash)) {
            throw "Runtime archive identity differs from its build index: $PackageId"
        }
        $records = [Collections.Generic.Dictionary[string, object]]::new(
            [StringComparer]::OrdinalIgnoreCase)
        foreach ($file in @($manifest.files)) {
            $relative = Convert-ToPortablePath ([string]$file.relativePath)
            if ([IO.Path]::IsPathFullyQualified($relative) -or
                $relative.Split('/') -contains '..' -or
                -not $records.TryAdd($relative, $file)) {
                throw "Runtime manifest contains an unsafe or duplicate path: $PackageId::$relative"
            }
        }
        $payloadEntries = @($entries | Where-Object {
            (Convert-ToPortablePath $_.FullName) -cne 'runtime-pack-manifest.json'
        })
        $entrySet = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $payloadEntries) {
            $relative = Convert-ToPortablePath $entry.FullName
            $record = if ($records.ContainsKey($relative)) {
                $records[$relative]
            } else {
                $null
            }
            if (-not $entrySet.Add($relative) -or
                $null -eq $record -or
                $entry.Length -ne [long]$record.byteLength) {
                throw "Runtime archive differs from its internal manifest: $PackageId::$relative"
            }
            $entryStream = $entry.Open()
            try {
                $entryHash = Get-StreamSha256 $entryStream
            } finally {
                $entryStream.Dispose()
            }
            if (-not (Test-Sha256Equal $entryHash ([string]$record.sha256))) {
                throw "Runtime archive content hash differs from its internal manifest: $PackageId::$relative"
            }
        }
        if ($entrySet.Count -ne $records.Count) {
            throw "Runtime archive file set differs from its internal manifest: $PackageId"
        }
    } finally {
        $archive.Dispose()
    }
}

function Assert-RuntimePackIndex([string]$Root) {
    $indexPath = Join-Path $Root 'runtime-pack-build-index.json'
    if (-not (Test-Path -LiteralPath $indexPath -PathType Leaf)) {
        throw "Runtime-pack boundary requires runtime-pack-build-index.json under $Root."
    }
    $index = Get-Content -Raw -LiteralPath $indexPath | ConvertFrom-Json
    if ($index.schemaVersion -ne 'replayfoundry-runtime-pack-build-1.0' -or
        $index.profile -notin @('Base', 'Advanced')) {
        throw 'Runtime-pack boundary found an unsupported build index.'
    }
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $indexedArchives = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    $packageIds = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($pack in @($index.packs)) {
        $packageId = [string]$pack.packageId
        if ($packageId -notmatch '^[a-z0-9][a-z0-9.-]{0,127}$' -or
            -not $packageIds.Add($packageId)) {
            throw "Runtime-pack index contains an invalid or duplicate package ID: $packageId"
        }
        $archiveRelative = Convert-ToPortablePath ([string]$pack.archive)
        $archive = Resolve-SafeChild $rootFull $archiveRelative
        if ($archiveRelative -cne "archives/$packageId.zip" -or
            -not $indexedArchives.Add($archive)) {
            throw "Runtime-pack index points outside its exact archive set: $packageId"
        }
        if (-not (Test-Path -LiteralPath $archive -PathType Leaf) -or
            (Get-Item -LiteralPath $archive).Length -ne [long]$pack.byteLength -or
            -not (Test-Sha256Equal (Get-FileSha256 $archive) ([string]$pack.sha256))) {
            throw "Runtime-pack archive failed build-index verification: $packageId"
        }
        Assert-RuntimeArchiveManifest $archive $packageId ([string]$pack.manifestHash)
    }
    $actualArchives = @(Get-ChildItem -LiteralPath (Join-Path $Root 'archives') -File -Filter '*.zip' |
        ForEach-Object { $_.FullName })
    if ($actualArchives.Count -ne $indexedArchives.Count -or
        @($actualArchives | Where-Object { -not $indexedArchives.Contains($_) }).Count -ne 0) {
        throw 'Runtime-pack archive directory differs from its build index.'
    }
}

function Assert-InstallerManifest(
    [string]$Root,
    [string]$RuntimeRoot,
    [string]$CatalogPath) {
    $manifestPath = Join-Path $Root 'installer-release-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Installer boundary requires installer-release-manifest.json under $Root."
    }
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 'replayfoundry-installer-release-manifest-1.1') {
        throw 'Installer boundary found an unsupported release manifest.'
    }
    $installer = Resolve-SafeChild $Root (Join-Path 'installer' ([string]$manifest.installer.fileName))
    if (-not (Test-Path -LiteralPath $installer -PathType Leaf) -or
        (Get-Item -LiteralPath $installer).Length -ne [long]$manifest.installer.byteLength -or
        -not (Test-Sha256Equal (Get-FileSha256 $installer) ([string]$manifest.installer.sha256))) {
        throw 'Installer executable differs from its release manifest.'
    }
    $appManifest = Resolve-SafeChild $Root 'app/release-manifest.json'
    if (-not (Test-Sha256Equal (Get-FileSha256 $appManifest) ([string]$manifest.appReleaseManifest.sha256))) {
        throw 'Installer manifest does not bind the application release manifest.'
    }
    Assert-PublishManifest (Join-Path $Root 'app')
    $brandingManifest = Resolve-SafeChild $Root 'branding/installer-branding-manifest.json'
    if (-not (Test-Sha256Equal (Get-FileSha256 $brandingManifest) ([string]$manifest.installerBrandingManifestSha256))) {
        throw 'Installer manifest does not bind the branding manifest.'
    }
    if ([string]::IsNullOrWhiteSpace($RuntimeRoot)) {
        throw 'Installer boundary requires -RuntimePackBuildRoot.'
    }
    $runtimeIndex = Join-Path ([IO.Path]::GetFullPath($RuntimeRoot)) 'runtime-pack-build-index.json'
    if (-not (Test-Sha256Equal (Get-FileSha256 $runtimeIndex) ([string]$manifest.runtimePackBuildIndexSha256))) {
        throw 'Installer manifest does not bind the runtime-pack build index.'
    }
    Assert-RuntimePackIndex ([IO.Path]::GetFullPath($RuntimeRoot))
    if ($null -ne $manifest.advancedCatalogSha256) {
        $catalogHash = if ([string]::IsNullOrWhiteSpace($CatalogPath)) {
            $null
        } else {
            Get-FileSha256 ([IO.Path]::GetFullPath($CatalogPath))
        }
        if ($null -eq $catalogHash -or
            -not (Test-Sha256Equal $catalogHash ([string]$manifest.advancedCatalogSha256))) {
            throw 'Installer manifest does not bind the Advanced AI catalog.'
        }
    }
    if ($manifest.releaseChannel -eq 'Production') {
        $signature = Get-AuthenticodeSignature -LiteralPath $installer
        if ($manifest.sourceTreeDirty -or
            -not $manifest.signing.required -or
            $manifest.signing.mode -ne 'ArtifactSigning' -or
            $manifest.signing.status -ne 'Valid' -or
            $signature.Status.ToString() -ne 'Valid' -or
            $null -eq $signature.SignerCertificate -or
            $signature.SignerCertificate.Thumbprint -ne [string]$manifest.signing.signerThumbprint) {
            throw 'Production installer is not a clean, valid signed payload.'
        }
    }
}

function Assert-NoReparsePoint([string]$FullPath) {
    $current = [IO.Path]::GetFullPath($FullPath)
    while (-not [string]::IsNullOrWhiteSpace($current)) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Release data-boundary inspection cannot traverse a reparse point: $current"
            }
        }
        $parent = [IO.Directory]::GetParent($current)
        if ($null -eq $parent) { break }
        $current = $parent.FullName
    }
}

if ($Profile -eq 'Repository') {
    if ($null -ne $Path -and $Path.Count -gt 0) {
        throw 'Repository profile resolves its path from the script location.'
    }
    $gitProbe = [string](& git -C $repositoryRoot rev-parse --is-inside-work-tree 2>$null)
    $isGitRepository = $LASTEXITCODE -eq 0 -and $gitProbe.Trim() -eq 'true'
    if ($isGitRepository) {
        $paths = @(
            & git -C $repositoryRoot ls-files --cached --others --exclude-standard
        )
        if ($LASTEXITCODE -ne 0) {
            throw 'Could not enumerate repository files for data-boundary inspection.'
        }
    } elseif (Test-Path -LiteralPath `
        (Join-Path $repositoryRoot '.replayfoundry-public-source') -PathType Leaf) {
        $paths = @(Get-ChildItem -LiteralPath $repositoryRoot -Recurse -File -Force |
            Where-Object {
                $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
            } | ForEach-Object {
                [IO.Path]::GetRelativePath($repositoryRoot, $_.FullName)
            })
    } else {
        throw 'Repository data-boundary inspection requires a Git worktree or sealed public source.'
    }
    foreach ($relative in $paths) {
        $full = Join-Path $repositoryRoot $relative
        if (Test-Path -LiteralPath $full -PathType Leaf) {
            Test-PayloadFile $full 'repository' $relative
        }
    }
} else {
    if ($null -eq $Path -or $Path.Count -eq 0) {
        throw "$Profile data-boundary inspection requires at least one -Path."
    }
    foreach ($candidate in $Path) {
        $full = [IO.Path]::GetFullPath($candidate)
        Assert-NoReparsePoint $full
        if (Test-Path -LiteralPath $full -PathType Leaf) {
            Test-PayloadFile $full $full ([IO.Path]::GetFileName($full))
            continue
        }
        if (-not (Test-Path -LiteralPath $full -PathType Container)) {
            throw "Release data-boundary path was not found: $full"
        }
        foreach ($file in Get-ChildItem -LiteralPath $full -Recurse -File -Force) {
            if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Release data-boundary inspection found a reparse point: $($file.FullName)"
            }
            $relative = [IO.Path]::GetRelativePath($full, $file.FullName)
            if ($Profile -eq 'Installer' -and
                (Convert-ToPortablePath $relative).StartsWith(
                    'reports/',
                    [StringComparison]::OrdinalIgnoreCase)) {
                continue
            }
            Test-PayloadFile $file.FullName $full $relative
        }
    }
}

if ($Profile -eq 'Publish') {
    if ($Path.Count -ne 1 -or
        -not (Test-Path -LiteralPath $Path[0] -PathType Container)) {
        throw 'Publish profile requires exactly one publish directory.'
    }
    Assert-PublishManifest ([IO.Path]::GetFullPath($Path[0]))
} elseif ($Profile -eq 'RuntimePacks') {
    if ($Path.Count -ne 1 -or
        -not (Test-Path -LiteralPath $Path[0] -PathType Container)) {
        throw 'RuntimePacks profile requires exactly one runtime-pack build directory.'
    }
    Assert-RuntimePackIndex ([IO.Path]::GetFullPath($Path[0]))
} elseif ($Profile -eq 'Installer') {
    if ($Path.Count -ne 1 -or
        -not (Test-Path -LiteralPath $Path[0] -PathType Container)) {
        throw 'Installer profile requires exactly one installer artifact directory.'
    }
    Assert-InstallerManifest `
        ([IO.Path]::GetFullPath($Path[0])) `
        $RuntimePackBuildRoot `
        $AdvancedCatalogPath
}

if ($violations.Count -gt 0) {
    throw "Release data boundary rejected mutable or machine-specific payloads:$([Environment]::NewLine)$($violations -join [Environment]::NewLine)"
}

Write-Output "Release data boundary passed for $Profile."
