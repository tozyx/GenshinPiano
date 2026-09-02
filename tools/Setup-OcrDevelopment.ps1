param(
    [string]$ResearchDirectory,
    [string]$PythonExecutable,
    [switch]$SkipDependencyInstall
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($ResearchDirectory)) {
    $ResearchDirectory = Join-Path $repositoryRoot "..\_research"
}
$researchDirectory = [IO.Path]::GetFullPath($ResearchDirectory)
$oemerDirectory = Join-Path $researchDirectory "oemer"
$venvDirectory = Join-Path $oemerDirectory ".venv"
$venvPython = Join-Path $venvDirectory "Scripts\python.exe"
$oemerRepository = "https://github.com/BreezeWhite/oemer.git"
$oemerCommit = "dbe2a933d630d0f74805d717960eb259473f5978"
$requirementsPath = Join-Path $PSScriptRoot "requirements-ocr-development.txt"
$patchPath = Join-Path $PSScriptRoot "ocr\oemer-inference.patch"

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Git failed with exit code ${LASTEXITCODE}: git $($Arguments -join ' ')"
    }
}

function Get-FileWithHash {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [Parameter(Mandatory = $true)][string]$Destination,
        [Parameter(Mandatory = $true)][string]$Sha256
    )

    if (Test-Path -LiteralPath $Destination -PathType Leaf) {
        $currentHash = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash
        if ($currentHash.Equals($Sha256, [StringComparison]::OrdinalIgnoreCase)) {
            Write-Host "Model already verified: $Destination"
            return
        }
    }

    $destinationDirectory = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    $partialPath = "$Destination.download"
    Remove-Item -LiteralPath $partialPath -Force -ErrorAction SilentlyContinue
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            Write-Host "Downloading model ($attempt/3): $Uri"
            Invoke-WebRequest -Uri $Uri -OutFile $partialPath -UseBasicParsing
            $downloadHash = (Get-FileHash -LiteralPath $partialPath -Algorithm SHA256).Hash
            if (-not $downloadHash.Equals($Sha256, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Model checksum mismatch. Expected $Sha256, got $downloadHash."
            }
            Move-Item -LiteralPath $partialPath -Destination $Destination -Force
            return
        }
        catch {
            Remove-Item -LiteralPath $partialPath -Force -ErrorAction SilentlyContinue
            if ($attempt -eq 3) { throw }
            Start-Sleep -Seconds 2
        }
    }
}

New-Item -ItemType Directory -Path $researchDirectory -Force | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $oemerDirectory ".git") -PathType Container)) {
    if (Test-Path -LiteralPath $oemerDirectory) {
        throw "The Oemer target exists but is not a Git checkout: $oemerDirectory"
    }
    Invoke-Git clone $oemerRepository $oemerDirectory
}

$remote = (& git -C $oemerDirectory remote get-url origin).Trim()
if ($LASTEXITCODE -ne 0 -or $remote -ne $oemerRepository) {
    throw "Unexpected Oemer origin '$remote'. Expected '$oemerRepository'."
}
$head = (& git -C $oemerDirectory rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw "Unable to read the Oemer revision." }
if ($head -ne $oemerCommit) {
    $dirty = & git -C $oemerDirectory status --porcelain
    if ($dirty) {
        throw "Oemer has local changes and is not at the supported commit. Move or commit those changes first."
    }
    Invoke-Git "-C" $oemerDirectory fetch origin $oemerCommit
    Invoke-Git "-C" $oemerDirectory checkout --detach $oemerCommit
}

& git -C $oemerDirectory apply --reverse --check $patchPath 2>$null
if ($LASTEXITCODE -ne 0) {
    & git -C $oemerDirectory apply --check $patchPath
    if ($LASTEXITCODE -ne 0) {
        throw "The GenshinPiano Oemer compatibility patch cannot be applied."
    }
    Invoke-Git "-C" $oemerDirectory apply $patchPath
}
else {
    Write-Host "Oemer compatibility patch is already applied."
}

Get-FileWithHash `
    -Uri "https://github.com/BreezeWhite/oemer/releases/download/checkpoints/1st_model.onnx" `
    -Destination (Join-Path $oemerDirectory "oemer\checkpoints\unet_big\model.onnx") `
    -Sha256 "37512E858731096439746F60B377C049F07055B4A23EC6EB9A178CE92CFBA174"
Get-FileWithHash `
    -Uri "https://github.com/BreezeWhite/oemer/releases/download/checkpoints/2nd_model.onnx" `
    -Destination (Join-Path $oemerDirectory "oemer\checkpoints\seg_net\model.onnx") `
    -Sha256 "ED2E1A86EA75712EE6CDC740E96F7A36753543CF9BB980227C071C9256D9D82E"

if (-not (Test-Path -LiteralPath $venvPython -PathType Leaf)) {
    if ([string]::IsNullOrWhiteSpace($PythonExecutable)) {
        $launcher = Get-Command py -ErrorAction SilentlyContinue
        if ($null -eq $launcher) {
            throw "Python 3.11 was not found. Install it or pass -PythonExecutable."
        }
        & $launcher.Source -3.11 -m venv $venvDirectory
    }
    else {
        & ([IO.Path]::GetFullPath($PythonExecutable)) -m venv $venvDirectory
    }
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $venvPython -PathType Leaf)) {
        throw "Failed to create the OCR Python virtual environment."
    }
}

$pythonVersion = (& $venvPython -c "import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}')").Trim()
if ($LASTEXITCODE -ne 0 -or $pythonVersion -ne "3.11") {
    throw "OCR development requires Python 3.11; found '$pythonVersion'."
}

if (-not $SkipDependencyInstall) {
    & $venvPython -m pip install --disable-pip-version-check -r $requirementsPath
    if ($LASTEXITCODE -ne 0) { throw "Failed to install OCR Python dependencies." }
    & $venvPython -m pip install --disable-pip-version-check --no-deps -e $oemerDirectory
    if ($LASTEXITCODE -ne 0) { throw "Failed to install Oemer in editable mode." }
}

& $venvPython -c "import oemer,onnxruntime as ort; assert 'CPUExecutionProvider' in ort.get_available_providers(); print('Oemer development environment ready:', oemer.__path__[0], ort.__version__)"
if ($LASTEXITCODE -ne 0) { throw "The OCR development environment failed validation." }

$engineProject = Join-Path $repositoryRoot "src\GenshinPiano.Ocr.Engine\GenshinPiano.Ocr.Engine.csproj"
& dotnet build $engineProject -c Debug
if ($LASTEXITCODE -ne 0) { throw "The OCR engine Debug build failed." }

$engineOutput = Join-Path $repositoryRoot "src\GenshinPiano.Ocr.Engine\bin\Debug\net10.0-windows"
$addonDirectory = Join-Path $repositoryRoot "src\GenshinPiano.App\bin\Debug\net10.0-windows\addons\ocr"
if (Test-Path -LiteralPath $addonDirectory) {
    Remove-Item -LiteralPath $addonDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $addonDirectory -Force | Out-Null
Get-ChildItem -LiteralPath $engineOutput -Force |
    Copy-Item -Destination $addonDirectory -Recurse -Force

& (Join-Path $PSScriptRoot "Publish-OcrPythonRuntime.ps1") `
    -DestinationDirectory (Join-Path $addonDirectory "staff-omr\python") `
    -PythonEnvironmentDirectory $venvDirectory `
    -OemerSourceDirectory (Join-Path $oemerDirectory "oemer")
if ($LASTEXITCODE -ne 0) { throw "The portable OCR Python runtime build failed." }

Write-Host ""
Write-Host "OCR development add-on is ready:"
Write-Host $addonDirectory
