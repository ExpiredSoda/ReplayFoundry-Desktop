[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Fail([string] $Message) {
    throw "Security boundary guard failed: $Message"
}

function Require-Text(
    [string] $Path,
    [string] $Pattern,
    [string] $Description) {
    $text = Get-Content -Raw -LiteralPath (Join-Path $root $Path)
    if ($text -notmatch $Pattern) { Fail $Description }
}

$networkMatches = Get-ChildItem -LiteralPath (Join-Path $root 'ReplayFoundry.Desktop') `
    -Recurse -Filter '*.cs' |
    Select-String -Pattern '\bHttpClient\b|\bHttpRequestMessage\b' |
    ForEach-Object { $_.Path.Substring($root.Length + 1).Replace('\', '/') } |
    Sort-Object -Unique
$approvedNetworkFiles = @(
    'ReplayFoundry.Desktop/Platform/Diagnostics/HttpsUserReportTransport.cs',
    'ReplayFoundry.Desktop/Platform/GameKnowledge/WikimediaGameKnowledgeProvider.cs',
    'ReplayFoundry.Desktop/Platform/YouTube/GoogleYouTubeAuthorizationService.cs',
    'ReplayFoundry.Desktop/Platform/YouTube/YouTubeDataApiClient.cs',
    'ReplayFoundry.Desktop/Platform/YouTube/YouTubePublishingFactory.cs'
)
$unexpected = @($networkMatches | Where-Object { $_ -notin $approvedNetworkFiles })
if ($unexpected.Count -gt 0) {
    Fail "unreviewed desktop network client(s): $($unexpected -join ', ')"
}

$pythonNetwork = Get-ChildItem -LiteralPath `
    (Join-Path $root 'eng/visual-semantic-host') -Recurse -Filter '*.py' |
    Select-String -Pattern '^\s*(?:import|from)\s+(?:requests|httpx|aiohttp|urllib\.request|socket|websockets)\b'
if ($pythonNetwork) {
    Fail 'the local Qwen host gained an unreviewed network dependency'
}

Require-Text `
    'ReplayFoundry.Desktop/Platform/Diagnostics/HttpsUserReportTransport.cs' `
    'SanitizeOutboundDraft' `
    'bug reports are not re-sanitized at the final HTTPS boundary'
Require-Text `
    'ReplayFoundry.Desktop/Features/Publish/YouTube/YouTubePublishContracts.cs' `
    'ExternalTextSecurity' `
    'YouTube public fields bypass the external-text sanitizer'
Require-Text `
    'ReplayFoundry.Desktop/Platform/GameKnowledge/WikimediaGameKnowledgeProvider.cs' `
    'ExternalTextSecurity\.SingleLine' `
    'Wikimedia lookup terms bypass the external-text sanitizer'
Require-Text `
    'ReplayFoundry.Desktop/App.xaml.cs' `
    'LocalCrashReportFallback' `
    'startup crashes have no pre-composition local route'

$renderers = @(
    'eng/visual-semantic-host/replayfoundry_visual_semantic/generation.py',
    'eng/visual-semantic-host/replayfoundry_visual_semantic/editorial/inference.py',
    'eng/visual-semantic-host/replayfoundry_visual_semantic/editorial/grounded_metadata_generation.py'
)
foreach ($renderer in $renderers) {
    $text = Get-Content -Raw -LiteralPath (Join-Path $root $renderer)
    if ($text -match 'apply_chat_template\(' -and
        $text -notmatch '_secure_model_messages') {
        Fail "$renderer renders an AI prompt without the centralized untrusted-data boundary"
    }
}

Write-Host 'Security boundary guard passed.'
