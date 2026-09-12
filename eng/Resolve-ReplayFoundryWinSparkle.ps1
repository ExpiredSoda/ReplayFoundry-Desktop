[CmdletBinding()]
param([string]$CacheDirectory = (Join-Path $env:LOCALAPPDATA 'ReplayFoundryBuildTools\WinSparkle\0.9.4'))
$ErrorActionPreference = 'Stop'
$archiveHash = '6037df37fc263bd1650a1c4949681a9d40ffe991d01f35892a406cb5d103c976'
$url = 'https://github.com/vslavik/winsparkle/releases/download/v0.9.4/WinSparkle-0.9.4.zip'
$cache = [IO.Path]::GetFullPath($CacheDirectory)
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$archive = Join-Path $cache 'WinSparkle-0.9.4.zip'
if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest -Uri $url -OutFile $archive }
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $archiveHash) {
    throw 'The WinSparkle SDK archive failed its pinned SHA-256 verification.'
}
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    foreach ($relative in @('x64/Release/WinSparkle.dll', 'bin/winsparkle-tool.exe', 'COPYING', 'COPYING.expat')) {
        $entry = $zip.GetEntry('WinSparkle-0.9.4/' + $relative)
        if ($null -eq $entry) { throw "WinSparkle SDK is missing $relative" }
        $target = Join-Path $cache $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        # Copy only the required, hash-verified SDK entries. No archive paths are trusted.
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
    }
} finally { $zip.Dispose() }
Write-Output $cache
