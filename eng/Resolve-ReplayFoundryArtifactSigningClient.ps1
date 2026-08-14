Set-StrictMode -Version Latest

function Test-ReplayFoundryPeX64([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $reader = [IO.BinaryReader]::new($stream)
        if ($reader.ReadUInt16() -ne 0x5A4D) { return $false }
        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        if ($peOffset -lt 0 -or $peOffset -gt ($stream.Length - 6)) { return $false }
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) { return $false }
        return $reader.ReadUInt16() -eq 0x8664
    }
    finally {
        $stream.Dispose()
    }
}

function Resolve-ReplayFoundryArtifactSigningDlib([string]$ExplicitPath) {
    $candidatePaths = [Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        $candidatePaths.Add([IO.Path]::GetFullPath($ExplicitPath))
    }
    else {
        $uninstallRoots = @(
            'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
            'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
            'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
        )
        $installRoots = @($uninstallRoots |
            ForEach-Object { Get-ItemProperty $_ -ErrorAction SilentlyContinue } |
            Where-Object {
                $null -ne $_.PSObject.Properties['DisplayName'] -and
                $_.DisplayName -match '^Artifact\s*Signing Client Tools$'
            } |
            ForEach-Object { $_.InstallLocation } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

        if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
            $installRoots += Join-Path $env:LOCALAPPDATA 'Microsoft\MicrosoftArtifactSigningClientTools'
        }
        if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
            $installRoots += Join-Path $env:ProgramFiles 'Microsoft Artifact Signing Client Tools'
        }
        if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
            $installRoots += Join-Path ${env:ProgramFiles(x86)} 'Microsoft Artifact Signing Client Tools'
        }

        foreach ($root in @($installRoots | Select-Object -Unique)) {
            $candidatePaths.Add((Join-Path $root 'Azure.CodeSigning.Dlib.dll'))
            $candidatePaths.Add((Join-Path $root 'x64\Azure.CodeSigning.Dlib.dll'))
            $candidatePaths.Add((Join-Path $root 'bin\x64\Azure.CodeSigning.Dlib.dll'))
        }
    }

    foreach ($candidatePath in $candidatePaths) {
        if (-not (Test-Path -LiteralPath $candidatePath -PathType Leaf)) { continue }
        $candidate = Get-Item -LiteralPath $candidatePath
        if ($candidate.Name -ne 'Azure.CodeSigning.Dlib.dll' -or
            -not (Test-ReplayFoundryPeX64 $candidate.FullName)) {
            continue
        }

        $directory = $candidate.DirectoryName
        $requiredAdjacentFiles = @(
            'Azure.CodeSigning.Dlib.deps.json',
            'Azure.CodeSigning.dll',
            'Azure.Core.dll',
            'Azure.Identity.dll'
        )
        $missing = @($requiredAdjacentFiles | Where-Object {
            -not (Test-Path -LiteralPath (Join-Path $directory $_) -PathType Leaf)
        })
        if ($missing.Count -ne 0) {
            throw "Artifact Signing dlib is incomplete; missing adjacent runtime files: $($missing -join ', ')."
        }
        return $candidate
    }

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        throw "A complete x64 Artifact Signing dlib payload was not found at '$([IO.Path]::GetFullPath($ExplicitPath))'."
    }
    throw 'Microsoft Artifact Signing Client Tools x64 dlib is unavailable. Install Microsoft.Azure.ArtifactSigningClientTools or pass -DlibPath to a complete bin/x64 payload.'
}
