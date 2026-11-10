[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\package-check'),
    [string]$Version = '0.0.0-test'
)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$') {
    throw "Invalid package version: $Version"
}
$root = Split-Path -Parent $PSScriptRoot
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
$package = Join-Path $output "GenshinPiano-$Version-win-x64-framework-unsigned.zip"
$staging = Join-Path $output ([guid]::NewGuid().ToString('N'))
if (Test-Path -LiteralPath $package) { throw "Package already exists: $package" }

New-Item -ItemType Directory -Path $staging | Out-Null
Push-Location $root
try {
    $publishOptions = @(
        '-c', 'Release', '-r', 'win-x64', '--self-contained', 'false',
        '-p:PublishSingleFile=true', '-p:NuGetAudit=false', '-p:DebugType=None',
        "-p:Version=$Version", '-o', $staging
    )
    foreach ($project in @('GenshinPiano.App', 'GenshinPiano.Updater')) {
        dotnet publish "src/$project/$project.csproj" @publishOptions
        if ($LASTEXITCODE -ne 0) { throw "Publishing $project failed ($LASTEXITCODE)." }
    }
    Copy-Item -LiteralPath (Join-Path $root 'publish/songs') -Destination $staging -Recurse

    foreach ($entry in @('GenshinPiano.exe', 'GenshinPiano.Updater.exe', 'songs')) {
        if (-not (Test-Path -LiteralPath (Join-Path $staging $entry))) {
            throw "Package is missing $entry."
        }
    }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $package)
    Write-Output "Unsigned test package: $package"
}
finally {
    Pop-Location
    # Only remove the freshly created staging directory directly under the requested output root.
    $resolved = (Resolve-Path -LiteralPath $staging).ProviderPath
    $resolvedOutput = (Resolve-Path -LiteralPath $output).ProviderPath
    if ([IO.Path]::GetDirectoryName($resolved) -eq $resolvedOutput.TrimEnd('\', '/')) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
