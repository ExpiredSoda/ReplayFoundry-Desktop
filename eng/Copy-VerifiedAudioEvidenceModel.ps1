param([Parameter(Mandatory=$true)][string]$Source, [Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference = 'Stop'
$sourceRoot = [IO.Path]::GetFullPath($Source)
$destinationRoot = [IO.Path]::GetFullPath($Destination)
$lock = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot '..\src\ReplayFoundry.VisualSemanticHost\audio-evidence-model-lock.json') | ConvertFrom-Json
if (Test-Path -LiteralPath $destinationRoot) { throw 'The audio model destination must be new.' }
New-Item -ItemType Directory -Path $destinationRoot | Out-Null
foreach ($file in $lock.files.PSObject.Properties) {
    $path = Join-Path $sourceRoot $file.Name
    $bytes = [IO.File]::ReadAllBytes($path)
    if ($file.Value.Length -eq 64) {
        $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
    } else {
        $header = [Text.Encoding]::ASCII.GetBytes(('blob ' + $bytes.Length + [char]0))
        $algorithm = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA1)
        try { $algorithm.AppendData($header); $algorithm.AppendData($bytes); $actual = [Convert]::ToHexString($algorithm.GetHashAndReset()) }
        finally { $algorithm.Dispose() }
    }
    if ($actual -ine $file.Value) { throw "Audio evidence model identity mismatch: $($file.Name)" }
    Copy-Item -LiteralPath $path -Destination (Join-Path $destinationRoot $file.Name)
}
$notice = Join-Path $sourceRoot 'LICENSE.txt'
if ((Get-Content -Raw -LiteralPath $notice) -notmatch 'Apache License' -or (Get-Content -Raw -LiteralPath $notice) -notmatch 'Version 2.0') {
    throw 'The audio model must retain its Apache-2.0 license text.'
}
Copy-Item -LiteralPath $notice -Destination (Join-Path $destinationRoot 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\src\ReplayFoundry.VisualSemanticHost\audio-evidence-model-lock.json') -Destination (Join-Path $destinationRoot 'model-provenance.json')
