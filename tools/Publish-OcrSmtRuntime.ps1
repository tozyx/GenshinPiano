#Requires -Version 7.0
param(
    [Parameter(Mandatory = $true)][string]$DestinationDirectory,
    [string]$SmtTrialDirectory,
    [string]$MsvcRuntimeDirectory
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($SmtTrialDirectory)) { $SmtTrialDirectory = Join-Path $repositoryRoot 'publish\smt-experiment' }
if ([string]::IsNullOrWhiteSpace($MsvcRuntimeDirectory)) { $MsvcRuntimeDirectory = Join-Path $env:WINDIR 'System32' }
$nativeDestination = [IO.Path]::GetFullPath($DestinationDirectory)
if ([IO.Path]::GetFileName($nativeDestination.TrimEnd('\')) -ne 'smt') { throw 'DestinationDirectory must be named smt.' }
$nativeArchive = Join-Path $SmtTrialDirectory 'crispembed-windows-x86_64.zip'
$modelName = 'smt-grandstaff-q8_0.gguf'
$modelPath = Join-Path $SmtTrialDirectory $modelName
if (-not (Test-Path -LiteralPath $modelPath -PathType Leaf)) { $modelPath = Join-Path (Join-Path $SmtTrialDirectory 'models') $modelName }
if ((Get-FileHash -LiteralPath $nativeArchive -Algorithm SHA256).Hash -ne 'e82337bcdefb1d54b0d97c26fc7e64ff35847d1db285e5062d6674df91ce6017') { throw 'SMT CPU archive SHA-256 mismatch.' }
if ((Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash -ne 'f6612885a8d7015bef5ab7f045a37a0f667575f4b65b324a353cf348d6edc752') { throw 'SMT model SHA-256 mismatch.' }
New-Item -ItemType Directory -Path $nativeDestination -Force | Out-Null
$nativeFiles = @('crispembed.exe','crispembed.dll','ggml.dll','ggml-base.dll','ggml-cpu.dll')
$archive = [IO.Compression.ZipFile]::OpenRead($nativeArchive)
try {
    foreach ($entry in $archive.Entries) {
        if ($entry.Name -in $nativeFiles -or $entry.Name -match '^(LICENSE|NOTICE|Apache-2\.0|SheenBidi-LICENSE)' -or $entry.Name -eq 'README.md') {
            $input = $entry.Open()
            try { $output = [IO.File]::Create((Join-Path $nativeDestination $entry.Name)); try { $input.CopyTo($output) } finally { $output.Dispose() } } finally { $input.Dispose() }
        }
    }
} finally { $archive.Dispose() }
Copy-Item -LiteralPath $modelPath -Destination (Join-Path $nativeDestination $modelName) -Force
# App-local x64 VC runtime avoids requiring a separate redistributable install.
foreach ($name in @('msvcp140.dll','vcruntime140.dll','vcruntime140_1.dll')) {
    $source = Join-Path $MsvcRuntimeDirectory $name
    $reader = [IO.BinaryReader]::new([IO.File]::OpenRead($source))
    try {
        $reader.BaseStream.Position = 0x3c
        $header = $reader.ReadInt32()
        $reader.BaseStream.Position = $header + 4
        if ($reader.ReadUInt16() -ne 0x8664) { throw "Expected x64 runtime DLL: $name" }
    } finally { $reader.Dispose() }
    Copy-Item -LiteralPath $source -Destination (Join-Path $nativeDestination $name) -Force
}
[ordered]@{
    runtime = 'CrispEmbed v0.17.18 CPU'; runtimeSource = 'https://github.com/CrispStrobe/CrispEmbed/releases/tag/v0.17.18'
    model = $modelName; modelSource = 'https://huggingface.co/cstr/smt-grandstaff-GGUF/tree/8e1595591659aa03cf1e71aab5aa03e8f3fccd7e'
    modelSha256 = 'f6612885a8d7015bef5ab7f045a37a0f667575f4b65b324a353cf348d6edc752'
    scope = 'Clear flat piano scans/rendered images; photos unsupported'
    appLocalRuntime = 'Microsoft Visual C++ runtime: msvcp140, vcruntime140, vcruntime140_1 (x64)'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $nativeDestination 'provenance.json') -Encoding utf8
foreach ($name in $nativeFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $nativeDestination $name) -PathType Leaf)) { throw "Missing SMT native file: $name" }
}
