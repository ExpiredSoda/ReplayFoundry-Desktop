[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SitePackages,
    [Parameter(Mandatory)][string]$ReportPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$packageRoot = (Resolve-Path -LiteralPath $SitePackages).Path
$packages = @(Get-ChildItem -LiteralPath $packageRoot -Directory -Filter '*.dist-info' | ForEach-Object {
    $metadata = Join-Path $_.FullName 'METADATA'
    if (-not (Test-Path -LiteralPath $metadata -PathType Leaf)) { throw 'Python package metadata is missing.' }
    $headers = Get-Content -LiteralPath $metadata -TotalCount 40
    $names = @($headers | Where-Object { $_ -match '^Name: ' })
    $versions = @($headers | Where-Object { $_ -match '^Version: ' })
    if ($names.Count -ne 1 -or $versions.Count -ne 1) { throw 'Python package identity is ambiguous.' }
    $name = $names[0].Substring(6).Trim()
    $version = $versions[0].Substring(9).Trim()
    if ($name -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$' -or
        $version -notmatch '^[A-Za-z0-9][A-Za-z0-9.+_-]{0,79}$') { throw 'Invalid Python package identity.' }
    [pscustomobject]@{ Name = $name; Version = $version }
})
if ($packages.Count -eq 0 -or $packages.Count -gt 200) { throw 'The AI runtime must have a bounded package inventory.' }
$queries = @($packages | ForEach-Object {
    @{ package = @{ name = $_.Name; ecosystem = 'PyPI' }; version = $_.Version.Split('+')[0] }
})
# Only public distribution names and versions leave the build machine. No paths or runtime content are submitted.
$response = Invoke-RestMethod -Uri 'https://api.osv.dev/v1/querybatch' -Method Post -ContentType 'application/json' `
    -Body (@{ queries = $queries } | ConvertTo-Json -Depth 5) -TimeoutSec 30 -MaximumRedirection 0
if (-not $response.PSObject.Properties['results'] -or @($response.results).Count -ne $packages.Count) {
    throw 'The advisory service did not verify every package. Do not publish this runtime.'
}
$results = @(for ($index = 0; $index -lt $packages.Count; $index++) {
    $result = $response.results[$index]
    if ($null -eq $result) { throw 'The advisory service returned an invalid package result.' }
    $unexpected = @($result.PSObject.Properties | Where-Object { $_.Name -notin @('vulns', 'next_page_token') })
    if ($unexpected.Count -gt 0 -or ($result.PSObject.Properties['next_page_token'] -and $result.next_page_token)) {
        throw 'The advisory service returned an error or incomplete package result. Do not publish this runtime.'
    }
    $advisories = @()
    if ($result.PSObject.Properties['vulns']) {
        $advisories = @($result.vulns | ForEach-Object {
            if (-not $_.PSObject.Properties['id'] -or [string]$_.id -notmatch '^[A-Za-z0-9._-]+$') {
                throw 'The advisory service returned an invalid advisory.'
            }
            [string]$_.id
        })
    }
    [pscustomobject]@{ Name = $packages[$index].Name; Version = $packages[$index].Version; Advisories = $advisories }
})
$affected = @($results | Where-Object { $_.Advisories.Count -gt 0 })
$report = @{ SchemaVersion = 1; CheckedAtUtc = [DateTimeOffset]::UtcNow.ToString('O');
    Service = 'https://api.osv.dev'; Passed = $affected.Count -eq 0; Packages = $results }
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding utf8
if ($affected.Count -gt 0) {
    throw "AI runtime dependencies have known advisories: $($affected.Name -join ', '). Update and requalify the runtime before packaging. See $ReportPath."
}
Write-Host "AI runtime advisory check passed for $($packages.Count) packages."
