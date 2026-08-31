param(
    [Parameter(Mandatory = $true)]
    [string]$AddonRootDirectory,

    [Parameter(Mandatory = $true)]
    [string]$ArtifactsDirectory,

    [Parameter(Mandatory = $true)]
    [string]$PrivateKeyPath
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $repositoryRoot "src\GenshinPiano.Ocr.Engine\GenshinPiano.Ocr.Engine.csproj"
$addonRootDirectory = [IO.Path]::GetFullPath($AddonRootDirectory)
$artifactsDirectory = [IO.Path]::GetFullPath($ArtifactsDirectory)
$addonPublishDirectory = Join-Path $addonRootDirectory "ocr"

[xml]$project = [IO.File]::ReadAllText($projectPath)
$version = [string]($project.Project.PropertyGroup.Version | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "The OCR engine project does not define a Version property."
}

New-Item -ItemType Directory -Path $artifactsDirectory -Force | Out-Null
if (Test-Path -LiteralPath $addonPublishDirectory) {
    Remove-Item -LiteralPath $addonPublishDirectory -Recurse -Force
}

Write-Host "Publishing self-contained OCR add-on $version..."
& dotnet publish $projectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:Version=$version `
    -p:PublishSingleFile=false `
    -p:NuGetAudit=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $addonPublishDirectory
if ($LASTEXITCODE -ne 0) {
    throw "OCR add-on publish failed with exit code $LASTEXITCODE."
}

$enginePath = Join-Path $addonPublishDirectory "GPianoOcrEngine.exe"
$manifestPath = Join-Path $addonPublishDirectory "manifest.json"
if (-not (Test-Path -LiteralPath $enginePath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "The OCR add-on publish output is incomplete."
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ([string]$manifest.engineVersion -ne $version) {
    throw "OCR manifest version '$($manifest.engineVersion)' does not match project version '$version'."
}

$packagePath = Join-Path $artifactsDirectory "ocr-addons-$version-win-x64.zip"
$checksumPath = "$packagePath.sha256"
$signaturePath = "$packagePath.sig"
Remove-Item -LiteralPath $packagePath, $checksumPath, $signaturePath -Force -ErrorAction SilentlyContinue

# Include the top-level addons directory so the archive can be extracted
# directly beside GenshinPiano.exe, without bundling it into the application
# release ZIP itself.
Compress-Archive `
    -Path $addonRootDirectory `
    -DestinationPath $packagePath `
    -Force

$hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
$packageName = [IO.Path]::GetFileName($packagePath)
[IO.File]::WriteAllText(
    $checksumPath,
    "$hash  $packageName",
    [Text.Encoding]::ASCII)

& (Join-Path $PSScriptRoot "Sign-UpdatePackage.ps1") `
    -PackagePath $packagePath `
    -PrivateKeyPath $PrivateKeyPath `
    -SignaturePath $signaturePath
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $signaturePath -PathType Leaf)) {
    throw "OCR add-on package signing failed."
}

Write-Host "OCR add-on copied to: $addonPublishDirectory"
Write-Host "OCR add-on package: $packagePath"
Write-Host "OCR add-on checksum: $checksumPath"
Write-Host "OCR add-on signature: $signaturePath"
