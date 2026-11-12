#Requires -Version 7.0
param(
    [string]$SmtTrialDirectory,
    [string]$MsvcRuntimeDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$engineProject = Join-Path $repositoryRoot 'src\GenshinPiano.Ocr.Engine\GenshinPiano.Ocr.Engine.csproj'
$buildArtifacts = Join-Path $repositoryRoot 'artifacts\ocr-development'
$addonDirectory = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'src\GenshinPiano.App\bin\Debug\net10.0-windows\addons\ocr'))

& dotnet build $engineProject -c Debug --artifacts-path $buildArtifacts -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'OCR Debug build failed.' }

# Fixed, resolved output under the workspace, checked before recursive replacement.
$expectedDirectory = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'src\GenshinPiano.App\bin\Debug\net10.0-windows\addons\ocr'))
if ($addonDirectory -ne $expectedDirectory -or -not $addonDirectory.StartsWith($repositoryRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Unexpected development output directory.'
}
if (Test-Path -LiteralPath $addonDirectory) { Remove-Item -LiteralPath $addonDirectory -Recurse -Force }
New-Item -ItemType Directory -Path $addonDirectory -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $buildArtifacts 'bin\GenshinPiano.Ocr.Engine\debug') -Force |
    Copy-Item -Destination $addonDirectory -Recurse -Force
& (Join-Path $PSScriptRoot 'Publish-OcrSmtRuntime.ps1') `
    -DestinationDirectory (Join-Path $addonDirectory 'staff-omr\smt') `
    -SmtTrialDirectory $SmtTrialDirectory -MsvcRuntimeDirectory $MsvcRuntimeDirectory
$manifest = Get-Content -LiteralPath (Join-Path $addonDirectory 'manifest.json') -Raw | ConvertFrom-Json
foreach ($required in $manifest.requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $addonDirectory $required) -PathType Leaf)) { throw "Missing runtime file: $required" }
}
Write-Host "OCR development add-on is ready: $addonDirectory"