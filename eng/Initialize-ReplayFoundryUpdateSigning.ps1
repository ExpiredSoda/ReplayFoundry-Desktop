[CmdletBinding()]
param([string]$SecretDirectory = (Join-Path $env:LOCALAPPDATA 'ReplayFoundryBuildSecrets'))
$ErrorActionPreference = 'Stop'
$sdk = & (Join-Path $PSScriptRoot 'Resolve-ReplayFoundryWinSparkle.ps1')
$secretRoot = [IO.Path]::GetFullPath($SecretDirectory)
New-Item -ItemType Directory -Path $secretRoot -Force | Out-Null
$identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
$acl = [Security.AccessControl.DirectorySecurity]::new()
$acl.SetAccessRuleProtection($true, $false)
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($identity, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
Set-Acl -LiteralPath $secretRoot -AclObject $acl
$protectedPath = Join-Path $secretRoot 'updates-eddsa.dpapi'
$temporary = Join-Path $secretRoot ('update-key-' + [Guid]::NewGuid().ToString('N') + '.tmp')
$entropy = [Text.Encoding]::UTF8.GetBytes('ReplayFoundry.UpdateSigning.v1')
try {
    if (Test-Path -LiteralPath $protectedPath) {
        $plain = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($protectedPath), $entropy, 'CurrentUser')
        [IO.File]::WriteAllBytes($temporary, $plain)
    } else {
        & (Join-Path $sdk 'bin\winsparkle-tool.exe') generate-key --file $temporary | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'WinSparkle key generation failed.' }
        $plain = [IO.File]::ReadAllBytes($temporary)
        $protected = [Security.Cryptography.ProtectedData]::Protect($plain, $entropy, 'CurrentUser')
        [IO.File]::WriteAllBytes($protectedPath, $protected)
    }
    & (Join-Path $sdk 'bin\winsparkle-tool.exe') public-key --private-key-file $temporary
    if ($LASTEXITCODE -ne 0) { throw 'WinSparkle public key extraction failed.' }
} finally {
    if ($null -ne $plain) { [Array]::Clear($plain) }
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
}
