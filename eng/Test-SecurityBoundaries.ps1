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

$networkMatches = Get-ChildItem -LiteralPath (Join-Path $root 'src\ReplayFoundry.Desktop') `
    -Recurse -Filter '*.cs' |
    Select-String -Pattern '\bHttpClient\b|\bHttpRequestMessage\b' |
    ForEach-Object { $_.Path.Substring($root.Length + 1).Replace('\', '/') } |
    Sort-Object -Unique
$approvedNetworkFiles = @(
    'src/ReplayFoundry.Desktop/Platform/Diagnostics/HttpsUserReportTransport.cs',
    'src/ReplayFoundry.Desktop/Platform/GameKnowledge/WikimediaGameKnowledgeProvider.cs',
    'src/ReplayFoundry.Desktop/Platform/GameKnowledge/WikimediaGameKnowledgeProvider.Retrieval.cs',
    'src/ReplayFoundry.Desktop/Platform/YouTube/GoogleYouTubeAuthorizationService.cs',
    'src/ReplayFoundry.Desktop/Platform/YouTube/YouTubeDataApiClient.cs',
    'src/ReplayFoundry.Desktop/Platform/YouTube/YouTubePublishingFactory.cs'
    'src/ReplayFoundry.Desktop/Platform/Intelligence/MiniLmModelArtifacts.cs'
    'src/ReplayFoundry.Desktop/Platform/Transcription/OnnxCorrectedCaptionAlignmentService.cs'
    'src/ReplayFoundry.Desktop/Platform/YouTube/YouTubeAnalyticsService.cs'
)
$unexpected = @($networkMatches | Where-Object { $_ -notin $approvedNetworkFiles })
if ($unexpected.Count -gt 0) {
    Fail "unreviewed desktop network client(s): $($unexpected -join ', ')"
}

# First-use model acquisition sends only fixed artifact URLs. Local recordings,
# transcripts and captions remain outside these HTTP requests. Require bounded,
# hash-pinned downloads and atomic promotion for both reviewed artifact clients.
$modelClients = @(
    @{
        Path = 'src/ReplayFoundry.Desktop/Platform/Intelligence/MiniLmModelArtifacts.cs'
        Url = 'https://huggingface.co/Xenova/all-MiniLM-L6-v2/resolve/'
        Bound = 'received > bytes'
    },
    @{
        Path = 'src/ReplayFoundry.Desktop/Platform/Transcription/OnnxCorrectedCaptionAlignmentService.cs'
        Url = 'https://huggingface.co/Xenova/wav2vec2-base-960h/resolve/'
        Bound = 'received > ModelBytes'
    }
)
foreach ($client in $modelClients) {
    foreach ($required in @(
        [regex]::Escape($client.Url),
        [regex]::Escape($client.Bound),
        'SHA256\.HashDataAsync',
        'HttpCompletionOption\.ResponseHeadersRead',
        'CancelAfter\(TimeSpan\.FromMinutes\(5\)\)',
        'File\.Move\(temporary,.*overwrite:\s*false')) {
        Require-Text $client.Path $required "$($client.Path) lost a reviewed download boundary"
    }
    $clientText = Get-Content -Raw -LiteralPath (Join-Path $root $client.Path)
    if ($clientText -match 'PostAsync|PutAsync|MultipartFormDataContent|StringContent|ByteArrayContent') {
        Fail "$($client.Path) gained an outbound content transport"
    }
}

$analytics = 'src/ReplayFoundry.Desktop/Platform/YouTube/YouTubeAnalyticsService.cs'
foreach ($required in @(
    'RequirePermission\(\);',
    'RequireScope\(credential\);',
    'AnalyticsReadOnlyScope',
    'new HttpRequestMessage\(HttpMethod\.Get, "https://youtubeanalytics\.googleapis\.com/v2/reports\?',
    'Take\(100\)',
    'CancelAfter\(TimeSpan\.FromSeconds\(45\)\)',
    'buffer\.Length \+ count > maximumBytes')) {
    Require-Text $analytics $required 'read-only analytics lost an authorization or response boundary'
}

$pythonNetwork = Get-ChildItem -LiteralPath `
    (Join-Path $root 'src/ReplayFoundry.VisualSemanticHost') -Recurse -Filter '*.py' |
    Select-String -Pattern '^\s*(?:import|from)\s+(?:requests|httpx|aiohttp|urllib\.request|socket|websockets)\b'
if ($pythonNetwork) {
    Fail 'the local Qwen host gained an unreviewed network dependency'
}

Require-Text `
    'src/ReplayFoundry.Desktop/Platform/Diagnostics/HttpsUserReportTransport.cs' `
    'SanitizeOutboundDraft' `
    'bug reports are not re-sanitized at the final HTTPS boundary'
Require-Text `
    'src/ReplayFoundry.Desktop/Features/Publish/YouTube/YouTubePublishContracts.cs' `
    'ExternalTextSecurity' `
    'YouTube public fields bypass the external-text sanitizer'
Require-Text `
    'src/ReplayFoundry.Desktop/Platform/GameKnowledge/WikimediaGameKnowledgeProvider.cs' `
    'ExternalTextSecurity\.SingleLine' `
    'Wikimedia lookup terms bypass the external-text sanitizer'
Require-Text `
    'src/ReplayFoundry.Desktop/App.xaml.cs' `
    'LocalCrashReportFallback' `
    'startup crashes have no pre-composition local route'

$renderers = @(
    'src/ReplayFoundry.VisualSemanticHost/replayfoundry_visual_semantic/generation.py',
    'src/ReplayFoundry.VisualSemanticHost/replayfoundry_visual_semantic/editorial/inference.py',
    'src/ReplayFoundry.VisualSemanticHost/replayfoundry_visual_semantic/editorial/grounded_metadata_generation.py'
)
foreach ($renderer in $renderers) {
    $text = Get-Content -Raw -LiteralPath (Join-Path $root $renderer)
    if ($text -match 'apply_chat_template\(' -and
        $text -notmatch '_secure_model_messages') {
        Fail "$renderer renders an AI prompt without the centralized untrusted-data boundary"
    }
}

Require-Text `
    'src/ReplayFoundry.Desktop/Platform/Processes/WindowsProcessRunner.cs' `
    '!request\.InheritParentEnvironment[\s\S]*Environment\.Clear\(\)' `
    'child-process replacement mode does not clear inherited environment variables'

$isolatedQwenLaunchers = @(
    'src/ReplayFoundry.Desktop/Platform/VisualSemantic/Qwen3VlBatchProcessExecutor.cs'
    'src/ReplayFoundry.Desktop/Platform/VisualSemantic/Qwen3VlInitializationCoordinator.cs'
    'src/ReplayFoundry.Desktop/Platform/VisualSemantic/Editorial/Qwen3VlGroundedMetadataGeneration.cs'
    'src/ReplayFoundry.Desktop/Platform/VisualSemantic/Editorial/Qwen3VlQualifiedEditorialProvider.cs'
)
$isolatedQwenLaunchers += @(Get-ChildItem -LiteralPath (Join-Path $root 'tools') `
    -Recurse -File -Filter 'Qwen3Vl*.cs' | Where-Object {
        (Get-Content -Raw -LiteralPath $_.FullName) -match 'WindowsProcessRunner'
    } | ForEach-Object {
        $_.FullName.Substring($root.Length + 1).Replace('\', '/')
    })
$isolatedQwenLaunchers = @($isolatedQwenLaunchers | Sort-Object -Unique)
foreach ($launcher in $isolatedQwenLaunchers) {
    Require-Text $launcher 'inheritParentEnvironment:\s*false' `
        "$launcher allows unrelated parent secrets into the local AI process"
}

Write-Host 'Security boundary guard passed.'
