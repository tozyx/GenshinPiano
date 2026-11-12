#Requires -Version 7.0
param(
    [Parameter(Mandatory = $true)][string]$ImagePath,
    [ValidateSet('System', 'Page')][string]$Model = 'System',
    [string]$TrialDirectory,
    [string]$OutputDirectory,
    [ValidateRange(1, 64)][int]$Threads = 4,
    [ValidateRange(1, 3600)][int]$TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
if ([string]::IsNullOrWhiteSpace($TrialDirectory)) {
    $TrialDirectory = Join-Path $repositoryRoot 'publish\smt-experiment'
}
$trialRoot = [IO.Path]::GetFullPath($TrialDirectory)
$image = [IO.Path]::GetFullPath($ImagePath)
if (-not (Test-Path -LiteralPath $image -PathType Leaf)) {
    throw "Image does not exist: $image"
}

# These are the three manually downloaded artifacts, not similarly named partial downloads.
$modelName = if ($Model -eq 'System') { 'smt-grandstaff-q8_0.gguf' } else { 'smt-fp-grandstaff-q8_0.gguf' }
$modelHash = if ($Model -eq 'System') {
    'f6612885a8d7015bef5ab7f045a37a0f667575f4b65b324a353cf348d6edc752'
} else {
    '2f53c0a93c0c2f7c289f69dcc10b36464a5bfed07ad2ed89df42582d5675272c'
}
$archivePath = Join-Path $trialRoot 'crispembed-windows-x86_64.zip'
$modelPath = Join-Path $trialRoot $modelName
if (-not (Test-Path -LiteralPath $modelPath -PathType Leaf)) {
    $modelPath = Join-Path (Join-Path $trialRoot 'models') $modelName
}
$artifacts = @(
    @{ Path = $archivePath; Hash = 'e82337bcdefb1d54b0d97c26fc7e64ff35847d1db285e5062d6674df91ce6017' },
    @{ Path = $modelPath; Hash = $modelHash }
)
foreach ($artifact in $artifacts) {
    if (-not (Test-Path -LiteralPath $artifact.Path -PathType Leaf)) {
        throw "Download the artifact first: $($artifact.Path). See tools/README.md for resource setup."
    }
    if ((Get-FileHash -LiteralPath $artifact.Path -Algorithm SHA256).Hash -ne $artifact.Hash) {
        throw "Artifact SHA-256 mismatch: $($artifact.Path)"
    }
}

# Re-extract the verified CPU package, including its matching DLLs and notices.
$nativeRoot = Join-Path $trialRoot 'native'
Expand-Archive -LiteralPath $archivePath -DestinationPath $nativeRoot -Force
$executable = Join-Path $nativeRoot 'crispembed.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'The verified archive does not contain crispembed.exe.'
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $runId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $OutputDirectory = Join-Path $trialRoot "results\$runId"
}
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
# Every run owns a new directory, so evidence from an earlier run is never overwritten.
if (Test-Path -LiteralPath $outputRoot) {
    throw "OutputDirectory already exists; choose a new directory: $outputRoot"
}
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $executable
$startInfo.WorkingDirectory = $nativeRoot
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.StandardOutputEncoding = [Text.Encoding]::UTF8
$startInfo.StandardErrorEncoding = [Text.Encoding]::UTF8
$startInfo.Environment['SMT_OCR_FORCE_CPU'] = '1'
foreach ($argument in @('-m', $modelPath, '-t', [string]$Threads, '--ocr', $image)) {
    $startInfo.ArgumentList.Add($argument)
}
$process = [Diagnostics.Process]::new()
$process.StartInfo = $startInfo
$watch = [Diagnostics.Stopwatch]::StartNew()
$timedOut = $false
$started = $false
try {
    if (-not $process.Start()) { throw 'Unable to start SMT.' }
    $started = $true
    # Drain both streams while waiting; a verbose backend cannot block on a full pipe.
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    while (-not $process.WaitForExit(250)) {
        if ($watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
            $timedOut = $true
            $process.Kill($true)
            $process.WaitForExit()
            break
        }
    }
    $watch.Stop()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    $exitCode = $process.ExitCode
    [IO.File]::WriteAllText((Join-Path $outputRoot 'recognized.tokens.txt'), $stdout, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $outputRoot 'stderr.log'), $stderr, [Text.UTF8Encoding]::new($false))
    $metrics = [ordered]@{
        startedAt = $process.StartTime.ToUniversalTime().ToString('O')
        runtimeVersion = 'CrispEmbed v0.17.18 CPU'
        runtimeArchiveSha256 = $artifacts[0].Hash
        model = $modelName
        modelSha256 = $modelHash
        modelBytes = (Get-Item -LiteralPath $modelPath).Length
        image = $image
        imageSha256 = (Get-FileHash -LiteralPath $image -Algorithm SHA256).Hash.ToLowerInvariant()
        threads = $Threads
        elapsedSeconds = [Math]::Round($watch.Elapsed.TotalSeconds, 3)
        exitCode = $exitCode
        timedOut = $timedOut
        outputCharacters = $stdout.Length
        # Exit code zero does not establish valid notation, coverage or accuracy.
        recognitionQualityVerified = $false
    }
    $metrics | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputRoot 'metrics.json') -Encoding utf8
    Write-Host "SMT evidence: $outputRoot"
    $metrics | ConvertTo-Json
    if ($timedOut) { throw "SMT exceeded $TimeoutSeconds seconds; partial output was retained." }
    if ($exitCode -ne 0) { throw "SMT exited with code $exitCode; inspect stderr.log." }
    if ([string]::IsNullOrWhiteSpace($stdout)) { throw 'SMT returned no notation.' }
}
finally {
    if ($started -and -not $process.HasExited) {
        $process.Kill($true)
        $process.WaitForExit()
    }
    $process.Dispose()
}
