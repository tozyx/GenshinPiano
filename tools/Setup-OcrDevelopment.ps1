#Requires -Version 5.1
param([Alias('SmtTrialDirectory')][string]$SmtResourceDirectory, [string]$MsvcRuntimeDirectory)
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$arguments = @{
    AddonRootDirectory = Join-Path $repositoryRoot 'src\GenshinPiano.App\bin\Debug\net10.0-windows\addons'
    ArtifactsDirectory = Join-Path $repositoryRoot 'artifacts\ocr-development'
    BuildArtifactsDirectory = Join-Path $repositoryRoot 'artifacts\ocr-development'
    SkipSigning = $true
    SkipArchive = $true
}
if ($SmtResourceDirectory) { $arguments.SmtResourceDirectory = $SmtResourceDirectory }
if ($MsvcRuntimeDirectory) { $arguments.MsvcRuntimeDirectory = $MsvcRuntimeDirectory }
& (Join-Path $PSScriptRoot 'Publish-OcrResources.ps1') @arguments
