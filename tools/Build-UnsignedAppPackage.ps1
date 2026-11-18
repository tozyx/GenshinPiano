#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\package-check'),
    [string]$Version = '0.0.0-test',
    [ValidateSet('Framework', 'SelfContained')][string]$PackageType = 'Framework',
    [switch]$IncludeOcrResources
)

$ErrorActionPreference = 'Stop'
if ($Version -cnotmatch '\A[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?\z') {
    throw "Invalid package version: $Version"
}
$root = Split-Path -Parent $PSScriptRoot
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
$selfContained = $PackageType -eq 'SelfContained'
$flavor = if ($selfContained) { '' } else { '-framework' }
$package = Join-Path $output "GenshinPiano-$Version-win-x64$flavor-unsigned.zip"
$scratch = Join-Path $output ([guid]::NewGuid().ToString('N'))
$staging = Join-Path $scratch 'app'
foreach ($path in @($package, "$package.sha256", "$package.build-info.json")) {
    if (Test-Path -LiteralPath $path) { throw "Output already exists: $path" }
}
if (-not (Get-ChildItem -LiteralPath (Join-Path $root 'publish/songs') -File -Filter '*.gpiano')) {
    throw 'At least one bundled score is required under publish/songs.'
}

New-Item -ItemType Directory -Path $staging -Force | Out-Null
Push-Location $root
try {
    $publishOptions = @(
        '-c', 'Release', '-r', 'win-x64', '--self-contained', $selfContained.ToString().ToLowerInvariant(),
        '-p:NuGetAudit=false', '-p:DebugType=None', '-p:DebugSymbols=false', "-p:Version=$Version"
    )
    dotnet publish src/GenshinPiano.App/GenshinPiano.App.csproj @publishOptions `
        -p:PublishSingleFile=true "-p:EnableCompressionInSingleFile=$($selfContained.ToString().ToLowerInvariant())" -o $staging
    if ($LASTEXITCODE -ne 0) { throw "Publishing the application failed ($LASTEXITCODE)." }

    if ($selfContained) {
        # Match the release BAT: keep the Native AOT updater's build files out of the app package.
        $updater = Join-Path $scratch 'updater'
        dotnet publish src/GenshinPiano.Updater/GenshinPiano.Updater.csproj @publishOptions `
            -p:PublishAot=true -p:StripSymbols=true -o $updater
        if ($LASTEXITCODE -ne 0) { throw "Publishing the Native AOT updater failed ($LASTEXITCODE)." }
        Copy-Item -LiteralPath (Join-Path $updater 'GenshinPiano.Updater.exe') -Destination $staging
    }
    else {
        dotnet publish src/GenshinPiano.Updater/GenshinPiano.Updater.csproj @publishOptions -p:PublishSingleFile=true -o $staging
        if ($LASTEXITCODE -ne 0) { throw "Publishing the updater failed ($LASTEXITCODE)." }
    }
    Copy-Item -LiteralPath (Join-Path $root 'publish/songs') -Destination $staging -Recurse

    if ($IncludeOcrResources) {
        & (Join-Path $PSScriptRoot 'Publish-OcrResources.ps1') `
            -AddonRootDirectory (Join-Path $staging 'addons') -ArtifactsDirectory (Join-Path $scratch 'ocr-output') `
            -BuildArtifactsDirectory (Join-Path $scratch 'ocr-build') -SkipSigning -SkipArchive
    }

    foreach ($entry in @('GenshinPiano.exe', 'GenshinPiano.Updater.exe', 'songs')) {
        if (-not (Test-Path -LiteralPath (Join-Path $staging $entry))) {
            throw "Package is missing $entry."
        }
    }
    $sdkVersion = (& dotnet --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Could not determine the .NET SDK version.' }
    $commit = (& git -c "safe.directory=$root" rev-parse HEAD | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Could not determine the source commit; use a Git clone.' }
    $sourceStatus = @(& git -c "safe.directory=$root" status --porcelain --untracked-files=normal)
    if ($LASTEXITCODE -ne 0) { throw 'Could not determine the source working-tree status.' }
    $resourceVersion = if ($IncludeOcrResources) {
        (Get-Content -LiteralPath (Join-Path $staging 'addons/ocr/manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json).resourceVersion
    } else { $null }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $package)
    $hash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($package))" | Set-Content -LiteralPath "$package.sha256" -Encoding ASCII
    [ordered]@{
        version = $Version
        packageType = $PackageType
        runtimeIdentifier = 'win-x64'
        sourceCommit = $commit
        sourceWorkingTreeDirty = $sourceStatus.Count -gt 0
        dotnetSdk = $sdkVersion
        ocrResourceVersion = $resourceVersion
        signed = $false
        sha256 = $hash
    } | ConvertTo-Json | Set-Content -LiteralPath "$package.build-info.json" -Encoding UTF8
    Write-Output "Unsigned package: $package"
    Write-Output "SHA-256: $hash"
}
finally {
    Pop-Location
    # Only remove the freshly created staging directory directly under the requested output root.
    $resolved = (Resolve-Path -LiteralPath $scratch).ProviderPath
    $resolvedOutput = (Resolve-Path -LiteralPath $output).ProviderPath
    if ([IO.Path]::GetDirectoryName($resolved) -eq $resolvedOutput.TrimEnd('\', '/')) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
