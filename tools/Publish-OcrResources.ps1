#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$AddonRootDirectory,
    [Parameter(Mandatory = $true)][string]$ArtifactsDirectory,
    [string]$PrivateKeyPath,
    [string]$SmtTrialDirectory,
    [string]$MsvcRuntimeDirectory,
    [string]$BuildArtifactsDirectory,
    [string]$Version,
    [switch]$SkipSigning,
    [switch]$SkipArchive
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$engineDirectory = Join-Path $repositoryRoot 'src\GenshinPiano.Ocr.Engine'
$projectPath = Join-Path $engineDirectory 'GenshinPiano.Ocr.Engine.csproj'
$addonRoot = [IO.Path]::GetFullPath($AddonRootDirectory)
$output = [IO.Path]::GetFullPath($ArtifactsDirectory)
$target = [IO.Path]::GetFullPath((Join-Path $addonRoot 'ocr'))
if ([IO.Path]::GetFileName($addonRoot.TrimEnd('\')) -ne 'addons') { throw 'AddonRootDirectory must be named addons.' }
if ($target -eq $repositoryRoot -or $repositoryRoot.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing to replace the workspace.' }
if (-not $SkipSigning -and -not $SkipArchive -and -not (Test-Path -LiteralPath $PrivateKeyPath -PathType Leaf)) { throw 'A signing key is required for release packaging. Use SkipSigning only for local tests.' }
if (-not $SmtTrialDirectory) { $SmtTrialDirectory = Join-Path $repositoryRoot 'publish\smt-resource' }
if (-not $MsvcRuntimeDirectory) { $MsvcRuntimeDirectory = Join-Path $env:WINDIR 'System32' }
if (-not $BuildArtifactsDirectory) { $BuildArtifactsDirectory = Join-Path $repositoryRoot 'artifacts\ocr-resources-publish' }
$buildArtifacts = [IO.Path]::GetFullPath($BuildArtifactsDirectory)
$manifest = Get-Content -LiteralPath (Join-Path $engineDirectory 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $Version) { $Version = [string]$manifest.resourceVersion }
if ($Version -cnotmatch '\A[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?\z') { throw 'Resource version must be major.minor.patch with an optional prerelease suffix.' }
if ($manifest.schemaVersion -ne 2 -or $manifest.engineCompatibilityVersion -ne 1) { throw 'Unsupported source resource manifest.' }
$manifest.resourceVersion = $Version

# Restore packages for their native libraries/models; never publish another .NET runtime.
& dotnet restore $projectPath --artifacts-path $buildArtifacts -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Restoring OCR dependencies failed.' }
$assets = Get-Content -LiteralPath (Join-Path $buildArtifacts 'obj\GenshinPiano.Ocr.Engine\project.assets.json') -Raw -Encoding UTF8 | ConvertFrom-Json
function Find-Package([string]$id) {
    $library = $assets.libraries.PSObject.Properties | Where-Object { $_.Name.Split('/')[0] -eq $id } | Select-Object -First 1
    if ($null -eq $library) { throw "Package not found: $id" }
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $path = Join-Path $folder $library.Value.path
        if (Test-Path -LiteralPath $path -PathType Container) { return $path }
    }
    throw "Package cache not found: $id"
}
$onnx = Find-Package 'Microsoft.ML.OnnxRuntime'
$skia = Find-Package 'SkiaSharp.NativeAssets.Win32'
$rapid = Find-Package 'RapidOcrNet'

# Only replace the verified, immediate 'ocr' child of the requested addons directory.
if ([IO.Path]::GetDirectoryName($target) -ne $addonRoot.TrimEnd('\')) { throw 'Unexpected OCR destination.' }
if (Test-Path -LiteralPath $target) {
    $resolved = (Resolve-Path -LiteralPath $target).ProviderPath
    if ($resolved -ne $target) { throw 'Unexpected resolved OCR destination.' }
    Remove-Item -LiteralPath $target -Recurse -Force
}
$native = Join-Path $target 'native'
$v5 = Join-Path $target 'models\v5'
$orpheus = Join-Path $target 'models\orpheusnet'
New-Item -ItemType Directory -Path $native, $v5, $orpheus, $output -Force | Out-Null
foreach ($name in @('onnxruntime.dll', 'onnxruntime_providers_shared.dll')) {
    Copy-Item -LiteralPath (Join-Path $onnx "runtimes\win-x64\native\$name") -Destination $native
}
Copy-Item -LiteralPath (Join-Path $skia 'runtimes\win-x64\native\libSkiaSharp.dll') -Destination $native
foreach ($name in @('ch_PP-OCRv5_mobile_det.onnx','ch_ppocr_mobile_v2.0_cls_infer.onnx','latin_PP-OCRv5_rec_mobile_infer.onnx','ppocrv5_latin_dict.txt')) {
    Copy-Item -LiteralPath (Join-Path $rapid "models\v5\$name") -Destination $v5
}
foreach ($name in @('orpheusnet-srcnn-x3.onnx','orpheusnet-middle.onnx','orpheusnet-metadata.onnx')) {
    Copy-Item -LiteralPath (Join-Path $engineDirectory "models\orpheusnet\$name") -Destination $orpheus
}
& (Join-Path $PSScriptRoot 'Publish-OcrSmtRuntime.ps1') -DestinationDirectory (Join-Path $target 'staff-omr\smt') -SmtTrialDirectory $SmtTrialDirectory -MsvcRuntimeDirectory $MsvcRuntimeDirectory
foreach ($required in $manifest.requiredFiles) {
    $file = Join-Path $target $required
    if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -eq 0) { throw "Missing OCR resource: $required" }
}
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES.md') -Destination $target
[IO.File]::WriteAllText((Join-Path $target 'manifest.json'), ($manifest | ConvertTo-Json -Depth 20) + "`n", [Text.UTF8Encoding]::new($false))
if ($SkipArchive) { Write-Host "OCR resources prepared: $target"; return }

$packagePath = Join-Path $output "ocr-resources-$Version-win-x64.zip"
$checksumPath = "$packagePath.sha256"
$signaturePath = "$packagePath.sig"
foreach ($path in @($packagePath, $checksumPath, $signaturePath)) { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force } }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($packagePath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $target -File -Recurse) {
        $relative = $file.FullName.Substring($target.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, "addons/ocr/$relative", [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
$hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($checksumPath, "$hash  $([IO.Path]::GetFileName($packagePath))", [Text.Encoding]::ASCII)
if (-not $SkipSigning) {
    & (Join-Path $PSScriptRoot 'Sign-UpdatePackage.ps1') -PackagePath $packagePath -PrivateKeyPath $PrivateKeyPath -SignaturePath $signaturePath
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $signaturePath)) { throw 'Signing OCR resources failed.' }
}
Write-Host "OCR resource package: $packagePath"
