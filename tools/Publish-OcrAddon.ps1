#Requires -Version 5.1
# Compatibility entry point; new releases contain resources, not a standalone engine.
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
& (Join-Path $PSScriptRoot 'Publish-OcrResources.ps1') @PSBoundParameters
