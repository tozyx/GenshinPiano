param(
    [Parameter(Mandatory = $true)]
    [string]$DestinationDirectory,

    [string]$PythonEnvironmentDirectory,

    [string]$OemerSourceDirectory
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($PythonEnvironmentDirectory)) {
    $PythonEnvironmentDirectory = Join-Path $repositoryRoot "..\_research\oemer\.venv"
}
if ([string]::IsNullOrWhiteSpace($OemerSourceDirectory)) {
    $OemerSourceDirectory = Join-Path $repositoryRoot "..\_research\oemer\oemer"
}

$pythonEnvironmentDirectory = [IO.Path]::GetFullPath($PythonEnvironmentDirectory)
$oemerSourceDirectory = [IO.Path]::GetFullPath($OemerSourceDirectory)
$destinationDirectory = [IO.Path]::GetFullPath($DestinationDirectory)
$venvPython = Join-Path $pythonEnvironmentDirectory "Scripts\python.exe"
if (-not (Test-Path -LiteralPath $venvPython -PathType Leaf)) {
    throw "OCR Python environment was not found: $venvPython"
}
if (-not (Test-Path -LiteralPath (Join-Path $oemerSourceDirectory "__init__.py") -PathType Leaf)) {
    throw "Oemer source package was not found: $oemerSourceDirectory"
}

$environmentJson = & $venvPython -c `
    "import json,site,sys; print(json.dumps({'base':sys.base_prefix,'site':site.getsitepackages()[-1]}))"
if ($LASTEXITCODE -ne 0) {
    throw "Failed to inspect the OCR Python environment."
}
$environment = $environmentJson | ConvertFrom-Json
$baseDirectory = [IO.Path]::GetFullPath([string]$environment.base)
$sitePackagesDirectory = [IO.Path]::GetFullPath([string]$environment.site)
if (-not (Test-Path -LiteralPath (Join-Path $baseDirectory "python.exe") -PathType Leaf) -or
    -not (Test-Path -LiteralPath $sitePackagesDirectory -PathType Container)) {
    throw "The OCR Python environment is incomplete."
}

function Copy-DirectoryWithRobocopy {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination,
        [string[]]$ExcludeDirectories = @(),
        [string[]]$ExcludeFiles = @()
    )

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    $arguments = @($Source, $Destination, "/E", "/NFL", "/NDL", "/NJH", "/NJS", "/NP", "/R:2", "/W:1")
    if ($ExcludeDirectories.Count -gt 0) {
        $arguments += "/XD"
        $arguments += $ExcludeDirectories
    }
    if ($ExcludeFiles.Count -gt 0) {
        $arguments += "/XF"
        $arguments += $ExcludeFiles
    }

    & robocopy @arguments | Out-Host
    if ($LASTEXITCODE -ge 8) {
        throw "Robocopy failed with exit code $LASTEXITCODE while copying '$Source'."
    }
}

if (Test-Path -LiteralPath $destinationDirectory) {
    Remove-Item -LiteralPath $destinationDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null

foreach ($fileName in @(
    "python.exe",
    "pythonw.exe",
    "python3.dll",
    "python311.dll",
    "vcruntime140.dll",
    "vcruntime140_1.dll",
    "LICENSE.txt")) {
    $sourcePath = Join-Path $baseDirectory $fileName
    if (Test-Path -LiteralPath $sourcePath -PathType Leaf) {
        Copy-Item -LiteralPath $sourcePath -Destination $destinationDirectory -Force
    }
}

Copy-DirectoryWithRobocopy `
    -Source (Join-Path $baseDirectory "DLLs") `
    -Destination (Join-Path $destinationDirectory "DLLs") `
    -ExcludeDirectories @("__pycache__") `
    -ExcludeFiles @(
        "*.pyc",
        "_test*.pyd",
        "_ctypes_test.pyd",
        "_tkinter.pyd",
        "_sqlite3.pyd",
        "tcl*.dll",
        "tk*.dll",
        "sqlite3.dll",
        "*.ico",
        "*.cat")
Copy-DirectoryWithRobocopy `
    -Source (Join-Path $baseDirectory "Lib") `
    -Destination (Join-Path $destinationDirectory "Lib") `
    -ExcludeDirectories @(
        "site-packages",
        "__pycache__",
        "test",
        "tests",
        "ensurepip",
        "idlelib",
        "tkinter",
        "pydoc_data",
        "distutils",
        "venv",
        "lib2to3") `
    -ExcludeFiles @("*.pyc")

$portableSitePackages = Join-Path $destinationDirectory "Lib\site-packages"
Copy-DirectoryWithRobocopy `
    -Source $sitePackagesDirectory `
    -Destination $portableSitePackages `
    -ExcludeDirectories @(
        "__pycache__",
        "test",
        "tests",
        "oemer",
        "onnxruntime-gpu",
        "nvidia",
        "pip",
        "setuptools",
        "pkg_resources",
        "_distutils_hack",
        "sympy",
        "mpmath",
        "matplotlib",
        "matplotlib.libs",
        "mpl_toolkits",
        "fontTools",
        "contourpy",
        "cycler",
        "kiwisolver",
        "dateutil",
        "pyparsing") `
    -ExcludeFiles @(
        "*.pyc",
        "*.pyi",
        "py.typed",
        "*.whl",
        "__editable__*oemer*",
        "distutils-precedence.pth",
        "isympy.py",
        "pylab.py")
Copy-DirectoryWithRobocopy `
    -Source $oemerSourceDirectory `
    -Destination (Join-Path $portableSitePackages "oemer") `
    -ExcludeDirectories @("__pycache__") `
    -ExcludeFiles @("*.pyc")

# Oemer imports pyplot at module load, but only uses it in optional developer
# visualization helpers. Keep those imports valid without shipping matplotlib
# and its font/rendering dependency tree in the end-user inference package.
$matplotlibStub = Join-Path $portableSitePackages "matplotlib"
New-Item -ItemType Directory -Path $matplotlibStub -Force | Out-Null
[IO.File]::WriteAllText(
    (Join-Path $matplotlibStub "__init__.py"),
    "from . import pyplot`n",
    [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText(
    (Join-Path $matplotlibStub "pyplot.py"),
    @"
def __getattr__(name):
    def unavailable(*args, **kwargs):
        raise RuntimeError("Matplotlib diagnostics are not included in the OCR runtime.")
    return unavailable
"@,
    [Text.UTF8Encoding]::new($false))

foreach ($relativePath in @(
    "onnxruntime\backend",
    "onnxruntime\datasets",
    "onnxruntime\quantization",
    "onnxruntime\tools",
    "onnxruntime\transformers")) {
    $toolPath = Join-Path $portableSitePackages $relativePath
    if (Test-Path -LiteralPath $toolPath) {
        Remove-Item -LiteralPath $toolPath -Recurse -Force
    }
}

foreach ($metadataPattern in @(
    "pip-*.dist-info",
    "setuptools-*.dist-info",
    "sympy-*.dist-info",
    "mpmath-*.dist-info",
    "matplotlib-*.dist-info",
    "fonttools-*.dist-info",
    "contourpy-*.dist-info",
    "cycler-*.dist-info",
    "kiwisolver-*.dist-info",
    "python_dateutil-*.dist-info",
    "pyparsing-*.dist-info")) {
    Get-ChildItem -LiteralPath $portableSitePackages -Directory -Filter $metadataPattern |
        Remove-Item -Recurse -Force
}

$portablePython = Join-Path $destinationDirectory "python.exe"
$validation = & $portablePython -B -c `
    "import json,os,oemer,onnxruntime as ort; from oemer import ete; root=os.path.join(os.path.dirname(oemer.__file__),'checkpoints'); sessions=[ort.InferenceSession(os.path.join(root,name,'model.onnx'),providers=['CPUExecutionProvider']) for name in ('unet_big','seg_net')]; print(json.dumps({'oemer':oemer.__path__[0],'ort':ort.__version__,'providers':ort.get_available_providers(),'modelsCpuOnly':all(session.get_providers()==['CPUExecutionProvider'] for session in sessions)}))"
if ($LASTEXITCODE -ne 0) {
    throw "The packaged OCR Python runtime failed its import validation."
}
$validationData = $validation | ConvertFrom-Json
if ($validationData.providers -notcontains "CPUExecutionProvider" -or
    $validationData.providers -contains "CUDAExecutionProvider" -or
    $validationData.modelsCpuOnly -ne $true) {
    throw "The packaged OCR Python runtime does not contain the expected CPU-only provider."
}

$metadata = [ordered]@{
    pythonVersion = (& $portablePython -B -c "import platform; print(platform.python_version())")
    onnxRuntimeVersion = [string]$validationData.ort
    executionProvider = "CPUExecutionProvider"
}
$metadata | ConvertTo-Json | Set-Content `
    -LiteralPath (Join-Path $destinationDirectory "runtime.json") `
    -Encoding utf8

Get-ChildItem -LiteralPath $destinationDirectory -Recurse -Directory -Filter "__pycache__" |
    Remove-Item -Recurse -Force

$runtimeSize = (Get-ChildItem -LiteralPath $destinationDirectory -Recurse -File |
    Measure-Object -Property Length -Sum).Sum
Write-Host "Portable OCR Python runtime: $destinationDirectory"
Write-Host ("Runtime size: {0:N1} MB" -f ($runtimeSize / 1MB))
