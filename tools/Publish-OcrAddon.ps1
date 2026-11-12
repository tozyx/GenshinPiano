#Requires -Version 7.0
param(
    [Parameter(Mandatory = $true)]
    [string]$AddonRootDirectory,

    [Parameter(Mandatory = $true)]
    [string]$ArtifactsDirectory,

    [Parameter(Mandatory = $true)]
    [string]$PrivateKeyPath,

    [string]$SmtTrialDirectory,

    [string]$MsvcRuntimeDirectory,

    [string]$BuildArtifactsDirectory
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $repositoryRoot "src\GenshinPiano.Ocr.Engine\GenshinPiano.Ocr.Engine.csproj"
$addonRootDirectory = [IO.Path]::GetFullPath($AddonRootDirectory)
$artifactsDirectory = [IO.Path]::GetFullPath($ArtifactsDirectory)
$addonPublishDirectory = Join-Path $addonRootDirectory "ocr"
if ([string]::IsNullOrWhiteSpace($SmtTrialDirectory)) { $SmtTrialDirectory = Join-Path $repositoryRoot 'publish\smt-experiment' }
if ([string]::IsNullOrWhiteSpace($MsvcRuntimeDirectory)) { $MsvcRuntimeDirectory = Join-Path $env:WINDIR 'System32' }
if ([string]::IsNullOrWhiteSpace($BuildArtifactsDirectory)) { $BuildArtifactsDirectory = Join-Path $repositoryRoot 'artifacts\ocr-publish' }
$smtTrialRoot = [IO.Path]::GetFullPath($SmtTrialDirectory)
$buildArtifacts = [IO.Path]::GetFullPath($BuildArtifactsDirectory)
if ([IO.Path]::GetFileName($addonRootDirectory.TrimEnd('\')) -ne 'addons') { throw 'AddonRootDirectory must be named addons to preserve the update ZIP layout.' }
$publishPrefix = $addonPublishDirectory.TrimEnd('\') + '\'
if ($repositoryRoot -eq $addonPublishDirectory -or $repositoryRoot.StartsWith($publishPrefix,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to overwrite the source workspace.'
}
$nativeArchive = Join-Path $smtTrialRoot 'crispembed-windows-x86_64.zip'
$modelName = 'smt-grandstaff-q8_0.gguf'
$modelPath = Join-Path $smtTrialRoot $modelName
if (-not (Test-Path -LiteralPath $modelPath -PathType Leaf)) { $modelPath = Join-Path (Join-Path $smtTrialRoot 'models') $modelName }
if ((Get-FileHash -LiteralPath $nativeArchive -Algorithm SHA256).Hash -ne 'e82337bcdefb1d54b0d97c26fc7e64ff35847d1db285e5062d6674df91ce6017') { throw 'SMT CPU archive SHA-256 mismatch.' }
if ((Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash -ne 'f6612885a8d7015bef5ab7f045a37a0f667575f4b65b324a353cf348d6edc752') { throw 'SMT model SHA-256 mismatch.' }

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
    --artifacts-path $buildArtifacts `
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

& (Join-Path $PSScriptRoot 'Publish-OcrSmtRuntime.ps1') `
    -DestinationDirectory (Join-Path $addonPublishDirectory 'staff-omr\smt') `
    -SmtTrialDirectory $SmtTrialDirectory `
    -MsvcRuntimeDirectory $MsvcRuntimeDirectory
foreach ($required in $manifest.requiredFiles) {
    $file = Join-Path $addonPublishDirectory $required
    if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -eq 0) { throw "Required OCR runtime file is missing: $required" }
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
