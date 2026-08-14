$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

function Test-RawAuditFileName {
    param([Parameter(Mandatory)][string]$Path)

    return $Path -match (
        '(^|/)[^/]*' +
        '(raw[-_. ]*(output[-_. ]*)?audit|' +
        'provider[-_. ]*raw[-_. ]*output)' +
        '[^/]*\.json$'
    )
}

function Test-RawAuditSchemaText {
    param([Parameter(Mandatory)][string]$Text)

    return $Text -match (
        '"schemaVersion"\s*:\s*' +
        '"visual-semantic-raw-output-audit-1\.(0|1|2)"'
    )
}

function Test-VisualSemanticGenerationPayloadFileName {
    param([Parameter(Mandatory)][string]$Path)

    return (
        $Path -match (
            '(^|/)[^/]*' +
            '(generation[-_. ]*(audit|manifest|result|trace)|' +
            'qwen3[-_. ]*vl[-_. ][^/]*smoke)' +
            '[^/]*\.json$'
        )
    )
}

function Test-VisualSemanticGenerationPayloadSchemaText {
    param([Parameter(Mandatory)][string]$Text)

    return $Text -match (
        '"schemaVersion"\s*:\s*' +
        '"visual-semantic-generation-manifest-1\.0"'
    )
}

function Test-VisualSemanticDiagnosticPayloadFileName {
    param([Parameter(Mandatory)][string]$Path)

    return (
        $Path -match (
            '(^|/)(sampling[-_. ]*audit|' +
            'input[-_. ]*policy[-_. ]*validity[-_. ]*decision)' +
            '([^/]*)\.(json|md)$'
        ) -or
        $Path -match (
            '(^|/)[^/]*host[-_. ]*failure[^/]*\.json$'
        )
    )
}

function Test-VisualSemanticDiagnosticPayloadSchemaText {
    param([Parameter(Mandatory)][string]$Text)

    return $Text -match (
        '"schemaVersion"\s*:\s*"' +
        '(visual-semantic-host-failure-1\.(0|1)|' +
        'visual-semantic-sampling-audit-1\.0|' +
        'visual-semantic-sampling-audit-artifact-1\.0|' +
        'visual-semantic-sampling-audit-refresh-(parity|provenance)-1\.0|' +
        'visual-semantic-input-policy-validity-decision-1\.0)"'
    )
}

function Test-VisualSemanticSamplingRefreshPayloadPath {
    param([Parameter(Mandatory)][string]$Path)

    $normalized = $Path -replace '\\', '/'

    return (
        $normalized -match (
            '(^|/)[^/]*' +
            '(current[-_. ]*host[-_. ]*sampling[-_. ]*audit|' +
            'current[-_. ]*host[-_. ]*sampling[-_. ]*parity|' +
            'sampling[-_. ]*audit[-_. ]*refresh)' +
            '[^/]*/'
        ) -or
        $normalized -match (
            '(^|/)' +
            '(sampling[-_. ]*audit[-_. ]*refresh|' +
            'current[-_. ]*host[-_. ]*sampling[-_. ]*audit)' +
            '[-_. ]*(parity|provenance)[^/]*\.(json|md)$'
        ) -or
        $normalized -match (
            '(^|/)current[-_. ]*host[-_. ]*sampling[-_. ]*parity' +
            '[^/]*\.(json|md)$'
        )
    )
}

function Test-VisualSemanticAttemptPayloadFileName {
    param([Parameter(Mandatory)][string]$Path)

    return $Path -match (
        '(^|/)(visual-semantic-provider-attempt-batch|' +
        'provider-attempt-batch|attempt-batch|' +
        'provider-compatibility-matrix|' +
        'provider-attempt-report)\.(json|md)$'
    )
}

function Test-VisualSemanticAttemptPayloadSchemaText {
    param([Parameter(Mandatory)][string]$Text)

    return $Text -match (
        '"schemaVersion"\s*:\s*"' +
        '(visual-semantic-provider-attempt-batch-1\.0|' +
        'visual-semantic-provider-compatibility-matrix-1\.0|' +
        'visual-semantic-provider-attempt-report-1\.0|' +
        'visual-semantic-provider-terminal-decision-1\.2|' +
        'visual-semantic-observation-batch-1\.5)"'
    )
}

function Test-VisualSemanticMediaPayloadFileName {
    param([Parameter(Mandatory)][string]$Path)

    return $Path -match '\.(avi|m4a|mkv|mov|mp3|mp4|wav|webm)$'
}

function Test-VisualSemanticPrompt2GeneratedPayloadFileName {
    param([Parameter(Mandatory)][string]$Path)

    return $Path -match (
        '(^|/)(prompt2-(full-context-input-batch|' +
        'repeat-input-batch|visual-only-input-batch|' +
        'prompt-manifest|development-configuration-lock|' +
        'blinding-proof|model-free-validation)|' +
        'semantic-content-parity)\.(json|md)$'
    )
}

function Test-VisualSemanticPrompt2GeneratedSchemaText {
    param([Parameter(Mandatory)][string]$Text)

    return $Text -match (
        '"schemaVersion"\s*:\s*"' +
        '(visual-semantic-prompt2-development-configuration-lock-1\.0|' +
        'visual-semantic-prompt-manifest-2\.0|' +
        'visual-semantic-prompt2-semantic-parity-1\.0|' +
        'visual-semantic-prompt2-blinding-proof-1\.0)"'
    )
}

function Test-VisualSemanticPrompt2AttemptPayloadFileName {
    param([Parameter(Mandatory)][string]$Path)

    return $Path -match (
        '(^|/)[^/]*prompt2[^/]*' +
        '(attempt|completed[-_. ]*execution|contract[-_. ]*pilot|' +
        'parse[-_. ]*diagnostic|recovery)' +
        '[^/]*\.(json|md)$'
    )
}

function Test-VisualSemanticPrompt2AttemptSchemaText {
    param([Parameter(Mandatory)][string]$Text)

    return $Text -match (
        '"schemaVersion"\s*:\s*"' +
        '(visual-semantic-editorial-attempt-set-1\.0|' +
        'visual-semantic-editorial-development-attempt-1\.0|' +
        'visual-semantic-editorial-development-execution-1\.0|' +
        'visual-semantic-editorial-contract-pilot-(plan|attempt|completed)-1\.0|' +
        'visual-semantic-editorial-(attempt-diagnostic|attempt-recovery|' +
        'pilot-recovery)-1\.0)"'
    )
}

if (
    -not (Test-RawAuditFileName 'artifacts/smoke.raw-audit.2026.json') -or
    -not (Test-RawAuditFileName 'provider-raw-output-audit-case-01.json') -or
    (Test-RawAuditFileName 'VisualSemanticOutputNormalization.cs') -or
    -not (Test-RawAuditSchemaText '{"schemaVersion":"visual-semantic-raw-output-audit-1.0"}') -or
    -not (Test-RawAuditSchemaText '{"schemaVersion":"visual-semantic-raw-output-audit-1.1"}') -or
    -not (Test-RawAuditSchemaText '{"schemaVersion":"visual-semantic-raw-output-audit-1.2"}') -or
    (Test-RawAuditSchemaText '{"schemaVersion":"visual-semantic-observation-batch-1.1"}') -or
    -not (Test-VisualSemanticGenerationPayloadFileName 'artifacts/generation-audit.json') -or
    -not (Test-VisualSemanticGenerationPayloadFileName 'frozen-qwen3-vl-primary-ordinal-6-smoke.json') -or
    (Test-VisualSemanticGenerationPayloadFileName 'VisualSemanticGenerationManifest.cs') -or
    -not (
        Test-VisualSemanticGenerationPayloadSchemaText (
            '{"schemaVersion":"visual-semantic-generation-manifest-1.0"}'
        )
    ) -or
    (
        Test-VisualSemanticGenerationPayloadSchemaText (
            '{"schemaVersion":"visual-semantic-observation-batch-1.4"}'
        )
    ) -or
    -not (Test-VisualSemanticDiagnosticPayloadFileName 'artifacts/sampling-audit.json') -or
    -not (Test-VisualSemanticDiagnosticPayloadFileName 'sampling-audit-artifact-index.json') -or
    -not (Test-VisualSemanticDiagnosticPayloadFileName 'primary-host-failure.json') -or
    (Test-VisualSemanticDiagnosticPayloadFileName 'Qwen3VlSamplingAuditParser.cs') -or
    -not (
        Test-VisualSemanticSamplingRefreshPayloadPath (
            'artifacts/evaluation-current-host-sampling-audit/integrity/artifact-index.json'
        )
    ) -or
    -not (
        Test-VisualSemanticSamplingRefreshPayloadPath (
            'artifacts/sampling-audit-refresh-parity.json'
        )
    ) -or
    -not (
        Test-VisualSemanticSamplingRefreshPayloadPath (
            'artifacts/evaluation-current-host-sampling-parity/artifact-index.json'
        )
    ) -or
    -not (
        Test-VisualSemanticSamplingRefreshPayloadPath (
            'artifacts/sampling-audit-refresh-provenance.md'
        )
    ) -or
    (
        Test-VisualSemanticSamplingRefreshPayloadPath (
            'eng/visual-semantic-host/replayfoundry-visual-semantic-current-host-sampling-audit-refresh-policy-1.0.txt'
        )
    ) -or
    (
        Test-VisualSemanticSamplingRefreshPayloadPath (
            'VisualSemanticSamplingAuditRefreshPolicy.cs'
        )
    ) -or
    -not (
        Test-VisualSemanticDiagnosticPayloadSchemaText (
            '{"schemaVersion":"visual-semantic-host-failure-1.0"}'
        )
    ) -or
    -not (
        Test-VisualSemanticDiagnosticPayloadSchemaText (
            '{"schemaVersion":"visual-semantic-host-failure-1.1"}'
        )
    ) -or
    -not (
        Test-VisualSemanticDiagnosticPayloadSchemaText (
            '{"schemaVersion":"visual-semantic-sampling-audit-artifact-1.0"}'
        )
    ) -or
    -not (
        Test-VisualSemanticDiagnosticPayloadSchemaText (
            '{"schemaVersion":"visual-semantic-sampling-audit-refresh-parity-1.0"}'
        )
    ) -or
    -not (
        Test-VisualSemanticDiagnosticPayloadSchemaText (
            '{"schemaVersion":"visual-semantic-sampling-audit-refresh-provenance-1.0"}'
        )
    ) -or
    (
        Test-VisualSemanticDiagnosticPayloadSchemaText (
            '{"schemaVersion":"visual-semantic-current-host-sampling-audit-refresh-policy-1.0"}'
        )
    ) -or
    -not (
        Test-VisualSemanticAttemptPayloadFileName (
            'artifacts/visual-semantic-provider-attempt-batch.json'
        )
    ) -or
    -not (
        Test-VisualSemanticAttemptPayloadFileName (
            'owned-workspace/attempt-batch.json'
        )
    ) -or
    -not (
        Test-VisualSemanticAttemptPayloadFileName (
            'provider-compatibility-matrix.json'
        )
    ) -or
    -not (
        Test-VisualSemanticAttemptPayloadFileName (
            'provider-attempt-report.md'
        )
    ) -or
    (
        Test-VisualSemanticAttemptPayloadFileName (
            'Qwen3VlProviderAttemptContracts.cs'
        )
    ) -or
    (
        Test-VisualSemanticAttemptPayloadFileName (
            'docs/provider-attempt-design.md'
        )
    ) -or
    -not (
        Test-VisualSemanticAttemptPayloadSchemaText (
            '{"schemaVersion":"visual-semantic-provider-attempt-batch-1.0"}'
        )
    ) -or
    -not (
        Test-VisualSemanticAttemptPayloadSchemaText (
            '{"schemaVersion":"visual-semantic-provider-compatibility-matrix-1.0"}'
        )
    ) -or
    -not (
        Test-VisualSemanticAttemptPayloadSchemaText (
            '{"schemaVersion":"visual-semantic-provider-attempt-report-1.0"}'
        )
    ) -or
    -not (
        Test-VisualSemanticAttemptPayloadSchemaText (
            '{"schemaVersion":"visual-semantic-provider-terminal-decision-1.2"}'
        )
    ) -or
    -not (
        Test-VisualSemanticAttemptPayloadSchemaText (
            '{"schemaVersion":"visual-semantic-observation-batch-1.5"}'
        )
    ) -or
    (
        Test-VisualSemanticDiagnosticPayloadSchemaText (
            '{"schemaVersion":"visual-semantic-observation-batch-1.1"}'
        )
    ) -or
    (
        Test-VisualSemanticAttemptPayloadSchemaText (
            '{"schemaVersion":"visual-semantic-observation-batch-1.4"}'
        )
    ) -or
    -not (
        Test-VisualSemanticMediaPayloadFileName (
            'VisualSemanticProviderV05A6/media/case.mp4'
        )
    ) -or
    -not (
        Test-VisualSemanticMediaPayloadFileName (
            'arbitrary/copied-audio.wav'
        )
    ) -or
    -not (
        Test-VisualSemanticPrompt2GeneratedPayloadFileName (
            'artifacts/prompt2-development-configuration-lock.json'
        )
    ) -or
    -not (
        Test-VisualSemanticPrompt2GeneratedPayloadFileName (
            'semantic-content-parity.json'
        )
    ) -or
    (
        Test-VisualSemanticPrompt2GeneratedPayloadFileName (
            'eng/visual-semantic-host/replayfoundry-visual-semantic-editorial-development-gates-1.0.json'
        )
    ) -or
    -not (
        Test-VisualSemanticPrompt2GeneratedSchemaText (
            '{"schemaVersion":"visual-semantic-prompt2-development-configuration-lock-1.0"}'
        )
    ) -or
    -not (
        Test-VisualSemanticPrompt2AttemptPayloadFileName (
            'artifacts/prompt2-development-attempt.raw.json'
        )
    ) -or
    -not (
        Test-VisualSemanticPrompt2AttemptPayloadFileName (
            'artifacts/prompt2-contract-pilot-report.md'
        )
    ) -or
    (
        Test-VisualSemanticPrompt2AttemptPayloadFileName (
            'Qwen3VlEditorialAttemptParser.cs'
        )
    ) -or
    -not (
        Test-VisualSemanticPrompt2AttemptSchemaText (
            '{"schemaVersion":"visual-semantic-editorial-development-attempt-1.0"}'
        )
    ) -or
    -not (
        Test-VisualSemanticPrompt2AttemptSchemaText (
            '{"schemaVersion":"visual-semantic-editorial-contract-pilot-attempt-1.0"}'
        )
    ) -or
    (
        Test-VisualSemanticPrompt2GeneratedSchemaText (
            '{"schemaVersion":"visual-semantic-editorial-observation-2.0"}'
        )
    ) -or
    (
        Test-VisualSemanticMediaPayloadFileName (
            'docs/media-format.md'
        )
    )
) {
    throw 'Repository payload guard visual-semantic self-check failed.'
}

$tracked = @(git -C $repositoryRoot ls-files)

if ($LASTEXITCODE -ne 0) {
    throw 'git ls-files failed.'
}

$untracked = @(
    git -C $repositoryRoot ls-files --others --exclude-standard
)

if ($LASTEXITCODE -ne 0) {
    throw 'git ls-files --others failed.'
}

$repositoryPaths =
    @($tracked + $untracked) |
    Sort-Object -Unique
$violations = @(
    $repositoryPaths | Where-Object {
        $_ -match '(^|/)(runtime-packs|model-packs|tool-cache|transcripts)/' -or
        $_ -match '(^|/)ggml-[^/]+\.bin$' -or
        $_ -match '(^|/)whisper-bin-[^/]+\.zip$' -or
        $_ -match '(^|/)replayfoundry-(media-tools|silero-vad|whisper-cpp|whisper-(base|small)-multilingual|qwen3-vl-runtime|qwen3-vl-4b-instruct)\.zip$' -or
        $_ -match '(^|/)ReplayFoundry-[^/]+-(Base|Advanced)-win-x64-setup\.exe$' -or
        $_ -match '(^|/)(creative-pack-payloads|commerce-catalog-exports)/' -or
        $_ -match '(^|/)replayfoundry-studio-creative-pack-[^/]+\.zip$' -or
        $_ -match '(^|/)whisper-cli\.exe$' -or
        $_ -match '\.(gguf|onnx)$' -or
        $_ -match '(^|/)semantic-review\.codex-provisional\.json$' -or
        $_ -match '(^|/)codex-visual-review-report\.md$' -or
        $_ -match '(^|/)semantic-review-machine\.sealed\.json$' -or
        $_ -match '(^|/)semantic-review-answers\.draft\.json$' -or
        $_ -match '(^|/)proxy-semantic-review\.normalized\.json$' -or
        $_ -match '(^|/)ai-proxy-semantic-development-evaluation\.json$' -or
        $_ -match '(^|/)semantic-proxy-feature-matrix\.jsonl$' -or
        $_ -match '(^|/)semantic-proxy-preference-dataset\.json$' -or
        $_ -match '(^|/)future-semantic-holdout-reservation\.json$' -or
        $_ -match '(^|/)visual-semantic-provider-terminal-decision\.json$' -or
        $_ -match '(^|/)proxy-semantic-development-report\.md$' -or
        $_ -match '(^|/)proxy-semantic-provider-requirements\.md$' -or
        (Test-RawAuditFileName $_) -or
        (Test-VisualSemanticGenerationPayloadFileName $_) -or
        (Test-VisualSemanticDiagnosticPayloadFileName $_) -or
        (Test-VisualSemanticSamplingRefreshPayloadPath $_) -or
        (Test-VisualSemanticAttemptPayloadFileName $_) -or
        (Test-VisualSemanticPrompt2GeneratedPayloadFileName $_) -or
        (Test-VisualSemanticPrompt2AttemptPayloadFileName $_) -or
        (Test-VisualSemanticMediaPayloadFileName $_) -or
        $_ -match '\.(safetensors|pt|pth|ckpt)$' -or
        $_ -match '\.(pyc|pyo)$' -or
        $_ -match '(^|/)(\.venv|venv|site-packages|__pycache__)/' -or
        $_ -match '(^|/)pyvenv\.cfg$' -or
        $_ -match '(^|/)(visual-semantic-input-batch|visual-semantic-observation-batch|visual-semantic-provider-development-evaluation|visual-semantic-provider-research-lock|visual-semantic-ablation-evaluation)\.json$' -or
        $_ -match '(^|/)(full-context-input-batch|visual-only-input-batch|full-context-repeat-input-batch|proxy-development-labels|blinding-proof|observation-batch|ablation-evaluation|repeatability-evaluation|provisional-visual-semantic-research-lock)\.json$' -or
        $_ -match '(^|/)visual-semantic-provider-development-report\.md$' -or
        $_ -match '(^|/)(host-requests|prepared-inputs|evaluation-only|host-results|primary-run|ablation-run|repeat-run|visual-semantic-model|visual-semantic-environment)/'
    }
)

$visualSemanticSchemaViolations = @(
    $repositoryPaths | ForEach-Object {
        $relativePath = $_

        if ($relativePath -notmatch '\.json$') {
            return
        }

        $fullPath = Join-Path $repositoryRoot $relativePath

        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            return
        }

        $file = Get-Item -LiteralPath $fullPath

        if ($file.Length -le 0) {
            return
        }

        $text =
            Get-Content -LiteralPath $fullPath -TotalCount 64 |
            Out-String

        if (
            (Test-RawAuditSchemaText $text) -or
            (Test-VisualSemanticGenerationPayloadSchemaText $text) -or
            (Test-VisualSemanticDiagnosticPayloadSchemaText $text) -or
            (Test-VisualSemanticSamplingRefreshPayloadPath $relativePath) -or
            (Test-VisualSemanticAttemptPayloadSchemaText $text) -or
            (Test-VisualSemanticPrompt2GeneratedSchemaText $text) -or
            (Test-VisualSemanticPrompt2AttemptSchemaText $text)
        ) {
            $relativePath
        }
    }
)
$violations = @(
    @($violations + $visualSemanticSchemaViolations) |
    Sort-Object -Unique
)

if ($violations.Count -ne 0) {
    $joined = $violations -join [Environment]::NewLine
    throw "Tracked runtime/model payloads are forbidden:$([Environment]::NewLine)$joined"
}

Write-Output (
    "Repository payload guard passed: " +
    "$($tracked.Count) tracked and " +
    "$($untracked.Count) untracked paths inspected.")
