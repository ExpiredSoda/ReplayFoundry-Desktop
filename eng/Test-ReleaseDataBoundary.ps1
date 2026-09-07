[CmdletBinding()]
param(
    [ValidateSet('Repository', 'Publish', 'RuntimePacks', 'Installer', 'DistributionAssets')]
    [string]$Profile = 'Repository',
    [string[]]$Path,
    [string]$RuntimePackBuildRoot,
    [string]$AdvancedCatalogPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$violations = [Collections.Generic.List[string]]::new()
$forbiddenFileNames = @(
    'RecentGenerationProjects.json', 'GenerationAudioRoles.json',
    'studio-project.json', 'studio-project.json.bak', 'library-catalog.json',
    'game-context-memory.json', 'clip-preferences.json',
    'taste-state.json', 'taste-training-report.json', 'taste-evaluation-report.json',
    'editorial-metadata-preference-consent.json',
    'editorial-metadata-preferences.json', 'editorial-reroll-preference.json',
    'bug-report-consent.json', 'generation-output-location.json',
    'hidden-moment-decisions.json', 'studio-candidate-decisions.json',
    'youtube-connection-permission.json', 'youtube-publish-drafts.json',
    'youtube-publish-history.json', 'youtube-publish-preferences.json',
    'pending-local-data-reset.json'
)
$forbiddenMediaExtensions = @('.avi', '.m4a', '.mkv', '.mov', '.mp3', '.mp4', '.wav', '.webm')
$textExtensions = @(
    '.cs', '.css', '.html', '.js', '.json', '.md', '.mjs', '.props',
    '.ps1', '.psd1', '.py', '.svg', '.targets', '.ts', '.tsx', '.txt',
    '.xaml', '.xml', '.yaml', '.yml'
)

function ConvertTo-PortablePath([string]$Value) {
    return $Value.Replace('\', '/').TrimStart('/')
}

function Get-Sha256([string]$Value) {
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $Value).Hash
}

function Test-Sha256Equal([string]$Left, [string]$Right) {
    return -not [string]::IsNullOrWhiteSpace($Left) -and
        $Left.Equals($Right, [StringComparison]::OrdinalIgnoreCase)
}

function Resolve-SafeChild([string]$Root, [string]$RelativePath) {
    $portable = ConvertTo-PortablePath $RelativePath
    if ([string]::IsNullOrWhiteSpace($portable) -or
        [IO.Path]::IsPathFullyQualified($RelativePath) -or
        $portable.Split('/') -contains '..') {
        throw "A release manifest contains an unsafe relative path: $RelativePath"
    }
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $full = [IO.Path]::GetFullPath((Join-Path $rootFull $portable))
    if (-not $full.StartsWith(
        $rootFull + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "A release manifest path escaped its root: $RelativePath"
    }
    return $full
}

function Assert-NoReparsePoint([string]$FullPath) {
    $current = [IO.Path]::GetFullPath($FullPath)
    while (-not [string]::IsNullOrWhiteSpace($current)) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Release inspection cannot traverse a reparse point: $current"
            }
        }
        $parent = [IO.Directory]::GetParent($current)
        if ($null -eq $parent) { break }
        $current = $parent.FullName
    }
}

function Test-PayloadFile([string]$FullPath, [string]$Container, [string]$RelativePath) {
    $portable = ConvertTo-PortablePath $RelativePath
    $name = [IO.Path]::GetFileName($portable)
    $extension = [IO.Path]::GetExtension($name).ToLowerInvariant()
    if ($name -in $forbiddenFileNames -or
        $name -match '(?i)\.taste-(example|model)\.json$' -or
        $portable -match '(?i)(^|/)(Cache/(GameKnowledge|StudioPreview)|Diagnostics/(Outbox|VisualSemanticFailures))(/|$)' -or
        $extension -in $forbiddenMediaExtensions) {
        $violations.Add("$Container::$portable (mutable or captured user payload)")
        return
    }
    if ($extension -notin $textExtensions -or
        (Get-Item -LiteralPath $FullPath).Length -gt 16MB) { return }
    $text = [IO.File]::ReadAllText($FullPath)
    if ($extension -eq '.json' -and $text -match 'foundry-taste-(local-state|example|checkpoint|training)-1') {
        $violations.Add("$Container::$portable (personal learning data)")
        return
    }
    $userRoots = @(
        [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile),
        $env:USERPROFILE
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
    foreach ($userRoot in $userRoots) {
        if ($text.Contains($userRoot, [StringComparison]::OrdinalIgnoreCase)) {
            $violations.Add("$Container::$portable (machine-specific absolute path)")
            break
        }
    }
    if ($text -match '(?i)(-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----|github_pat_[0-9A-Za-z_]{20,}|ghp_[0-9A-Za-z]{30,}|client_secret\s*[:=]\s*["''][^"'']+)') {
        $violations.Add("$Container::$portable (credential-shaped text)")
    }
}

function Assert-ManifestFiles(
    [string]$Root,
    [object[]]$Records,
    [string[]]$ExcludedRelativePaths) {
    $recordMap = [Collections.Generic.Dictionary[string, object]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($record in $Records) {
        $relative = ConvertTo-PortablePath ([string]$record.path)
        [void](Resolve-SafeChild $Root $relative)
        if (-not $recordMap.TryAdd($relative, $record)) {
            throw "A release manifest contains a duplicate path: $relative"
        }
    }
    $excluded = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($relative in $ExcludedRelativePaths) { [void]$excluded.Add($relative) }
    $actual = @(Get-ChildItem -LiteralPath $Root -Recurse -File -Force |
        ForEach-Object { ConvertTo-PortablePath ([IO.Path]::GetRelativePath($Root, $_.FullName)) } |
        Where-Object { -not $excluded.Contains($_) })
    if ($actual.Count -ne $recordMap.Count -or
        @($actual | Where-Object { -not $recordMap.ContainsKey($_) }).Count -ne 0) {
        throw "Release payload files differ from their manifest under $Root."
    }
    foreach ($relative in $actual) {
        $record = $recordMap[$relative]
        $file = Resolve-SafeChild $Root $relative
        $length = if ($null -ne $record.size) { [long]$record.size } else { [long]$record.byteLength }
        if ((Get-Item -LiteralPath $file).Length -ne $length -or
            -not (Test-Sha256Equal (Get-Sha256 $file) ([string]$record.sha256))) {
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
    if ($manifest.schemaVersion -ne 'replayfoundry-release-manifest-1.1' -or
        $manifest.releaseChannel -notin @('Production', 'Development') -or
        $manifest.dataChannel -ne $manifest.releaseChannel) {
        throw 'Publish boundary found an unsupported or inconsistent release manifest.'
    }
    Assert-ManifestFiles $Root @($manifest.files) @('release-manifest.json')
    if ($manifest.releaseChannel -eq 'Production') {
        if ($manifest.sourceTreeDirty -or -not $manifest.signing.required -or
            $manifest.signing.mode -ne 'ArtifactSigning') {
            throw 'Production publish manifest is not clean and signing-required.'
        }
        foreach ($signature in @($manifest.signing.files)) {
            $file = Resolve-SafeChild $Root ([string]$signature.path)
            $actual = Get-AuthenticodeSignature -LiteralPath $file
            if ($signature.status -ne 'Valid' -or $actual.Status.ToString() -ne 'Valid' -or
                $null -eq $actual.SignerCertificate -or
                $actual.SignerCertificate.Thumbprint -ne [string]$signature.signerThumbprint) {
                throw "Production Authenticode verification failed: $($signature.path)"
            }
        }
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
    $expectedArchives = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($pack in @($index.packs)) {
        $packageId = [string]$pack.packageId
        if ($packageId -notmatch '^[a-z0-9][a-z0-9.-]{0,127}$') {
            throw "Runtime-pack index contains an invalid package ID: $packageId"
        }
        $relative = ConvertTo-PortablePath ([string]$pack.archive)
        if ($relative -cne "archives/$packageId.zip") {
            throw "Runtime-pack archive path is not canonical: $packageId"
        }
        $archivePath = Resolve-SafeChild $Root $relative
        if (-not $expectedArchives.Add($archivePath) -or
            -not (Test-Path -LiteralPath $archivePath -PathType Leaf) -or
            (Get-Item -LiteralPath $archivePath).Length -ne [long]$pack.byteLength -or
            -not (Test-Sha256Equal (Get-Sha256 $archivePath) ([string]$pack.sha256))) {
            throw "Runtime-pack archive failed build-index verification: $packageId"
        }
        $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
        try {
            $entries = @($archive.Entries | Where-Object {
                -not [string]::IsNullOrEmpty($_.Name)
            })
            $manifestEntry = @($entries | Where-Object {
                [StringComparer]::OrdinalIgnoreCase.Equals(
                    (ConvertTo-PortablePath $_.FullName),
                    'runtime-pack-manifest.json')
            })
            if ($manifestEntry.Count -ne 1) {
                throw "Runtime archive has no unique manifest: $packageId"
            }
            $reader = [IO.StreamReader]::new($manifestEntry[0].Open(), [Text.UTF8Encoding]::new($false))
            try { $packManifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
            if ($packManifest.identity.packageId -cne $packageId -or
                -not (Test-Sha256Equal ([string]$packManifest.manifestHash) ([string]$pack.manifestHash))) {
                throw "Runtime archive identity differs from its build index: $packageId"
            }
            $recordMap = [Collections.Generic.Dictionary[string, object]]::new(
                [StringComparer]::OrdinalIgnoreCase)
            foreach ($record in @($packManifest.files)) {
                $relative = ConvertTo-PortablePath ([string]$record.relativePath)
                if ([string]::IsNullOrWhiteSpace($relative) -or
                    [IO.Path]::IsPathFullyQualified($relative) -or
                    $relative.Split('/') -contains '..' -or
                    -not $recordMap.TryAdd($relative, $record)) {
                    throw "Runtime manifest contains an unsafe or duplicate path: $packageId::$relative"
                }
            }
            $payloadEntries = @($entries | Where-Object {
                -not [StringComparer]::OrdinalIgnoreCase.Equals(
                    (ConvertTo-PortablePath $_.FullName),
                    'runtime-pack-manifest.json')
            })
            $entryMap = [Collections.Generic.Dictionary[string, object]]::new(
                [StringComparer]::OrdinalIgnoreCase)
            foreach ($entry in $payloadEntries) {
                $relative = ConvertTo-PortablePath $entry.FullName
                if (-not $entryMap.TryAdd($relative, $entry) -or
                    -not $recordMap.ContainsKey($relative)) {
                    throw "Runtime archive differs from its internal manifest: $packageId::$relative"
                }
            }
            if ($recordMap.Count -ne $entryMap.Count) {
                throw "Runtime archive differs from its internal manifest: $packageId"
            }
            foreach ($pair in $recordMap.GetEnumerator()) {
                $relative = $pair.Key
                $record = $pair.Value
                $entry = $entryMap[$relative]
                if ($entry.Length -ne [long]$record.byteLength) {
                    throw "Runtime archive file differs from its manifest: $packageId::$relative"
                }
                $stream = $entry.Open()
                try {
                    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()
                } finally { $stream.Dispose() }
                if (-not (Test-Sha256Equal $hash ([string]$record.sha256))) {
                    throw "Runtime archive content hash differs from its manifest: $packageId::$relative"
                }
            }
        } finally { $archive.Dispose() }
    }
    $actualArchives = @(Get-ChildItem -LiteralPath (Join-Path $Root 'archives') -File -Filter '*.zip' |
        ForEach-Object { $_.FullName })
    if ($actualArchives.Count -ne $expectedArchives.Count -or
        @($actualArchives | Where-Object { -not $expectedArchives.Contains($_) }).Count -ne 0) {
        throw 'Runtime-pack archive directory differs from its build index.'
    }
}

function Assert-InstallerManifest([string]$Root) {
    $manifestPath = Join-Path $Root 'installer-release-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Installer boundary requires installer-release-manifest.json under $Root."
    }
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 'replayfoundry-installer-release-manifest-1.1') {
        throw 'Installer boundary found an unsupported release manifest.'
    }
    $installer = Resolve-SafeChild $Root (Join-Path 'installer' ([string]$manifest.installer.fileName))
    if ((Get-Item -LiteralPath $installer).Length -ne [long]$manifest.installer.byteLength -or
        -not (Test-Sha256Equal (Get-Sha256 $installer) ([string]$manifest.installer.sha256))) {
        throw 'Installer executable differs from its release manifest.'
    }
    $appManifest = Resolve-SafeChild $Root 'app/release-manifest.json'
    if (-not (Test-Sha256Equal (Get-Sha256 $appManifest) ([string]$manifest.appReleaseManifest.sha256))) {
        throw 'Installer manifest does not bind the application release manifest.'
    }
    Assert-PublishManifest (Join-Path $Root 'app')
    $branding = Resolve-SafeChild $Root 'branding/installer-branding-manifest.json'
    if (-not (Test-Sha256Equal (Get-Sha256 $branding) ([string]$manifest.installerBrandingManifestSha256))) {
        throw 'Installer manifest does not bind the branding manifest.'
    }
    if ([string]::IsNullOrWhiteSpace($RuntimePackBuildRoot)) {
        throw 'Installer boundary requires -RuntimePackBuildRoot.'
    }
    $runtimeRoot = [IO.Path]::GetFullPath($RuntimePackBuildRoot)
    if (-not (Test-Sha256Equal (Get-Sha256 (Join-Path $runtimeRoot 'runtime-pack-build-index.json')) ([string]$manifest.runtimePackBuildIndexSha256))) {
        throw 'Installer manifest does not bind the runtime-pack build index.'
    }
    Assert-RuntimePackIndex $runtimeRoot
    if ($null -ne $manifest.advancedCatalogSha256) {
        if ([string]::IsNullOrWhiteSpace($AdvancedCatalogPath) -or
            -not (Test-Sha256Equal (Get-Sha256 ([IO.Path]::GetFullPath($AdvancedCatalogPath))) ([string]$manifest.advancedCatalogSha256))) {
            throw 'Installer manifest does not bind the Advanced AI catalog.'
        }
    }
    if ($manifest.releaseChannel -eq 'Production') {
        $signature = Get-AuthenticodeSignature -LiteralPath $installer
        if ($manifest.sourceTreeDirty -or -not $manifest.signing.required -or
            $manifest.signing.mode -ne 'ArtifactSigning' -or
            $manifest.signing.status -ne 'Valid' -or
            $signature.Status.ToString() -ne 'Valid') {
            throw 'Production installer is not a clean, valid signed payload.'
        }
    }
}

if ($Profile -eq 'Repository') {
    if ($null -ne $Path -and $Path.Count -gt 0) {
        throw 'Repository profile resolves its path from the script location.'
    }
    $gitProbe = [string](& git -C $repositoryRoot rev-parse --is-inside-work-tree 2>$null)
    if ($LASTEXITCODE -eq 0 -and $gitProbe.Trim() -eq 'true') {
        $paths = @(& git -C $repositoryRoot ls-files --cached --others --exclude-standard)
        if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate repository files.' }
    } elseif (Test-Path -LiteralPath (Join-Path $repositoryRoot '.replayfoundry-public-source') -PathType Leaf) {
        $paths = @(Get-ChildItem -LiteralPath $repositoryRoot -Recurse -File -Force |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
            ForEach-Object { [IO.Path]::GetRelativePath($repositoryRoot, $_.FullName) })
    } else {
        throw 'Repository inspection requires a Git worktree or sealed public source.'
    }
    foreach ($relative in $paths) {
        $full = Join-Path $repositoryRoot $relative
        if (Test-Path -LiteralPath $full -PathType Leaf) {
            Test-PayloadFile $full 'repository' $relative
        }
    }
} else {
    if ($null -eq $Path -or $Path.Count -eq 0) {
        throw "$Profile inspection requires at least one -Path."
    }
    foreach ($candidate in $Path) {
        $full = [IO.Path]::GetFullPath($candidate)
        Assert-NoReparsePoint $full
        if (Test-Path -LiteralPath $full -PathType Leaf) {
            Test-PayloadFile $full $full ([IO.Path]::GetFileName($full))
        } elseif (Test-Path -LiteralPath $full -PathType Container) {
            foreach ($file in Get-ChildItem -LiteralPath $full -Recurse -File -Force) {
                Test-PayloadFile $file.FullName $full ([IO.Path]::GetRelativePath($full, $file.FullName))
            }
        } else {
            throw "Release inspection path was not found: $full"
        }
    }
}

if ($Profile -eq 'Publish') {
    if ($Path.Count -ne 1) { throw 'Publish profile requires one directory.' }
    Assert-PublishManifest ([IO.Path]::GetFullPath($Path[0]))
} elseif ($Profile -eq 'RuntimePacks') {
    if ($Path.Count -ne 1) { throw 'RuntimePacks profile requires one directory.' }
    Assert-RuntimePackIndex ([IO.Path]::GetFullPath($Path[0]))
} elseif ($Profile -eq 'Installer') {
    if ($Path.Count -ne 1) { throw 'Installer profile requires one directory.' }
    Assert-InstallerManifest ([IO.Path]::GetFullPath($Path[0]))
}

if ($violations.Count -gt 0) {
    throw "Release data boundary rejected unsafe payloads:$([Environment]::NewLine)$($violations -join [Environment]::NewLine)"
}

Write-Output "Release data boundary passed for $Profile."
