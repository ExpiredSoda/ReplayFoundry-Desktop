[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InstallerManifestPath,
    [Parameter(Mandatory)][string]$OutputPath,
    [Parameter(Mandatory)][string]$ReleaseNotes,
    [string]$PreviousAppcastPath,
    [string]$ProtectedKeyPath = (Join-Path $env:LOCALAPPDATA 'ReplayFoundryBuildSecrets\updates-eddsa.dpapi')
)
$ErrorActionPreference = 'Stop'
$config = Import-PowerShellDataFile (Join-Path $PSScriptRoot 'ReplayFoundry.Updates.psd1')
$manifestPath = [IO.Path]::GetFullPath($InstallerManifestPath)
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.releaseChannel -ne 'Production' -or $manifest.sourceTreeDirty -or
    $manifest.signing.mode -ne 'ArtifactSigning' -or $manifest.signing.status -ne 'Valid' -or $manifest.profile -ne 'Base') {
    throw 'Appcasts require a clean, signed Production Base installer manifest.'
}
$version = [Version]$manifest.fileVersion
if ($version.Revision -lt 1) { throw 'An app update requires a positive build revision.' }
if ($PreviousAppcastPath) {
    $readerSettings = [Xml.XmlReaderSettings]::new()
    $readerSettings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $readerSettings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create([IO.Path]::GetFullPath($PreviousAppcastPath), $readerSettings)
    try {
        $previous = [Xml.XmlDocument]::new()
        $previous.XmlResolver = $null
        $previous.Load($reader)
        foreach ($node in $previous.GetElementsByTagName('enclosure')) {
            $oldVersion = $node.GetAttribute('version', 'http://www.andymatuschak.org/xml-namespaces/sparkle')
            if ($oldVersion -and $version -le [Version]$oldVersion) { throw 'The new build must be newer than every published build in its channel.' }
        }
    } finally { $reader.Dispose() }
}
$name = [string]$manifest.installer.fileName
if ([IO.Path]::GetFileName($name) -cne $name) { throw 'Unsafe installer file name.' }
$installer = Join-Path (Split-Path -Parent $manifestPath) ('installer\' + $name)
$expectedUrl = "https://github.com/ExpiredSoda/ReplayFoundry-Desktop/releases/download/v$($manifest.productVersion)/$name"
if ($manifest.installer.downloadUri -cne $expectedUrl) { throw 'The update must use its immutable official GitHub release URL.' }
$authenticode = Get-AuthenticodeSignature -LiteralPath $installer
if ($authenticode.Status -ne 'Valid' -or $authenticode.SignerCertificate.Subject -notmatch 'CN=Expired Soda Studios LLC(?:,|$)' -or
    (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash -ne $manifest.installer.sha256 -or
    (Get-Item -LiteralPath $installer).Length -ne $manifest.installer.byteLength) {
    throw 'The installer no longer matches its signed release evidence.'
}
$sdk = & (Join-Path $PSScriptRoot 'Resolve-ReplayFoundryWinSparkle.ps1')
$tool = Join-Path $sdk 'bin\winsparkle-tool.exe'
$secretRoot = Split-Path -Parent ([IO.Path]::GetFullPath($ProtectedKeyPath))
$temporary = Join-Path $secretRoot ('update-sign-' + [Guid]::NewGuid().ToString('N') + '.tmp')
try {
    $plain = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($ProtectedKeyPath),
        [Text.Encoding]::UTF8.GetBytes('ReplayFoundry.UpdateSigning.v1'), 'CurrentUser')
    [IO.File]::WriteAllBytes($temporary, $plain)
    $publicOutput = (& $tool public-key --private-key-file $temporary) -join "`n"
    if ($LASTEXITCODE -ne 0 -or -not $publicOutput.Contains('Public key: ' + $config.PublicKey)) {
        throw 'The signing key does not match the public key embedded in this release.'
    }
    $signature = ((& $tool sign --private-key-file $temporary $installer) -join '').Trim()
    if ($LASTEXITCODE -ne 0 -or [Convert]::FromBase64String($signature).Length -ne 64) { throw 'EdDSA update signing failed.' }
} finally {
    if ($null -ne $plain) { [Array]::Clear($plain) }
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
}
& $tool verify --public-key $config.PublicKey --signature $signature $installer
if ($LASTEXITCODE -ne 0) { throw 'The finished update failed EdDSA verification.' }
$sparkle = 'http://www.andymatuschak.org/xml-namespaces/sparkle'
$document = [Xml.XmlDocument]::new()
$document.AppendChild($document.CreateXmlDeclaration('1.0', 'utf-8', $null)) | Out-Null
$rss = $document.CreateElement('rss')
$rss.SetAttribute('version', '2.0')
$rss.SetAttribute('xmlns:sparkle', $sparkle)
$document.AppendChild($rss) | Out-Null
$channel = $document.CreateElement('channel')
$rss.AppendChild($channel) | Out-Null
function Add-TextElement($Parent, [string]$Name, [string]$Text) {
    $node = $document.CreateElement($Name)
    $node.InnerText = $Text
    $Parent.AppendChild($node) | Out-Null
}
Add-TextElement $channel 'title' 'Replay Foundry updates'
$item = $document.CreateElement('item')
$channel.AppendChild($item) | Out-Null
Add-TextElement $item 'title' "Replay Foundry $($manifest.productVersion)"
$safeNotes = [Net.WebUtility]::HtmlEncode($ReleaseNotes)
$safeVersion = [Net.WebUtility]::HtmlEncode([string]$manifest.productVersion)
$notesHtml = @"
<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="color-scheme" content="dark light">
<style>body{margin:0;padding:22px;background:#131c24;color:#edf4f8;font:15px/1.55 "Segoe UI",sans-serif}
.brand{color:#58d6ff;font-size:12px;font-weight:600;letter-spacing:.08em}h1{font-size:21px;line-height:1.25;margin:10px 0 16px}
.notes{white-space:pre-wrap}footer{border-top:1px solid #34414b;margin-top:22px;padding-top:14px;color:#bdcbd5;font-size:12px}</style>
</head><body><div class="brand">REPLAY FOUNDRY</div><h1>What's new in $safeVersion</h1><div class="notes">$safeNotes</div>
<footer>Signed by Expired Soda Studios LLC. You choose when to install.</footer></body></html>
"@
Add-TextElement $item 'description' $notesHtml
Add-TextElement $item 'pubDate' ([DateTimeOffset]::UtcNow.ToString('r'))
$minimum = $document.CreateElement('sparkle', 'minimumSystemVersion', $sparkle)
$minimum.InnerText = '10.0.19041'
$item.AppendChild($minimum) | Out-Null
$enclosure = $document.CreateElement('enclosure')
$enclosure.SetAttribute('url', $expectedUrl)
$enclosure.SetAttribute('length', [string]$manifest.installer.byteLength)
$enclosure.SetAttribute('type', 'application/octet-stream')
foreach ($pair in @{
    version = $version.ToString(); shortVersionString = [string]$manifest.productVersion
    edSignature = $signature; os = 'windows-x64'
}.GetEnumerator()) {
    $attribute = $document.CreateAttribute('sparkle', $pair.Key, $sparkle)
    $attribute.Value = $pair.Value
    $enclosure.Attributes.Append($attribute) | Out-Null
}
$item.AppendChild($enclosure) | Out-Null
$target = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
$document.Save($target)
Write-Output "Verified signed update feed: $target"
