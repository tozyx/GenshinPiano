param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [Parameter(Mandatory = $true)]
    [string]$PrivateKeyPath,

    [string]$SignaturePath = "$PackagePath.sig"
)

$ErrorActionPreference = "Stop"
$packageFullPath = [IO.Path]::GetFullPath($PackagePath)
$privateFullPath = [IO.Path]::GetFullPath($PrivateKeyPath)
$signatureFullPath = [IO.Path]::GetFullPath($SignaturePath)

if (-not (Test-Path -LiteralPath $packageFullPath -PathType Leaf)) {
    throw "Update package not found: $packageFullPath"
}
if (-not (Test-Path -LiteralPath $privateFullPath -PathType Leaf)) {
    throw "Update signing private key not found: $privateFullPath"
}

$rsa = [Security.Cryptography.RSACryptoServiceProvider]::new()
try {
    $rsa.PersistKeyInCsp = $false
    $rsa.FromXmlString([IO.File]::ReadAllText($privateFullPath))
    $sha256 = [Security.Cryptography.SHA256]::Create()
    $packageStream = [IO.File]::OpenRead($packageFullPath)
    try {
        $hash = $sha256.ComputeHash($packageStream)
    }
    finally {
        $packageStream.Dispose()
        $sha256.Dispose()
    }
    $hashText = [BitConverter]::ToString($hash).Replace("-", "")
    $canonical = "GenshinPiano.Update.v1`n$([IO.Path]::GetFileName($packageFullPath))`n$hashText`n"
    $canonicalBytes = [Text.Encoding]::UTF8.GetBytes($canonical)
    $signature = $rsa.SignData($canonicalBytes, "SHA256")
    [IO.File]::WriteAllText(
        $signatureFullPath,
        [Convert]::ToBase64String($signature),
        [Text.Encoding]::ASCII)
}
finally {
    $rsa.Dispose()
}

Write-Host "Signature created: $signatureFullPath"
