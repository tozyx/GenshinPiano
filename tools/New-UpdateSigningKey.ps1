param(
    [Parameter(Mandatory = $true)]
    [string]$PrivateKeyPath,

    [string]$PublicKeyPath
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($PublicKeyPath)) {
    $PublicKeyPath = Join-Path $PSScriptRoot "..\src\GenshinPiano.App\Assets\Security\UpdateSigningPublicKey.xml"
}
$privateFullPath = [IO.Path]::GetFullPath($PrivateKeyPath)
$publicFullPath = [IO.Path]::GetFullPath($PublicKeyPath)

if (Test-Path -LiteralPath $privateFullPath) {
    throw "Refusing to overwrite an existing private key: $privateFullPath"
}

[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($privateFullPath)) | Out-Null
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($publicFullPath)) | Out-Null

$rsa = [Security.Cryptography.RSACryptoServiceProvider]::new(3072)
try {
    $rsa.PersistKeyInCsp = $false
    [IO.File]::WriteAllText(
        $privateFullPath,
        $rsa.ToXmlString($true),
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(
        $publicFullPath,
        $rsa.ToXmlString($false),
        [Text.UTF8Encoding]::new($false))
}
finally {
    $rsa.Dispose()
}

Write-Host "Private key (keep secret and back it up): $privateFullPath"
Write-Host "Public key (commit with the application): $publicFullPath"
