[CmdletBinding()]
param([string]$RepositoryRoot)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Join-Path $PSScriptRoot '..'
}
$root = [IO.Path]::GetFullPath($RepositoryRoot)

function ConvertTo-RepositoryPath([string]$Path) {
    return $Path.Replace('\', '/').TrimStart('/')
}

function Get-RepositoryPaths {
    $gitProbe = [string](& git -C $root rev-parse --is-inside-work-tree 2>$null)
    if ($LASTEXITCODE -eq 0 -and $gitProbe.Trim() -eq 'true') {
        $tracked = @(& git -C $root ls-files --cached)
        if ($LASTEXITCODE -ne 0) { throw 'Could not inspect tracked source files.' }
        $untracked = @(& git -C $root ls-files --others --exclude-standard)
        if ($LASTEXITCODE -ne 0) { throw 'Could not inspect untracked source files.' }
        return [pscustomobject]@{
            Tracked = @($tracked)
            Untracked = @($untracked)
            Paths = @($tracked + $untracked | Sort-Object -Unique)
        }
    }
    if (-not (Test-Path -LiteralPath `
        (Join-Path $root '.replayfoundry-public-source') -PathType Leaf)) {
        throw 'Payload inspection requires a Git worktree or sealed public source.'
    }
    $paths = @(Get-ChildItem -LiteralPath $root -Recurse -File -Force |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        ForEach-Object {
            ConvertTo-RepositoryPath ([IO.Path]::GetRelativePath($root, $_.FullName))
        })
    return [pscustomobject]@{
        Tracked = @($paths)
        Untracked = @()
        Paths = @($paths)
    }
}

$inventory = Get-RepositoryPaths
$forbiddenDirectoryPattern =
    '(^|/)(bin|obj|artifacts|outputs|tmp|runtime-packs|model-packs|' +
    'tool-cache|transcripts|site-packages|__pycache__)(/|$)'
$forbiddenExtensions = @(
    '.exe', '.dll', '.pdb', '.zip', '.7z', '.nupkg',
    '.onnx', '.gguf', '.bin', '.safetensors', '.pt', '.pth', '.ckpt',
    '.pfx', '.p12', '.pvk', '.key', '.pem', '.pyc', '.pyo',
    '.mp4', '.mkv', '.mov', '.avi', '.webm', '.wav', '.mp3', '.m4a'
)
$allowedBinaryExtensions = @('.png', '.jpg', '.jpeg', '.gif', '.ico')
$violations = [Collections.Generic.List[string]]::new()
foreach ($relative in $inventory.Paths) {
    $portable = ConvertTo-RepositoryPath $relative
    $full = Join-Path $root $portable
    if ($portable -match $forbiddenDirectoryPattern) {
        $violations.Add("$portable (generated or packaged directory)")
        continue
    }
    $extension = [IO.Path]::GetExtension($portable).ToLowerInvariant()
    if ($extension -in $forbiddenExtensions) {
        $violations.Add("$portable (packaged payload extension)")
        continue
    }
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }
    $item = Get-Item -LiteralPath $full -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        $violations.Add("$portable (reparse point)")
        continue
    }
    if ($item.Length -gt 25MB -and $extension -notin $allowedBinaryExtensions) {
        $violations.Add("$portable (unexpected oversized source file)")
        continue
    }
    if ($extension -in @('.ps1', '.psd1', '.cs', '.py', '.json', '.yml', '.yaml', '.md', '.txt', '.xml', '.xaml', '.props', '.targets')) {
        $text = Get-Content -Raw -LiteralPath $full
        if ($text -match '(?i)(-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----|github_pat_[0-9A-Za-z_]{20,}|ghp_[0-9A-Za-z]{30,}|client_secret\s*[:=]\s*["''][^"'']+)') {
            $violations.Add("$portable (credential-shaped text)")
        }
    }
}

if ($violations.Count -ne 0) {
    throw "Repository payload guard rejected production source:$([Environment]::NewLine)$($violations -join [Environment]::NewLine)"
}

Write-Output (
    'Repository payload guard passed: ' +
    "$($inventory.Tracked.Count) tracked and " +
    "$($inventory.Untracked.Count) untracked paths inspected.")
